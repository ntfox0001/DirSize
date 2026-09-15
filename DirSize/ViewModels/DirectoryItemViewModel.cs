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
    {
        get
        {
            if (_children == null)
            {
                _children = new ObservableCollection<DirectoryItemViewModel>();
                LoadChildren();
            }
            return _children;
        }
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (SetProperty(ref _isExpanded, value)) LoadChildren();
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    private void LoadChildren()
    {
        if (_loaded) return;
        _loaded = true;
        var target = _children ?? new ObservableCollection<DirectoryItemViewModel>();
        foreach (var sub in NativeDirectoryScanner.ListSubdirectories(FullPath).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            target.Add(new DirectoryItemViewModel(Path.GetFileName(sub), sub, _cache));
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