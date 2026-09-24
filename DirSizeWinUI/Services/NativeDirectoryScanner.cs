using System.IO;
using System.Runtime.InteropServices;
using DirSize.Models;

namespace DirSize.Services;

/// <summary>
/// 基于 Win32 原生 API（FindFirstFileExW/FindNextFileW）的目录扫描器。
/// 用 FIND_FIRST_EX_LARGE_FETCH 批量读取、跳过重解析点避免环，并对顶层子目录并行递归。
/// WIN32_FIND_DATA 用 IntPtr + 手动偏移读取字段，避免 ByValTStr 字符串封送在部分环境下的错乱。
/// </summary>
public sealed class NativeDirectoryScanner
{
    private const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;
    private const uint FILE_ATTRIBUTE_REPARSE_POINT = 0x400;
    private const uint FIND_FIRST_EX_LARGE_FETCH = 0x00000002;
    private const int FIND_DATA_SIZE = 592; // sizeof(WIN32_FIND_DATAW)
    private static readonly IntPtr INVALID_HANDLE_VALUE = new(-1);

    // WIN32_FIND_DATAW 关键字段偏移（均为 4 字节整数）。
    private const int OFF_ATTR = 0;
    private const int OFF_SIZE_HIGH = 28;
    private const int OFF_SIZE_LOW = 32;
    private const int OFF_NAME = 44;

    #region P/Invoke（不依赖结构体字符串封送，直接操作缓冲区指针）

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr FindFirstFileExW(
        string lpFileName,
        int fInfoLevelId,      // FINDEX_INFO_LEVELS.FindExInfoBasic = 1
        IntPtr lpFindFileData,
        int fSearchOp,         // FINDEX_SEARCH_OPS.FindExSearchNameMatch = 0
        IntPtr lpSearchFilter,
        uint dwAdditionalFlags);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FindNextFileW(IntPtr hFindFile, IntPtr lpFindFileData);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FindClose(IntPtr hFindFile);

    #endregion

    /// <summary>后台扫描 <paramref name="rootPath"/>：算出整棵子树每个文件夹的大小，并经 onFolderSize 逐节点写缓存。</summary>
    public Task<DirectoryNode> ScanFolderAsync(
        string rootPath,
        Action<string, long>? onFolderSize,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken)
        => Task.Run(() => ScanFolder(rootPath, onFolderSize, progress, cancellationToken), cancellationToken);

    /// <summary>只列出某个目录的直接子目录（快速构建树形结构，不算大小）。</summary>
    public static string[] ListSubdirectories(string dir)
    {
        var result = new List<string>();
        Walk(dir, (name, isDir) =>
        {
            if (isDir) result.Add(Path.Combine(dir, name));
        });
        return result.ToArray();
    }

    private static DirectoryNode ScanFolder(
        string rootPath, Action<string, long>? onFolderSize,
        IProgress<ScanProgress>? progress, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var root = NewNode(rootPath);

        var subdirs = new List<string>();
        (long files, long bytes) = Enumerate(rootPath, subdirs);
        root.FileCount = files;
        root.Size = bytes;
        root.FolderCount = 1;

        // 顶层子目录并行递归，可大幅缩短大目录耗时。
        // 每个顶层子树一算完就立刻上报，供界面实时刷新对应行的大小（不阻塞处理流）。
        DirectoryNode[] children = subdirs.Count == 0
            ? Array.Empty<DirectoryNode>()
            : subdirs.AsParallel().WithCancellation(ct).Select(s =>
            {
                var c = ScanSubtree(s, onFolderSize, progress, ct);
                progress?.Report(new ScanProgress
                {
                    CurrentPath = s,
                    CompletedPath = c.FullPath,
                    CompletedSize = c.Size,
                    CompletedFolders = c.FolderCount,
                    CompletedFiles = c.FileCount,
                });
                return c;
            }).ToArray();

        foreach (var c in children)
        {
            root.Children.Add(c);
            root.Size += c.Size;
            root.FileCount += c.FileCount;
            root.FolderCount += c.FolderCount + 1;
        }

        // 根目录也上报一次完成，让“当前目标”行的大小能随扫描结束刷新到最终值。
        progress?.Report(new ScanProgress
        {
            CurrentPath = rootPath,
            CompletedPath = root.FullPath,
            CompletedSize = root.Size,
            CompletedFolders = root.FolderCount,
            CompletedFiles = root.FileCount,
        });

        onFolderSize?.Invoke(rootPath, root.Size);
        return root;
    }

