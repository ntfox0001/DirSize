using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;
using DirSize.Mvvm;
using DirSize.ViewModels;

namespace DirSize;

/// <summary>快照对比窗口：选择两份快照并展示目录大小差值。</summary>
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

        _vm.Initialize();
    }
}
