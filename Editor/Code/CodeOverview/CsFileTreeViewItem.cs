using UnityEditor.IMGUI.Controls;

public sealed class CsFileTreeViewItem : TreeViewItem<int>
{
    public string AssetPath { get; }
    public bool IsFolder { get; }

    public CsFileMetrics Metrics { get; private set; }

    public CsFileTreeViewItem(int id, int depth, string displayName, string assetPath, bool isFolder,
        CsFileMetrics metrics)
        : base(id, depth, displayName)
    {
        AssetPath = assetPath;
        IsFolder = isFolder;
        Metrics = metrics;
    }

    public void SetMetrics(CsFileMetrics metrics)
    {
        Metrics = metrics;
    }
}