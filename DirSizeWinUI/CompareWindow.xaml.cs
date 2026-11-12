using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;
using DirSize.Mvvm;
using DirSize.ViewModels;

namespace DirSize;

/// <summary>快照对比窗口：选择两份快照并以树型展示目录大小差值。</summary>
public sealed partial class CompareWindow : Window
{
    private readonly CompareViewModel _vm = new();

    public CompareWindow()
    {
        this.InitializeComponent();

        RootGrid.DataContext = _vm;

        if (AppWindow != null)
        {
            try
            {
                AppWindow.Resize(new SizeInt32(920, 640));
                AppWindow.Title = Locale.T("快照对比", "Snapshot Compare");
            }
            catch { }
        }

        // WinUI3 TreeView 重绑定时容器复用会崩掉窗口内容，
        // 所以监听 DiffRoots 变化，整个重建 TreeView。
        _vm.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(CompareViewModel.DiffRoots))
                RebuildTree();
        };
        RebuildTree();

        _vm.Initialize();
    }

    private void RebuildTree()
    {
        TreeHost.Content = null;
        var tree = new TreeView();
        if (TreeHost.Resources["DiffNodeTemplate"] is DataTemplate tpl)
            tree.ItemTemplate = tpl;
        tree.ItemsSource = _vm.DiffRoots;
        TreeHost.Content = tree;
    }
}
