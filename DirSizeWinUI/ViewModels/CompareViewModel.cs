using System.Collections.ObjectModel;
using System.IO;
using Microsoft.UI;
using Microsoft.UI.Xaml.Media;
using DirSize.Mvvm;
using DirSize.Services;

namespace DirSize.ViewModels;

/// <summary>对比树中的一个节点（对应一个目录）。</summary>
public sealed class DiffNode
{
    public string Name { get; init; } = "";
    public string AText { get; init; } = "";
    public string BText { get; init; } = "";
    public string DiffText { get; init; } = "";
    public SolidColorBrush DiffBrush { get; init; } = new(Colors.Black);
    /// <summary>仅作为路径连接点的中间目录（自身无变化）。</summary>
    public bool IsConnector { get; init; }
    public bool IsExpanded { get; set; } = true;
    public ObservableCollection<DiffNode> Children { get; } = new();
}

/// <summary>扁平计算结果，用于组装树。</summary>
public sealed class DiffRow
{
    public string RelPath { get; init; } = "";
    public bool IsRoot { get; init; }
    public string AText { get; init; } = "";
    public string BText { get; init; } = "";
    public string DiffText { get; init; } = "";
    public SolidColorBrush DiffBrush { get; init; } = new(Colors.Black);
    public long AbsDiff { get; init; }
    public bool IsSame { get; init; }
}

/// <summary>快照对比窗口的视图模型：加载快照列表、以树型结构展示目录大小差值。</summary>
public sealed class CompareViewModel : ObservableObject
{
    private SnapshotEntry? _snapshotA;
    private SnapshotEntry? _snapshotB;
    private bool _onlyChanged = true;
    private string _summary = "";
    private string _status = "";
    private bool _isLoading;

    public ObservableCollection<SnapshotEntry> Snapshots { get; } = new();

    // 注意：TreeView 重绑定时整体替换集合引用（而不是 Clear+Add），
    // 否则 WinUI3 TreeView 的容器复用会让窗口内容崩掉。
    private ObservableCollection<DiffNode> _diffRoots = new();
    public ObservableCollection<DiffNode> DiffRoots
    {
        get => _diffRoots;
        private set => SetProperty(ref _diffRoots, value);
    }

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

