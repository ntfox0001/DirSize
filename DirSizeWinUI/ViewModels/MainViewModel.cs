using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows.Input;
using DirSize.Mvvm;
using DirSize.Services;

namespace DirSize.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly NativeDirectoryScanner _scanner = new();
    private readonly SizeCache _cache = new();
    private CancellationTokenSource? _cts;

    private DriveOption? _selectedDrive;
    private DirectoryItemViewModel? _selectedDirectory;
    private DirectoryItemViewModel? _listSelection;
    private bool _isScanning;
    private string _progressText = Locale.T("请选择磁盘并点击“刷新”开始统计", "Select a drive and click Refresh to start");
    private string _statusText = "";
    private DirectoryItemViewModel? _activeTarget;

    public ObservableCollection<DriveOption> Drives { get; } = new();
    public ObservableCollection<DirectoryItemViewModel> Roots { get; } = new();
    public ObservableCollection<DirectoryItemViewModel> DirectoryList { get; } = new();

    // 界面静态文案（按系统语言在启动时定稿）。
    public string TextTitle => Locale.T("目录大小分析器", "Folder Size Analyzer");
    public string TextDisk => Locale.T("磁盘：", "Drive:");
    public string TextCancel => Locale.T("取消", "Cancel");
    public string TextRefresh => Locale.T("刷新（重新计算大小）", "Refresh (recalculate sizes)");
    public string TextOpenDir => Locale.T("打开目录", "Open Folder");
    public string TextOpenDirTip => Locale.T(
        "在资源管理器中打开当前选中的目录；未选中时打开右侧列表所在的目录",
        "Open the selected folder in Explorer; open the currently browsed folder if none is selected.");
    public string TextRefreshTip => Locale.T(
        "对左侧当前选中的目录后台重新计算大小并写入缓存",
        "Recalculate the size of the selected folder in the background and cache it.");
    public string TextStructureHeader => Locale.T("目录结构", "Folder Structure");
    public string TextSubDirHeader => Locale.T("子目录占用大小", "Subfolder Sizes");
    public string TextColName => Locale.T("名称", "Name");
    public string TextColSize => Locale.T("大小", "Size");
    public string TextColSubfolders => Locale.T("子目录数", "Subfolders");
    public string TextAskDoubao => Locale.T("发豆包", "Ask Doubao");
    public string TextAskDoubaoTip => Locale.T(
        "聚焦或打开本地豆包，把当前选中目录的占用情况发过去请它分析",
        "Focus/open the local Doubao app and send the selected folder's usage for analysis.");

    public AsyncRelayCommand RefreshCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand OpenDirectoryCommand { get; }

    public MainViewModel()
    {
        RefreshCommand = new AsyncRelayCommand(async _ => await RefreshAsync(), _ => !IsScanning);
        CancelCommand = new RelayCommand(_ => _cts?.Cancel(), _ => IsScanning);
        OpenDirectoryCommand = new RelayCommand(_ => OpenDirectory());

        foreach (var d in DriveInfo.GetDrives())
        {
            if (!d.IsReady) continue;
            var total = d.TotalSize > 0 ? ByteSizeFormatter.Format(d.TotalSize) : "?";
            Drives.Add(new DriveOption(d.Name, $"({total} 可用 {ByteSizeFormatter.Format(d.TotalFreeSpace)})"));
        }
    }

    /// <summary>窗口显示并完成布局后调用，此时树已落位，首屏才能正常展开首个盘符的目录结构。</summary>
    public void Initialize()
    {
        if (_selectedDrive == null && Drives.Count > 0)
            SelectedDrive = Drives[0];
    }

    public DriveOption? SelectedDrive
    {
        get => _selectedDrive;
        set
        {
            if (!SetProperty(ref _selectedDrive, value)) return;
            _cts?.Cancel();
            BuildStructure(value?.Root);
        }
    }

    public DirectoryItemViewModel? SelectedDirectory
    {
        get => _selectedDirectory;
        set
        {
            if (!SetProperty(ref _selectedDirectory, value)) return;
            ListSelection = null;
            DirectoryList.Clear();
            if (value != null)
                foreach (var child in value.Children)
                    DirectoryList.Add(child);
            OnPropertyChanged(nameof(SelectedSummary));
        }
    }

    /// <summary>右侧列表当前高亮（单选）的目录。双击进入时以它为目标；点“刷新”以它为目标重算大小。</summary>
    public DirectoryItemViewModel? ListSelection
    {
        get => _listSelection;
        set
        {
            if (!SetProperty(ref _listSelection, value)) return;
            OnPropertyChanged(nameof(SelectedSummary));
        }
    }

    public bool IsScanning
    {
        get => _isScanning;
        set
        {
            if (SetProperty(ref _isScanning, value))
            {
                RefreshCommand.RaiseCanExecuteChanged();
                CancelCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string ProgressText
    {
        get => _progressText;
        set => SetProperty(ref _progressText, value);
    }

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public bool IsBusyIndeterminate => IsScanning;

    public string SelectedSummary
    {
        get
        {
            var d = ListSelection ?? SelectedDirectory;
            return d is { } s
                ? s.FullPath + (Locale.T("     占用 ", "     Size ") + (s.Size is long size ? ByteSizeFormatter.Format(size) : Locale.T("未统计", "not calculated")))
                : Locale.T("未选择目录", "No folder selected");
        }
    }

    /// <summary>选中盘符后只建立目录结构（不算大小）；把盘根加入左侧树并默认选中。</summary>
    private void BuildStructure(string? rootPath)
    {
        Roots.Clear();
        DirectoryList.Clear();
        _selectedDirectory = null;
        OnPropertyChanged(nameof(SelectedSummary));

        if (string.IsNullOrEmpty(rootPath)) return;

        var root = new DirectoryItemViewModel(rootPath.TrimEnd('\\', '/'), rootPath, _cache);
        Roots.Add(root);

        ListSelection = null;
        _selectedDirectory = root;
        root.IsExpanded = true;
        OnPropertyChanged(nameof(SelectedDirectory));
        OnPropertyChanged(nameof(SelectedSummary));
        RebuildDirectoryList(root);

        ProgressText = rootPath + Locale.T("：结构已加载，点击右上角“刷新”统计大小", ": structure loaded — click Refresh to size it");
        StatusText = "";
    }

    /// <summary>后台扫描当前目标目录（含整棵子树），并写入大小缓存。目标：右侧列表高亮项优先，其次左侧选中节点。</summary>
    private async Task RefreshAsync()
    {
        var target = ListSelection ?? SelectedDirectory ?? Root();
        if (target == null) return;
        string targetPath = target.FullPath;

        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        _activeTarget = target;
        IsScanning = true;
        ProgressText = Locale.T("正在扫描…", "Scanning…");
        StatusText = "";

        var progress = new Progress<ScanProgress>(OnProgress);
        try
        {
            var root = await _scanner.ScanFolderAsync(targetPath, _cache.Store, progress, _cts.Token);
            ProgressText = Locale.T("扫描完成", "Scan complete");
            StatusText = Locale.IsChinese
                ? $"{root.Name}：{root.FolderCount} 个文件夹、{root.FileCount} 个文件，共 {ByteSizeFormatter.Format(root.Size)}"
                : $"{root.Name}: {root.FolderCount} folders, {root.FileCount} files, {ByteSizeFormatter.Format(root.Size)} total";
        }
        catch (OperationCanceledException)
        {
            ProgressText = Locale.T("已取消扫描", "Scan cancelled");
            StatusText = Locale.T("本次扫描已取消。", "This scan was cancelled.");
        }
        catch (Exception ex)
        {
            ProgressText = Locale.T("扫描失败", "Scan failed");
            StatusText = Locale.T("发生错误：", "Error: ") + ex.Message;
        }
        finally
        {
            RefreshVisibleSizes(targetPath);
            SortDirectoryList();
            IsScanning = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    private void OnProgress(ScanProgress p)
    {
        if (!string.IsNullOrEmpty(p.CompletedPath))
        {
            // 找到与本项路径对应的行（右侧列表项正好是扫描目标的直接子目录），原位刷新，不重建 item。
            foreach (var item in DirectoryList)
            {
                if (string.Equals(item.FullPath, p.CompletedPath, StringComparison.OrdinalIgnoreCase))
                {
                    item.RaiseSizeChanged();
                    break;
                }
            }

            // 当前扫描目标自身的大小在累加，也原位刷新。
            if (_activeTarget is { } target
                && string.Equals(target.FullPath, p.CompletedPath, StringComparison.OrdinalIgnoreCase))
                target.RaiseSizeChanged();
            OnPropertyChanged(nameof(SelectedSummary));
        }

        if (!string.IsNullOrEmpty(p.CurrentPath))
            ProgressText = Locale.T("正在扫描：", "Scanning: ") + p.CurrentPath;
    }

    /// <summary>缓存被填充后，让受影响节点重新读取大小。只遍历已加载的子节点，避免在 UI 线程递归强制加载整个子树导致卡死。</summary>
    private void RefreshVisibleSizes(string scannedRoot)
    {
        foreach (var r in Roots)
            WalkRefresh(r, scannedRoot);
    }

    private void WalkRefresh(DirectoryItemViewModel node, string scannedRoot)
    {
        if (IsWithin(node.FullPath, scannedRoot)) node.RaiseSizeChanged();
        if (!node.IsChildrenLoaded) return;
        foreach (var child in node.Children)
            WalkRefresh(child, scannedRoot);
    }

    /// <summary>把当前选中节点的子目录填充进右侧列表，并固定第一行为“..”父目录（盘根没有父目录时省略）。</summary>
    private void RebuildDirectoryList(DirectoryItemViewModel node)
    {
        DirectoryList.Clear();

        var parentPath = Path.GetDirectoryName(node.FullPath);
        if (!string.IsNullOrEmpty(parentPath))
            DirectoryList.Add(new DirectoryItemViewModel("..", parentPath, _cache) { IsParentEntry = true });

        foreach (var child in node.Children)
            DirectoryList.Add(child);
        SortDirectoryList();
    }

    /// <summary>右侧列表“..”父目录永远在首行；其余按大小降序（大的在上），未统计的排后面，同级再按名称。
    /// 使用 `Move` 逐项归位而不是“清空后重建”，避免打断用户的选中与滚动等界面操作。
    /// 归位时目标索引始终是合法下标（0..Count-1），盘根（无“..”）等边界也安全。</summary>
    private void SortDirectoryList()
    {
        var parent = DirectoryList.FirstOrDefault(x => x.IsParentEntry);
        var rest = DirectoryList.Where(x => !x.IsParentEntry).ToList();
        rest.Sort(CompareBySizeDescending);

        var desired = new List<DirectoryItemViewModel>(rest.Count + 1);
        if (parent != null) desired.Add(parent);
        desired.AddRange(rest);

        for (int i = 0; i < desired.Count; i++)
        {
            int j = -1;
            for (int k = i; k < DirectoryList.Count; k++)
            {
                if (ReferenceEquals(DirectoryList[k], desired[i])) { j = k; break; }
            }
            if (j != i) DirectoryList.Move(j, i);
        }
    }

    private static int CompareBySizeDescending(DirectoryItemViewModel a, DirectoryItemViewModel b)
    {
        int ha = a.Size.HasValue ? 0 : 1;
        int hb = b.Size.HasValue ? 0 : 1;
        int c = ha.CompareTo(hb);
        if (c != 0) return c;
        if (a.Size is { } sa && b.Size is { } sb)
        {
            c = sb.CompareTo(sa);
            if (c != 0) return c;
        }
        return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>供右侧列表结果反选回左侧树（双击进入下一层）。</summary>
    public void SelectDirectory(DirectoryItemViewModel? node)
    {
        if (node == null) return;
        ExpandAncestors(node);
        ListSelection = null;
        _selectedDirectory = node;
        node.IsExpanded = true;
        node.IsSelected = true;
        OnPropertyChanged(nameof(SelectedDirectory));
        RebuildDirectoryList(node);
        OnPropertyChanged(nameof(SelectedSummary));
    }

    /// <summary>用资源管理器打开目标目录：优先右侧列表选中项，其次当前浏览（左侧选中）目录。</summary>
    public void OpenDirectory()
    {
        var path = ListSelection?.FullPath ?? SelectedDirectory?.FullPath;
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{path.TrimEnd('\\', '/')}\"",
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            StatusText = Locale.T("打开失败：", "Failed to open: ") + ex.Message;
        }
    }

    /// <summary>把当前选中目录整理成一段发给豆包的话（文本，不含敏感信息）。无选中目录时返回 null。</summary>
    public string? BuildDoubaoQuery()
    {
        var item = ListSelection ?? SelectedDirectory ?? Root();
        if (item == null) return null;

        var sb = new StringBuilder();
        if (Locale.IsChinese)
        {
            sb.Append("请看这个 Windows 目录是做什么用的：").Append(item.FullPath).AppendLine();
            if (item.Size is long sz)
                sb.AppendLine().Append("该目录总大小约 ").Append(ByteSizeFormatter.Format(sz)).Append('。');
            if (item.IsChildrenLoaded && item.Children.Count > 0)
            {
                sb.AppendLine().Append("主要子文件夹占用：");
                foreach (var c in item.Children.Where(c => c.Size is long)
                             .OrderByDescending(c => c.Size ?? 0).Take(6))
                    sb.Append("  - ").Append(c.Name).Append(" (").Append(ByteSizeFormatter.Format(c.Size!.Value)).AppendLine(")");
            }
            sb.AppendLine().Append("请用两三句话告诉我这个目录大致是干什么的，并指出哪些子目录/文件占空间最大、是否可以安全清理。");
        }
        else
        {
            sb.Append("Please tell me what this Windows folder is for: ").Append(item.FullPath).AppendLine();
            if (item.Size is long sz)
                sb.AppendLine().Append("Total size is about ").Append(ByteSizeFormatter.Format(sz)).Append('.');
            if (item.IsChildrenLoaded && item.Children.Count > 0)
            {
                sb.AppendLine().Append("Main subfolders by usage:");
                foreach (var c in item.Children.Where(c => c.Size is long)
                             .OrderByDescending(c => c.Size ?? 0).Take(6))
                    sb.Append("  - ").Append(c.Name).Append(" (").Append(ByteSizeFormatter.Format(c.Size!.Value)).AppendLine(")");
            }
            sb.AppendLine().Append("In 2-3 sentences, what is this folder likely for, which subfolder/file takes the most space, and is it safe to clean?");
        }
        return sb.ToString();
    }

    /// <summary>从右侧列表双击“..”返回上级目录：让左侧树选中真实的父节点并重建列表。</summary>
    public void GoToParent()
    {
        var browse = _selectedDirectory;
        if (browse == null) return;
        var parentPath = Path.GetDirectoryName(browse.FullPath);
        if (string.IsNullOrEmpty(parentPath)) return;
        SelectDirectory(FindRealNode(parentPath));
    }

    /// <summary>在已加载的树中定位路径对应的真实节点（用于“返回上级”）。</summary>
    private DirectoryItemViewModel? FindRealNode(string path)
    {
        var root = Root();
        return root == null ? null : FindRealNodeRecursive(root, path);
    }

    private DirectoryItemViewModel? FindRealNodeRecursive(DirectoryItemViewModel node, string path)
    {
        if (string.Equals(node.FullPath, path, StringComparison.OrdinalIgnoreCase)) return node;
        if (!node.IsChildrenLoaded) return null;
        foreach (var child in node.Children)
        {
            if (!IsWithin(path, child.FullPath)
                && !string.Equals(path, child.FullPath, StringComparison.OrdinalIgnoreCase))
                continue;
            var hit = FindRealNodeRecursive(child, path);
            if (hit != null) return hit;
        }
        return null;
    }

    /// <summary>逐级展开 d 的所有祖先，使 d 在左侧树中可见并可选。</summary>
    private void ExpandAncestors(DirectoryItemViewModel d)
    {
        var root = Root();
        if (root == null) return;
        root.IsExpanded = true;
        var current = root;
        while (current != d)
        {
            var next = current.Children.FirstOrDefault(
                c => c == d || IsWithin(d.FullPath, c.FullPath));
            if (next == null) break;
            next.IsExpanded = true;
            current = next;
        }
    }

    private DirectoryItemViewModel? Root() => Roots.Count > 0 ? Roots[0] : null;

    private static bool IsWithin(string path, string basePath)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(basePath)) return false;
        if (string.Equals(path, basePath, StringComparison.OrdinalIgnoreCase)) return true;
        basePath = basePath.TrimEnd('\\');
        return path.StartsWith(basePath + "\\", StringComparison.OrdinalIgnoreCase);
    }
}