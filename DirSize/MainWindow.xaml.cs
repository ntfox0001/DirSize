using System.Windows;
using System.Windows.Controls;
using DirSize.Mvvm;
using DirSize.Services;
using DirSize.ViewModels;

namespace DirSize;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        (DataContext as MainViewModel)?.Initialize();
    }

    private void Tree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is MainViewModel vm)
            vm.SelectDirectory(e.NewValue as DirectoryItemViewModel);
    }

    private void List_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        if ((sender as ListView)?.SelectedItem is not DirectoryItemViewModel item) return;
        if (item.IsParentEntry) vm.GoToParent();
        else vm.SelectDirectory(item);
    }

    private async void Doubao_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        var prompt = vm.BuildDoubaoQuery();
        if (string.IsNullOrEmpty(prompt))
        {
            vm.StatusText = Locale.T("请先选择一个目录再发豆包", "Select a folder first");
            return;
        }

        // 窗口聚焦/按键操作放到后台线程，避免卡界面；不再依赖剪贴板。
        vm.StatusText = Locale.T("正在连接豆包…", "Reaching out to Doubao…");
        string result = await Task.Run(() => DoubaoHelper.SendToDoubao(prompt));
        vm.StatusText = result;
    }
}