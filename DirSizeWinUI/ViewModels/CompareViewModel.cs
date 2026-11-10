using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media;
using DirSize.Mvvm;
using DirSize.Services;

namespace DirSize.ViewModels;

/// <summary>对比结果表中的一行。</summary>
public sealed class DiffItem
{
    public string RelPath { get; init; } = "";
    public string AText { get; init; } = "";
    public string BText { get; init; } = "";
    public string DiffText { get; init; } = "";
    public SolidColorBrush DiffBrush { get; init; } = new(Colors.Black);
    /// <summary>变化幅度（用于排序）；新增/删除取其大小。</summary>
    public long AbsDiff { get; init; }
    /// <summary>两份快照中大小完全一致。</summary>
    public bool IsSame { get; init; }
}

/// <summary>快照对比窗口的视图模型：加载快照列表、计算两份快照的目录大小差值。</summary>
public sealed class CompareViewModel : ObservableObject
{
    private SnapshotEntry? _snapshotA;
    private SnapshotEntry? _snapshotB;
    private bool _onlyChanged = true;
    private string _summary = "";
    private string _status = "";
    private bool _isLoading;

    public ObservableCollection<SnapshotEntry> Snapshots { get; } = new();
    public ObservableCollection<DiffItem> DiffRows { get; } = new();

    public SnapshotEntry? SnapshotA
    {
        get => _snapshotA;
        set { if (SetProperty(ref _snapshotA, value)) ComputeDiffAsync(); }
    }

    public SnapshotEntry? SnapshotB
    {
        get => _snapshotB;
        set { if (SetProperty(ref _snapshotB, value)) ComputeDiffAsync(); }
    }

    /// <summary>只显示有变化的目录（新增/删除/大小改变），保证表格简洁。</summary>
    public bool OnlyChanged
    {
        get => _onlyChanged;
        set { if (SetProperty(ref _onlyChanged, value)) RebuildRows(); }
    }

    public string Summary { get => _summary; set => SetProperty(ref _summary, value); }
    public string Status { get => _status; set => SetProperty(ref _status, value); }
    public bool IsLoading { get => _isLoading; set => SetProperty(ref _isLoading, value); }

    public string TextSnapshotA => Locale.T("快照 A（旧）", "Snapshot A (older)");
    public string TextSnapshotB => Locale.T("快照 B（新）", "Snapshot B (newer)");
    public string TextColPath => Locale.T("目录", "Folder");
    public string TextColA => Locale.T("快照 A", "Snapshot A");
    public string TextColB => Locale.T("快照 B", "Snapshot B");
    public string TextColDiff => Locale.T("变化", "Change");
    public string TextOnlyChanged => Locale.T("只看有变化的目录", "Show changed folders only");
    public string TextNoSnapshots => Locale.T("还没有快照，请先在主窗口点击“快照”生成。", "No snapshots yet — use Snapshot in the main window first.");

    // 对比色：增大红色、减小绿色、新增紫色（与主界面大小列同色）、已删除灰色。
    private static readonly SolidColorBrush BrushGrow = new(Color.FromRgb(0xC0, 0x39, 0x2B));
    private static readonly SolidColorBrush BrushShrink = new(Color.FromRgb(0x27, 0xAE, 0x60));
    private static readonly SolidColorBrush BrushNew = new(Color.FromRgb(0x8E, 0x44, 0xAD));
    private static readonly SolidColorBrush BrushRemoved = new(Color.FromRgb(0x82, 0x90, 0xA0));

    /// <summary>已加载的快照数据缓存，键为文件路径。</summary>
    private readonly Dictionary<string, SnapshotData> _loaded = new();
    /// <summary>最近一次计算的完整结果（含无变化行），供 OnlyChanged 切换时重建视图。</summary>
    private List<DiffItem> _allRows = new();

    public void Initialize()
    {
        Snapshots.Clear();
        foreach (var s in SnapshotService.ListSnapshots())
            Snapshots.Add(s);
        Status = Snapshots.Count == 0 ? TextNoSnapshots : "";

        // 默认最早的一份做 A、最新的一份做 B，打开即有结果。
        if (Snapshots.Count >= 2)
        {
            SnapshotA = Snapshots[^1];
            SnapshotB = Snapshots[0];
        }
        else if (Snapshots.Count == 1)
        {
            SnapshotA = Snapshots[0];
        }
    }