    private static DirectoryNode ScanSubtree(
        string path, Action<string, long>? onFolderSize,
        IProgress<ScanProgress>? progress, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var node = NewNode(path);

        var subdirs = new List<string>();
        (long files, long bytes) = Enumerate(path, subdirs);
        node.FileCount = files;
        node.Size = bytes;
        node.FolderCount = 1;

        progress?.Report(new ScanProgress { CurrentPath = path, FilesScanned = files });

        foreach (var s in subdirs)
        {
            var child = ScanSubtree(s, onFolderSize, progress, ct);
            node.Children.Add(child);
            node.Size += child.Size;
            node.FileCount += child.FileCount;
            node.FolderCount += child.FolderCount + 1;
        }

        onFolderSize?.Invoke(path, node.Size);
        return node;
    }

    /// <summary>枚举目录项：返回文件的累计大小与数量，同时把子目录路径写入 <paramref name="subdirs"/>。</summary>
    private static (long Files, long Bytes) Enumerate(string dir, List<string> subdirs)
    {
        long files = 0, bytes = 0;
        Walk(dir,
            (name, isDir) =>
            {
                if (isDir) subdirs.Add(Path.Combine(dir, name));
            },
            size => { bytes += size; files++; });
        return (files, bytes);
    }

    /// <summary>
    /// 遍历目录各项；onEntry 收到 (名称, 是否为目录)；onFile 收到文件字节数。
    /// 用统一 IntPtr 缓冲读取，保证 name 解码正确。
    /// </summary>
    private static void Walk(
        string dir,
        Action<string, bool> onEntry,
        Action<long>? onFile = null)
    {
        string search = dir.EndsWith('\\') ? dir + "*" : dir + "\\*";
        IntPtr buffer = Marshal.AllocHGlobal(FIND_DATA_SIZE);
        try
        {
            IntPtr h = FindFirstFileExW(search, 1, buffer, 0, IntPtr.Zero, FIND_FIRST_EX_LARGE_FETCH);
            if (h == INVALID_HANDLE_VALUE) return;
            try
            {
                do
                {
                    string name = Marshal.PtrToStringUni(IntPtr.Add(buffer, OFF_NAME)) ?? "";
                    if (name.Length == 0 || name == "." || name == "..") continue;

                    uint attr = (uint)Marshal.ReadInt32(buffer, OFF_ATTR);
                    if ((attr & FILE_ATTRIBUTE_REPARSE_POINT) != 0) continue; // 跳过链接/联接点，避免环

                    if ((attr & FILE_ATTRIBUTE_DIRECTORY) != 0)
                    {
                        onEntry(name, true);
                        continue;
                    }

                    long size = ((long)(uint)Marshal.ReadInt32(buffer, OFF_SIZE_HIGH) << 32)
                                | (uint)Marshal.ReadInt32(buffer, OFF_SIZE_LOW);
                    if (onFile != null) onFile(size);
                    else onEntry(name, false);
                }
                while (FindNextFileW(h, buffer));
            }
            finally { FindClose(h); }
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private static DirectoryNode NewNode(string path)
    {
        var trimmed = path.TrimEnd('\\', '/');
        var name = Path.GetFileName(trimmed);
        if (string.IsNullOrEmpty(name)) name = path;
        return new DirectoryNode { Name = name, FullPath = path };
    }
}