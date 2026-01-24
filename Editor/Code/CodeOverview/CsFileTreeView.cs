using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

public sealed class CsFileTreeView : TreeView<int>
{
    private enum ColumnId
    {
        Name = 0,
        Lines = 1,
        DocCoverage = 2
    }

    private readonly List<string> _csFilePaths = new(1024);

    // Cache by asset path (Assets/.../File.cs)
    private readonly Dictionary<string, CsFileMetrics> _metricsCache = new(StringComparer.OrdinalIgnoreCase);

    // Numeric filters
    private int _minLines;
    private int _maxLines = 1_000_000;
    private int _minCoveragePercent;
    private int _maxCoveragePercent = 100;

    private static GUIStyle s_rightAlignedLabelStyle;

    public CsFileTreeView(TreeViewState<int> state)
        : base(state, CreateMultiColumnHeader())
    {
        showBorder = true;
        showAlternatingRowBackgrounds = true;
        rowHeight = EditorGUIUtility.singleLineHeight + 2f;

        multiColumnHeader.ResizeToFit();
        Reload();
    }

    public void SetNumericFilters(int minLines, int maxLines, int minCoveragePercent, int maxCoveragePercent)
    {
        _minLines = Mathf.Max(0, minLines);
        _maxLines = Mathf.Clamp(maxLines, _minLines, 1_000_000);
        _minCoveragePercent = Mathf.Clamp(minCoveragePercent, 0, 100);
        _maxCoveragePercent = Mathf.Clamp(maxCoveragePercent, _minCoveragePercent, 100);
    }

    public void ClearMetricsCache()
    {
        _metricsCache.Clear();
    }

    public void SetFocusAndEnsureSelectedItemVisible()
    {
        if (HasSelection())
        {
            SetFocus();
            FrameItem(GetSelection()[0]);
        }
        else
        {
            SetFocus();
        }
    }

    protected override TreeViewItem<int> BuildRoot()
    {
        RefreshFileList();

        TreeViewItem<int> root = new(0, -1, "Root")
        {
            children = new List<TreeViewItem<int>>()
        };

        Dictionary<string, FolderNode> foldersByPath = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Assets"] = new FolderNode("Assets", 1, null)
        };

        int nextId = 2;

        foreach (string assetPath in _csFilePaths)
        {
            if (!PassesSearchFilter(assetPath)) continue;

            CsFileMetrics metrics = GetOrComputeMetrics(assetPath);
            if (!PassesNumericFilters(metrics)) continue;

            string[] parts = assetPath.Replace('\\', '/').Split('/');
            if (parts.Length < 2) continue;

            string runningPath = "Assets";
            FolderNode current = foldersByPath["Assets"];

            for (int i = 1; i < parts.Length - 1; i++)
            {
                string folderName = parts[i];
                runningPath = runningPath + "/" + folderName;

                if (!foldersByPath.TryGetValue(runningPath, out FolderNode folderNode))
                {
                    folderNode = new FolderNode(folderName, nextId++, current);
                    foldersByPath[runningPath] = folderNode;
                    current.Children.Add(folderNode);
                }

                current = folderNode;
            }

            string fileName = parts[^1];
            current.Files.Add(new FileNode(fileName, assetPath, nextId++, metrics));
        }

        FolderNode assetsRoot = foldersByPath["Assets"];

        // Build TreeViewItems and compute folder aggregates based only on the filtered-in files.
        foreach (TreeViewItem<int> child in BuildItemsRecursive(assetsRoot, 0))
        {
            root.AddChild(child);
        }

        if (root.children == null) root.children = new List<TreeViewItem<int>>();