    private async void ComputeDiffAsync()
    {
        if (SnapshotA == null || SnapshotB == null)
        {
            Summary = "";
            DiffRows.Clear();
            return;
        }

        IsLoading = true;
        Status = Locale.T("正在加载快照…", "Loading snapshots…");
        try
        {
            var a = await LoadCached(SnapshotA);
            var b = await LoadCached(SnapshotB);
            Compute(a, b);
            Status = "";
        }
        catch (Exception ex)
        {
            Status = Locale.T("加载失败：", "Failed to load: ") + ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task<SnapshotData> LoadCached(SnapshotEntry e)
    {
        if (_loaded.TryGetValue(e.FilePath, out var cached)) return cached;
        var data = await SnapshotService.LoadAsync(e.FilePath);
        _loaded[e.FilePath] = data;
        return data;
    }

    private void Compute(SnapshotData a, SnapshotData b)
    {
        var rows = new List<DiffItem>();
        var prefix = a.Drive.TrimEnd('\\', '/') + "\\";
        var keys = new HashSet<string>(a.Folders.Keys, StringComparer.OrdinalIgnoreCase);
        keys.UnionWith(b.Folders.Keys);

        foreach (var k in keys)
        {
            bool inA = a.Folders.TryGetValue(k, out var sa);
            bool inB = b.Folders.TryGetValue(k, out var sb);

            long absDiff;
            string diffText;
            SolidColorBrush brush;
            bool isSame = false;

            if (!inB) { absDiff = sa; diffText = Locale.T("已删除", "deleted"); brush = BrushRemoved; }
            else if (!inA) { absDiff = sb; diffText = Locale.T("新增", "new"); brush = BrushNew; }
            else
            {
                var d = sb - sa;
                absDiff = Math.Abs(d);
                if (d == 0)
                {
                    isSame = true;
                    diffText = Locale.T("无变化", "same");
                    brush = BrushRemoved; // 灰色；仅取消“只看有变化”后可见
                }
                else if (d > 0) { diffText = "+" + ByteSizeFormatter.Format(d); brush = BrushGrow; }
                else { diffText = "-" + ByteSizeFormatter.Format(-d); brush = BrushShrink; }
            }

            var rel = k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? k[prefix.Length..] : k;
            if (rel.Length == 0) rel = k; // 盘根本身（键为 "C:\"）直接显示原样

            rows.Add(new DiffItem
            {
                RelPath = rel,
                AText = inA ? ByteSizeFormatter.Format(sa) : "—",
                BText = inB ? ByteSizeFormatter.Format(sb) : "—",
                DiffText = diffText,
                DiffBrush = brush,
                AbsDiff = absDiff,
                IsSame = isSame,
            });
        }

        // 变化幅度大的排前面，让用户一眼看到大变化。
        rows.Sort((x, y) => y.AbsDiff.CompareTo(x.AbsDiff));

        var rootKey = a.Drive.TrimEnd('\\', '/');
        var rootA = TryGetSize(a.Folders, rootKey, out var va0) ? va0 : 0;
        var rootB = TryGetSize(b.Folders, rootKey, out var vb0) ? vb0 : 0;
        var net = rootB - rootA;

        Summary = Locale.IsChinese
            ? $"{a.Drive.TrimEnd('\\')}  总量 {ByteSizeFormatter.Format(rootA)} → {ByteSizeFormatter.Format(rootB)}，净变化 {(net >= 0 ? "+" : "-")}{ByteSizeFormatter.Format(Math.Abs(net))}"
            : $"{a.Drive.TrimEnd('\\')}  total {ByteSizeFormatter.Format(rootA)} → {ByteSizeFormatter.Format(rootB)}, net {(net >= 0 ? "+" : "-")}{ByteSizeFormatter.Format(Math.Abs(net))}";

        _allRows = rows;
        RebuildRows();
    }

    /// <summary>盘根的键可能是 "C:" 或 "C:\"，两者都试。</summary>
    private static bool TryGetSize(Dictionary<string, long> folders, string path, out long size)
        => folders.TryGetValue(path, out size) || folders.TryGetValue(path + "\\", out size);

    private void RebuildRows()
    {
        DiffRows.Clear();
        foreach (var r in _allRows)
        {
            if (OnlyChanged && r.IsSame) continue;
            DiffRows.Add(r);
        }
    }
}
