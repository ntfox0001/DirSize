using Microsoft.UI.Xaml;

namespace DirSize;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        this.InitializeComponent();

        // 记录未处理异常到本地日志，便于排查“窗口内容消失”一类问题。
        this.UnhandledException += (s, e) =>
        {
            try
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DirSize");
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "crash.log"),
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {e.Message}\n{e.Exception.StackTrace}\n\n");
            }
            catch { }
            e.Handled = true;
        };
    }

    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
    }
}