        SetupDepthsFromParentsAndChildren(root);
        return root;
    }

    protected override void RowGUI(RowGUIArgs args)
    {
        if (args.item is not CsFileTreeViewItem item)
        {
            base.RowGUI(args);
            return;
        }

        for (int col = 0; col < args.GetNumVisibleColumns(); col++)
        {
            int columnIndex = args.GetColumn(col);
            Rect cellRect = args.GetCellRect(col);

            CenterRectUsingSingleLineHeight(ref cellRect);

            switch ((ColumnId)columnIndex)
            {
                case ColumnId.Name:
                    DrawNameCell(cellRect, args, item);
                    break;

                case ColumnId.Lines:
                    DrawLinesCell(cellRect, item);
                    break;

                case ColumnId.DocCoverage:
                    DrawCoverageCell(cellRect, item);
                    break;
            }
        }
    }

    protected override void DoubleClickedItem(int id)
    {
        if (FindItem(id, rootItem) is not CsFileTreeViewItem item || item.IsFolder) return;

        UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath<MonoScript>(item.AssetPath);
        if (asset == null) asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(item.AssetPath);

        if (asset != null) AssetDatabase.OpenAsset(asset);
    }

    protected override void SingleClickedItem(int id)
    {
        if (FindItem(id, rootItem) is not CsFileTreeViewItem item || item.IsFolder) return;

        UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath<MonoScript>(item.AssetPath);
        if (asset == null) asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(item.AssetPath);

        if (asset != null) EditorGUIUtility.PingObject(asset);
    }

    protected override void ContextClickedItem(int id)
    {
        if (FindItem(id, rootItem) is not CsFileTreeViewItem item || item.IsFolder) return;

        GenericMenu menu = new();

        menu.AddItem(new GUIContent("Ping Asset"), false, () =>
        {
            UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(item.AssetPath);
            if (asset != null) EditorGUIUtility.PingObject(asset);
        });

        menu.AddItem(new GUIContent("Open"), false, () =>
        {
            UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(item.AssetPath);
            if (asset != null) AssetDatabase.OpenAsset(asset);
        });

        menu.AddSeparator(string.Empty);

        menu.AddItem(new GUIContent("Copy Path"), false, () => { EditorGUIUtility.systemCopyBuffer = item.AssetPath; });

        menu.AddItem(new GUIContent("Reveal in File Explorer/Finder"), false, () =>
        {
            string fullPath = Path.GetFullPath(item.AssetPath);
            EditorUtility.RevealInFinder(fullPath);
        });

        menu.ShowAsContext();
    }

    private static MultiColumnHeader CreateMultiColumnHeader()
    {
        MultiColumnHeaderState.Column[] columns =
        {
            new()
            {
                headerContent = new GUIContent("Name"),
                headerTextAlignment = TextAlignment.Left,
                canSort = false,
                width = 460f,
                minWidth = 260f,
                autoResize = true,
                allowToggleVisibility = false
            },
            new()
            {
                headerContent = new GUIContent("Lines"),
                headerTextAlignment = TextAlignment.Right,
                canSort = false,
                width = 70f,
                minWidth = 55f,
                maxWidth = 110f,
                autoResize = false,
                allowToggleVisibility = false
            },
            new()
            {
                headerContent = new GUIContent("Doc %"),
                headerTextAlignment = TextAlignment.Right,
                canSort = false,
                width = 70f,
                minWidth = 55f,
                maxWidth = 110f,
                autoResize = false,
                allowToggleVisibility = false
            }
        };

        MultiColumnHeaderState state = new(columns);
        MultiColumnHeader header = new(state) { height = 22f };
        return header;
    }

    private void DrawNameCell(Rect rect, RowGUIArgs args, CsFileTreeViewItem item)
    {
        args.rowRect = rect;
        base.RowGUI(args);
    }

    private static void DrawLinesCell(Rect rect, CsFileTreeViewItem item)
    {
        int lines = item.Metrics.LineCount;
        string text = lines >= 0 ? lines.ToString() : "n/a";
        EditorGUI.LabelField(rect, text, RightAlignedLabelStyle);
    }

    private static void DrawCoverageCell(Rect rect, CsFileTreeViewItem item)
    {
        EditorGUI.LabelField(rect, $"{item.Metrics.CoveragePercent}%", RightAlignedLabelStyle);
    }

    private static GUIStyle RightAlignedLabelStyle
    {
        get
        {
            s_rightAlignedLabelStyle ??= new GUIStyle(EditorStyles.label)
            {
                alignment = TextAnchor.MiddleRight
            };

            return s_rightAlignedLabelStyle;
        }
    }

    private void RefreshFileList()
    {
        _csFilePaths.Clear();

        string[] guids = AssetDatabase.FindAssets("t:MonoScript", new[] { "Assets" });

        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            if (string.IsNullOrEmpty(path)) continue;

            path = path.Replace('\\', '/');

            if (!path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)) continue;

            if (path.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase)) continue;

            if (!path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) continue;

            _csFilePaths.Add(path);
        }

        _csFilePaths.Sort(StringComparer.OrdinalIgnoreCase);

        PruneMetricsCache();
    }

    private void PruneMetricsCache()
    {
        if (_metricsCache.Count == 0) return;

        HashSet<string> existing = new(_csFilePaths, StringComparer.OrdinalIgnoreCase);
        List<string> toRemove = null;

        foreach (KeyValuePair<string, CsFileMetrics> kv in _metricsCache)
        {
            if (!existing.Contains(kv.Key))
            {
                toRemove ??= new List<string>();
                toRemove.Add(kv.Key);
            }
        }

        if (toRemove == null) return;

        for (int i = 0; i < toRemove.Count; i++)
        {
            _metricsCache.Remove(toRemove[i]);
        }
    }

    private CsFileMetrics GetOrComputeMetrics(string assetPath)
    {
        if (_metricsCache.TryGetValue(assetPath, out CsFileMetrics cached)) return cached;

        string fullPath = Path.GetFullPath(assetPath);
        CsFileMetrics metrics = CsCommentCoverageAnalyzer.Analyze(fullPath);
        _metricsCache[assetPath] = metrics;
        return metrics;
    }

    private bool PassesSearchFilter(string assetPath)
    {
        if (string.IsNullOrWhiteSpace(searchString)) return true;

        return assetPath.IndexOf(searchString, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private bool PassesNumericFilters(CsFileMetrics metrics)
    {
        int lines = metrics.LineCount < 0 ? 0 : metrics.LineCount;
        if (lines < _minLines || lines > _maxLines) return false;

        int coveragePercent = metrics.CoveragePercent;
        if (coveragePercent < _minCoveragePercent || coveragePercent > _maxCoveragePercent) return false;

        return true;
    }

    private IEnumerable<TreeViewItem<int>> BuildItemsRecursive(FolderNode folder, int depth)
    {
        folder.Children.Sort(static (a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        folder.Files.Sort(static (a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

        bool isAssetsRoot = folder.Parent == null;

        if (!isAssetsRoot)
        {
            TreeViewItem<int> folderItem =
                new CsFileTreeViewItem(folder.Id, depth, folder.Name, null, true, folder.Aggregate)
                {
                    children = new List<TreeViewItem<int>>()
                };

            // Recompute aggregate based on visible children.
            CsFileMetrics aggregate = new(0, 0, 0);

            foreach (FolderNode childFolder in folder.Children)
            {
                foreach (TreeViewItem<int> item in BuildItemsRecursive(childFolder, depth + 1))
                {
                    folderItem.AddChild(item);

                    if (item is CsFileTreeViewItem childItem)
                        aggregate = CsFileMetrics.Combine(aggregate, childItem.Metrics);
                }
            }

            foreach (FileNode file in folder.Files)
            {
                CsFileTreeViewItem fileItem = new(file.Id, depth + 1, file.Name, file.AssetPath, false, file.Metrics);
                folderItem.AddChild(fileItem);
                aggregate = CsFileMetrics.Combine(aggregate, file.Metrics);
            }

            // If folder ended up empty due to filters, hide it.
            if (folderItem.children == null || folderItem.children.Count == 0) yield break;

            ((CsFileTreeViewItem)folderItem).SetMetrics(aggregate);
            yield return folderItem;
            yield break;
        }

        // Assets root: emit children at depth 0.
        foreach (FolderNode childFolder in folder.Children)
        {
            foreach (TreeViewItem<int> item in BuildItemsRecursive(childFolder, depth))
            {
                yield return item;
            }
        }

        foreach (FileNode file in folder.Files)
        {
            yield return new CsFileTreeViewItem(file.Id, depth, file.Name, file.AssetPath, false, file.Metrics);
        }
    }

    private sealed class FolderNode
    {
        public string Name { get; }
        public int Id { get; }
        public FolderNode Parent { get; }

        public List<FolderNode> Children { get; } = new(16);
        public List<FileNode> Files { get; } = new(64);

        public CsFileMetrics Aggregate { get; private set; }

        public FolderNode(string name, int id, FolderNode parent)
        {
            Name = name;
            Id = id;
            Parent = parent;
            Aggregate = new CsFileMetrics(0, 0, 0);
        }
    }

    private sealed class FileNode
    {
        public string Name { get; }
        public string AssetPath { get; }
        public int Id { get; }
        public CsFileMetrics Metrics { get; }

        public FileNode(string name, string assetPath, int id, CsFileMetrics metrics)
        {
            Name = name;
            AssetPath = assetPath;
            Id = id;
            Metrics = metrics;
        }
    }
}