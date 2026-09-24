using System.Collections.ObjectModel;
using System.IO;
using DirSize.Mvvm;
using DirSize.Services;

namespace DirSize.ViewModels;

/// <summary>左侧树 / 右侧列表共用的目录节点。只包含目录（不含文件）；子目录懒加载；
/// 大小实时从 SizeCache 读取——扫描过则为格式化数值，未扫描显示占位符。</summary>
public sealed class DirectoryItemViewModel : ObservableObject
{
    private readonly SizeCache _cache;
    private ObservableCollection<DirectoryItemViewModel>? _children;
    private bool _loaded;
    private bool _isExpanded;
    private bool _isSelected;

    public DirectoryItemViewModel(string name, string fullPath, SizeCache cache)
    {
        Name = name;
        FullPath = fullPath;
        _cache = cache;
    }

    public string Name { get; }
    public string FullPath { get; }

    /// <summary>是否为右侧列表首行的“..”父目录占位节点（不计大小、不枚举子集）。</summary>
    public bool IsParentEntry { get; init; }

    public long? Size => IsParentEntry ? null : _cache.Get(FullPath);
    public bool HasSize => Size.HasValue;
    public string SizeText => Size is long s ? ByteSizeFormatter.Format(s) : "—";
    public string SubfolderText => IsParentEntry ? "" : (Children.Count == 0 ? "0" : Children.Count.ToString());
    public string DetailText => IsParentEntry
        ? Locale.T("返回上级目录", "Go up to parent folder")
        : HasSize ? FullPath : Locale.T("尚未统计大小，点击上方“刷新”开始计算", "Size not calculated yet — click Refresh");

    public ObservableCollection<DirectoryItemViewModel> Children
        => _children ??= LoadChildren();

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            _ = SetProperty(ref _isExpanded, value);
            LoadChildren(); // 无论展开/折叠都确保子目录已加载（加载是幂等的）
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    /// <summary>确保 _children 已创建并填充子目录，返回该集合。可从 Children getter 或 IsExpanded 触发，
    /// 二者都必须把结果写回 _children 字段，避免“新建局部集合填充后被丢弃”导致空结果。</summary>
    private ObservableCollection<DirectoryItemViewModel> LoadChildren()
    {
        _children ??= new ObservableCollection<DirectoryItemViewModel>();
        if (_loaded) return _children;
        _loaded = true;
        foreach (var sub in NativeDirectoryScanner.ListSubdirectories(FullPath).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            _children.Add(new DirectoryItemViewModel(Path.GetFileName(sub), sub, _cache));
        return _children;
    }

    public bool IsChildrenLoaded => _loaded;

    /// <summary>缓存更新后让界面重新读取本次节点的各个大小相关属性。</summary>
    public void RaiseSizeChanged()
    {
        OnPropertyChanged(nameof(Size));
        OnPropertyChanged(nameof(HasSize));
        OnPropertyChanged(nameof(SizeText));
        OnPropertyChanged(nameof(DetailText));
    }
}