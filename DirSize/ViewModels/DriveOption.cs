using System.IO;

namespace DirSize.ViewModels;

/// <summary>下拉框中的一个盘符条目。</summary>
public sealed class DriveOption
{
    public DriveOption(string root, string detail)
    {
        Root = root;
        Detail = detail;
    }

    public string Root { get; }
    public string Detail { get; }
    public string Label => Root;
    public override string ToString() => Root + "  " + Detail;
}