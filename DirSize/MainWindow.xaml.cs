using System.Windows;
using System.Windows.Controls;
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
}