using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Graphics;
using DirSize.Mvvm;
using DirSize.Services;
using DirSize.ViewModels;

namespace DirSize;

public sealed partial class MainWindow : Window
{
    private readonly MainViewModel _vm = new();

    public MainWindow()
    {
        this.InitializeComponent();

        // WinUI3 的 Window 没有 DataContext，绑定挂在根 Grid 上。
        RootGrid.DataContext = _vm;

        // 窗口尺寸在代码里通过 AppWindow.Resize 设置，避免在 XAML 里写 Width/Height 触发 XamlCompiler 崩溃。
        if (AppWindow != null)
        {
            try { AppWindow.Resize(new SizeInt32(1160, 760)); } catch { }
        }

        // 首帧后初始化（此时树控件已完成布局，盘符才能正常展开）。
        DispatcherQueue.TryEnqueue(() => _vm.Initialize());
    }

    private void Tree_SelectionChanged(object sender, TreeViewSelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count > 0)
            _vm.SelectDirectory(e.AddedItems[0] as DirectoryItemViewModel);
    }

    private void List_MouseDoubleClick(object sender, DoubleTappedRoutedEventArgs e)
    {
        if ((sender as ListView)?.SelectedItem is not DirectoryItemViewModel item) return;
        if (item.IsParentEntry) _vm.GoToParent();
        else _vm.SelectDirectory(item);
    }

    private async void Doubao_Click(object sender, RoutedEventArgs e)
    {
        var prompt = _vm.BuildDoubaoQuery();
        if (string.IsNullOrEmpty(prompt))
        {
            _vm.StatusText = Locale.T("请先选择一个目录再发豆包", "Select a folder first");
            return;
        }

        _vm.StatusText = Locale.T("正在连接豆包…", "Reaching out to Doubao…");
        string result = await Task.Run(() => DoubaoHelper.SendToDoubao(prompt));
        _vm.StatusText = result;
    }
}