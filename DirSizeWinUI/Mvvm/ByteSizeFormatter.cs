namespace DirSize.Mvvm;

/// <summary>把字节数格式化成资源管理器风格的字符串，如 512 Bytes / 1.2 KB / 3.4 MB / 1.06 GB。</summary>
public static class ByteSizeFormatter
{
    public static string Format(long bytes)
    {
        if (bytes < 0) bytes = 0;
        double value = bytes;
        if (bytes < 1024) return $"{bytes} Bytes";
        if (bytes < 1024 * 1024) return $"{value / 1024:0.#} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{value / (1024 * 1024):0.#} MB";
        return $"{value / (1024d * 1024 * 1024):0.##} GB";
    }
}