    /// <summary>只显示有变化的目录（新增/删除/大小改变），路径上的中间目录作为连接点保留。</summary>
    public bool OnlyChanged
    {
        get => _onlyChanged;
        set { if (SetProperty(ref _onlyChanged, value)) RebuildTree(); }
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

    // 对比色：增大红色、减小绿色、新增紫色、已删除/无变化灰色。
    private static SolidColorBrush Rgb(byte r, byte g, byte b)
        => new(new Windows.UI.Color { A = 255, R = r, G = g, B = b });
    private static readonly SolidColorBrush BrushGrow = Rgb(0xC0, 0x39, 0x2B);
    private static readonly SolidColorBrush BrushShrink = Rgb(0x27, 0xAE, 0x60);
    private static readonly SolidColorBrush BrushNew = Rgb(0x8E, 0x44, 0xAD);
    private static readonly SolidColorBrush BrushGray = Rgb(0x82, 0x90, 0xA0);

    private readonly Dictionary<string, SnapshotData> _loaded = new();
    /// <summary>最近一次计算的扁平结果（含无变化行），供 OnlyChanged 切换时重建树。</summary>
    private List<DiffRow> _allRows = new();
    private string _driveDisplay = "";

    public void Initialize()
    {
        Snapshots.Clear();
        foreach (var s in SnapshotService.ListSnapshots())
            Snapshots.Add(s);
        Status = Snapshots.Count == 0 ? TextNoSnapshots : "";

        // 默认配对：优先找“最新的两份同盘快照”，找不到再退回最早/最新两份。
        if (Snapshots.Count >= 2)
        {
            SnapshotEntry? a = null, b = null;
            for (int i = 0; i < Snapshots.Count && a == null; i++)
            {
                for (int j = i + 1; j < Snapshots.Count; j++)
                {
                    if (Snapshots[i].DriveLabel == Snapshots[j].DriveLabel)
                    {
                        b = Snapshots[i];   // 新的做 B
                        a = Snapshots[j];   // 旧的做 A
                        break;
                    }
                }
            }
            a ??= Snapshots[^1];
            b ??= Snapshots[0];
            SnapshotA = a;
            SnapshotB = b;
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
            DiffRoots = new ObservableCollection<DiffNode>();
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
        _driveDisplay = a.Drive.TrimEnd('\\', '/');
        var prefix = _driveDisplay + "\\";
        var keys = new HashSet<string>(a.Folders.Keys, StringComparer.OrdinalIgnoreCase);
        keys.UnionWith(b.Folders.Keys);

        var rows = new List<DiffRow>();
        foreach (var k in keys)
        {
            bool inA = a.Folders.TryGetValue(k, out var sa);
            bool inB = b.Folders.TryGetValue(k, out var sb);

            long absDiff;
            string diffText;
            SolidColorBrush brush;
            bool isSame = false;

            if (!inB) { absDiff = sa; diffText = Locale.T("已删除", "deleted"); brush = BrushGray; }
            else if (!inA) { absDiff = sb; diffText = Locale.T("新增", "new"); brush = BrushNew; }
            else
            {
                var d = sb - sa;
                absDiff = Math.Abs(d);
                if (d == 0)
                {
                    isSame = true;
                    diffText = "";
                    brush = BrushGray;
                }
                else if (d > 0) { diffText = "+" + ByteSizeFormatter.Format(d); brush = BrushGrow; }
                else { diffText = "-" + ByteSizeFormatter.Format(-d); brush = BrushShrink; }
            }

            var rel = k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? k[prefix.Length..] : k;
            bool isRoot = rel.Length == 0; // 盘根本身

            rows.Add(new DiffRow
            {
                RelPath = isRoot ? k : rel,
                IsRoot = isRoot,
                AText = inA ? ByteSizeFormatter.Format(sa) : "—",
                BText = inB ? ByteSizeFormatter.Format(sb) : "—",
                DiffText = diffText,
                DiffBrush = brush,
                AbsDiff = absDiff,
                IsSame = isSame,
            });
        }

        // 变化幅度大的排前面：插入树的顺序即每层的子节点顺序。
        rows.Sort((x, y) => y.AbsDiff.CompareTo(x.AbsDiff));

        var rootA = TryGetSize(a.Folders, _driveDisplay, out var va0) ? va0 : 0;
        var rootB = TryGetSize(b.Folders, _driveDisplay, out var vb0) ? vb0 : 0;
        var net = rootB - rootA;

        Summary = Locale.IsChinese
            ? $"{_driveDisplay}  总量 {ByteSizeFormatter.Format(rootA)} → {ByteSizeFormatter.Format(rootB)}，净变化 {(net >= 0 ? "+" : "-")}{ByteSizeFormatter.Format(Math.Abs(net))}"
            : $"{_driveDisplay}  total {ByteSizeFormatter.Format(rootA)} → {ByteSizeFormatter.Format(rootB)}, net {(net >= 0 ? "+" : "-")}{ByteSizeFormatter.Format(Math.Abs(net))}";

        _allRows = rows;
        RebuildTree();
    }

    private void RebuildTree()
    {
        var result = new ObservableCollection<DiffNode>();

        // 1. 选出要显示的行：只看变化时排除自身无变化的行（路径连接点后面按需补）。
        var included = OnlyChanged ? _allRows.Where(r => !r.IsSame).ToList() : _allRows.ToList();

        var rowByPath = new Dictionary<string, DiffRow>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in _allRows) rowByPath[r.RelPath] = r;

        // 2. 建节点：included 的全部 + 它们的祖先连接点
        //    （例如 Users\foo 变了但 Users 没变，Users 仍要显示出来当路径）。
        var nodes = new Dictionary<string, DiffNode>(StringComparer.OrdinalIgnoreCase);
        DiffNode? root = null;

        DiffNode MakeNode(DiffRow r, bool connector) => new DiffNode
        {
            Name = r.IsRoot ? _driveDisplay + "\\" : LastSegment(r.RelPath),
            AText = r.AText,
            BText = r.BText,
            DiffText = connector ? "" : r.DiffText,
            DiffBrush = connector ? BrushGray : r.DiffBrush,
            IsConnector = connector,
        };

        foreach (var r in included)
        {
            if (!nodes.ContainsKey(r.RelPath))
                nodes[r.RelPath] = MakeNode(r, connector: false);
            if (r.IsRoot) root = nodes[r.RelPath];

            var parent = ParentPath(r.RelPath);
            while (parent != null && parent.Length > 0 && !nodes.ContainsKey(parent))
            {
                if (!rowByPath.TryGetValue(parent, out var pr)) break;
                nodes[parent] = MakeNode(pr, connector: pr.IsSame);
                parent = ParentPath(parent);
            }
        }

        // 3. 挂接父子关系。included 已按变化幅度降序，先挂它们保证大变化靠前；
        //    连接点补充挂接在后。
        var attached = new HashSet<DiffNode>();
        void Attach(DiffNode n, string path)
        {
            if (!attached.Add(n)) return;
            var parentPath = ParentPath(path);
            if (parentPath == null) return;               // 盘根节点本身
            DiffNode? pn = null;
            if (parentPath.Length == 0) pn = root;        // 顶层目录 → 盘根
            else nodes.TryGetValue(parentPath, out pn);
            if (pn != null) pn.Children.Add(n);
            else result.Add(n);                           // 盘根被过滤时顶层直接展示
        }

        foreach (var r in included)
            if (nodes.TryGetValue(r.RelPath, out var n)) Attach(n, r.RelPath);
        foreach (var kv in nodes)
            if (!attached.Contains(kv.Value)) Attach(kv.Value, kv.Key);

        if (root != null) result.Add(root);
        DiffRoots = result;
    }

    /// <summary>取相对路径的最后一段作为显示名。</summary>
    private static string LastSegment(string rel)
    {
        var idx = rel.LastIndexOf('\\');
        return idx >= 0 ? rel[(idx + 1)..] : rel;
    }

    /// <summary>相对路径的父路径：顶层目录返回 ""（父级是盘根），盘根返回 null。</summary>
    private static string? ParentPath(string rel)
    {
        if (rel.EndsWith(":\\")) return null;
        var idx = rel.LastIndexOf('\\');
        return idx < 0 ? "" : rel[..idx];
    }

    /// <summary>盘根的键可能是 "C:" 或 "C:\"，两者都试。</summary>
    private static bool TryGetSize(Dictionary<string, long> folders, string path, out long size)
        => folders.TryGetValue(path, out size) || folders.TryGetValue(path + "\\", out size);
}
