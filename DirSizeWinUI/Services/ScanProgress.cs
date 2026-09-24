namespace DirSize.Services;

/// <summary>扫描过程的实时进度快照。</summary>
public sealed class ScanProgress
{
    /// <summary>正在枚举的目录路径（用于状态栏展示）。</summary>
    public string CurrentPath { get; init; } = "";

    /// <summary>本次完成的文件数（累计）。</summary>
    public long FilesScanned { get; init; }

    /// <summary>当一个目录被完全算完后，给出它的路径与累计大小；为空表示该快照不包含“完成”信息。</summary>
    public string? CompletedPath { get; init; }

    /// <summary>对应 CompletedPath 目录的累计文件大小（含所有子孙）。</summary>
    public long CompletedSize { get; init; }

    /// <summary>对应 CompletedPath 目录的累计文件夹数。</summary>
    public long CompletedFolders { get; init; }

    /// <summary>对应 CompletedPath 目录的累计文件数。</summary>
    public long CompletedFiles { get; init; }
}