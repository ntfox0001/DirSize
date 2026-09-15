using System.Globalization;

namespace DirSize.Mvvm;

/// <summary>按语言选择界面：中文显示中文，其它显示英文。
/// 可通过启动参数强制：--lang=zh / --lang=en（也兼容 --zh / --en）。</summary>
public static class Locale
{
    /// <summary>是否使用中文界面。</summary>
    public static bool IsChinese { get; } = ResolveLanguage();

    private static bool ResolveLanguage()
    {
        foreach (var arg in Environment.GetCommandLineArgs())
        {
            string a = arg.Trim();
            if (a.Equals("--zh", StringComparison.OrdinalIgnoreCase)
                || a.Equals("--lang=zh", StringComparison.OrdinalIgnoreCase)
                || a.Equals("/zh", StringComparison.OrdinalIgnoreCase))
                return true;
            if (a.Equals("--en", StringComparison.OrdinalIgnoreCase)
                || a.Equals("--lang=en", StringComparison.OrdinalIgnoreCase)
                || a.Equals("/en", StringComparison.OrdinalIgnoreCase))
                return false;
        }
        return CultureInfo.CurrentUICulture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
            || CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("zh", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>取当前语言对应的文本。</summary>
    public static string T(string zh, string en) => IsChinese ? zh : en;
}