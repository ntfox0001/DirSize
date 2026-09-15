using System.Collections.ObjectModel;

namespace DirSize.Models;

/// <summary>扫描得到的目录/文件节点（只含纯数据，不含视图层状态）。</summary>
public sealed class DirectoryNode
{
    public string Name { get; set; } = "";
    public string FullPath { get; set; } = "";
    public bool IsFile { get; set; }
    public long Size { get; set; }
    public long FileCount { get; set; }
    public long FolderCount { get; set; }
    public bool IsAccessible { get; set; } = true;
    public ObservableCollection<DirectoryNode> Children { get; } = new();
}