using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;

namespace DirSize.Services;

/// <summary>一份快照：某个盘整棵目录树中每个文件夹的大小。</summary>
public sealed class SnapshotData
{
    public string Drive { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    // 键为完整目录路径。反序列化后会退化为默认比较器的字典，
    // 对比逻辑统一用 OrdinalIgnoreCase 显式比较，不依赖此字典的比较器。
    public Dictionary<string, long> Folders { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>快照文件列表里的一条摘要（从文件名解析，不读内容）。</summary>
public sealed record SnapshotEntry(string FilePath, string DriveLabel, DateTime CreatedAt)
{
    public string Display => $"{DriveLabel}  {CreatedAt:yyyy-MM-dd HH:mm:ss}";
}

/// <summary>
/// 快照的创建与读写。快照保存到 当前用户目录\DirSizeSnapshots，
/// 文件名 C_yyyyMMdd_HHmmss.json（C 为盘符字母）。
/// </summary>
public static class SnapshotService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = false,
    };

    public static string SnapshotDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "DirSizeSnapshots");

    /// <summary>后台扫描整个盘并把每个目录的大小写入快照文件，返回快照文件路径。
    /// onFolderSize 可选：每算完一个目录就回调一次，供调用方同步填充界面缓存。</summary>
    public static async Task<string> CreateAsync(
        string driveRoot, Action<string, long>? onFolderSize,
        IProgress<ScanProgress>? progress, CancellationToken ct)
    {
        var sizes = new ConcurrentDictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var scanner = new NativeDirectoryScanner();
        await scanner.ScanFolderAsync(driveRoot, (path, size) =>
        {
            sizes[path] = size;
            onFolderSize?.Invoke(path, size);
        }, progress, ct);

        var trimmed = driveRoot.TrimEnd('\\', '/');
        var letter = trimmed.Length >= 2 ? trimmed[..1].ToUpperInvariant() : "Drive";
        var data = new SnapshotData
        {
            Drive = driveRoot,
            CreatedAt = DateTime.Now,
        };
        foreach (var kv in sizes) data.Folders[kv.Key] = kv.Value;

        Directory.CreateDirectory(SnapshotDir);
        var file = Path.Combine(SnapshotDir, $"{letter}_{data.CreatedAt:yyyyMMdd_HHmmss}.json");
        await File.WriteAllTextAsync(file, JsonSerializer.Serialize(data, JsonOpts), ct);
        return file;
    }

    /// <summary>列出所有快照（按时间倒序，新的在前）。文件名损坏的条目会被跳过。</summary>
    public static List<SnapshotEntry> ListSnapshots()
    {
        var result = new List<SnapshotEntry>();
        if (!Directory.Exists(SnapshotDir)) return result;
        foreach (var f in Directory.GetFiles(SnapshotDir, "*.json"))
        {
            var name = Path.GetFileNameWithoutExtension(f);
            var sep = name.IndexOf('_');
            if (sep <= 0) continue;
            var drive = name[..sep];
            if (!DateTime.TryParseExact(name[(sep + 1)..], "yyyyMMdd_HHmmss",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var t)) continue;
            result.Add(new SnapshotEntry(f, drive + "盘", t));
        }
        result.Sort((a, b) => b.CreatedAt.CompareTo(a.CreatedAt));
        return result;
    }

    /// <summary>读取快照文件。损坏或缺失时抛出异常，由调用方提示。</summary>
    public static Task<SnapshotData> LoadAsync(string filePath)
        => Task.Run(() =>
        {
            var data = JsonSerializer.Deserialize<SnapshotData>(File.ReadAllText(filePath));
            if (data == null) throw new InvalidDataException("快照文件格式不正确");
            return data;
        });
}
