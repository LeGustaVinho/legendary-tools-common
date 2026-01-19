using System;
using System.Collections.Generic;
using System.Linq;
using LegendaryTools.Editor.Code.CSFilesAggregator.Pipeline;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace LegendaryTools.Editor.Code.CSFilesAggregator
{
    /// <summary>
    /// TreeView that displays the effective files included in the aggregation plan, grouped by folders.
    /// </summary>
    public sealed class CSFilesAggregatorFilesTreeView : TreeView
    {
        private const float CheckboxWidth = 18f;
        private const float ButtonWidth = 80f;
        private const float RightPadding = 6f;
        private const float Spacing = 6f;

        private readonly Func<string, IReadOnlyList<string>> _resolveDependenciesByDisplayPath;
        private readonly Action<string, bool> _onDoNotStripChanged;

        private readonly Dictionary<int, AggregationPlanFile> _fileById = new();
        private readonly Dictionary<string, int> _idByDisplayPath = new(StringComparer.Ordinal);
        private readonly Dictionary<int, bool> _doNotStripCheckboxStateById = new();

        private List<AggregationPlanFile> _files = new();
        private int _nextId = 1;

        private HashSet<string> _seedDoNotStripPaths = new(StringComparer.Ordinal);

        /// <summary>
        /// Creates a new TreeView.
        /// </summary>
        public CSFilesAggregatorFilesTreeView(
            TreeViewState state,
            Func<string, IReadOnlyList<string>> resolveDependenciesByDisplayPath,
            Action<string, bool> onDoNotStripChanged)
            : base(state)
        {
            _resolveDependenciesByDisplayPath = resolveDependenciesByDisplayPath;
            _onDoNotStripChanged = onDoNotStripChanged;

            showBorder = true;
            showAlternatingRowBackgrounds = true;
            Reload();
        }

        /// <summary>
        /// Updates the data shown in the tree.
        /// </summary>
        public void SetFiles(IReadOnlyList<AggregationPlanFile> files,
            IReadOnlyCollection<string> doNotStripDisplayPaths)
        {
            _files = files != null ? new List<AggregationPlanFile>(files) : new List<AggregationPlanFile>();
            _fileById.Clear();
            _idByDisplayPath.Clear();
            _doNotStripCheckboxStateById.Clear();

            _nextId = 1;

            _seedDoNotStripPaths = doNotStripDisplayPaths != null
                ? new HashSet<string>(doNotStripDisplayPaths, StringComparer.Ordinal)
                : new HashSet<string>(StringComparer.Ordinal);

            Reload();
        }

        /// <summary>
        /// Sets all file checkboxes to the given value.
        /// </summary>
        /// <remarks>
        /// Checkbox semantics: true = do NOT strip implementation; false = strip (when enabled).
        /// </remarks>
        public void SetAllDoNotStrip(bool doNotStrip)
        {
            if (_fileById.Count == 0) return;

            foreach (KeyValuePair<int, AggregationPlanFile> kvp in _fileById)
            {
                int id = kvp.Key;
                AggregationPlanFile file = kvp.Value;

                if (file == null || string.IsNullOrWhiteSpace(file.DisplayPath)) continue;

                _doNotStripCheckboxStateById[id] = doNotStrip;
                _onDoNotStripChanged?.Invoke(file.DisplayPath, doNotStrip);
            }

            GUI.changed = true;
        }

        /// <inheritdoc />
        protected override TreeViewItem BuildRoot()
        {
            TreeViewItem root = new(0, -1, "Root");

            if (_files == null || _files.Count == 0)
            {
                root.children = new List<TreeViewItem>();
                return root;
            }

            Dictionary<string, TreeViewItem> folderNodeByPath = new(StringComparer.Ordinal);
            Dictionary<int, string> sortKeyById = new();

            for (int i = 0; i < _files.Count; i++)
            {
                AggregationPlanFile file = _files[i];
                string displayPath = file?.DisplayPath ?? string.Empty;
                if (string.IsNullOrEmpty(displayPath)) continue;

                string[] parts = displayPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0) continue;

                // Build folder chain.
                TreeViewItem parent = root;
                string currentFolderPath = string.Empty;

                for (int p = 0; p < parts.Length - 1; p++)
                {
                    string folderName = parts[p];
                    currentFolderPath = string.IsNullOrEmpty(currentFolderPath)
                        ? folderName
                        : $"{currentFolderPath}/{folderName}";

                    if (!folderNodeByPath.TryGetValue(currentFolderPath, out TreeViewItem folderNode))
                    {
                        int id = _nextId++;
                        folderNode = new TreeViewItem(id, parent.depth + 1, folderName)
                        {
                            children = new List<TreeViewItem>()
                        };

                        folderNodeByPath[currentFolderPath] = folderNode;

                        AddChild(parent, folderNode);
                        sortKeyById[id] = $"0|{currentFolderPath}";
                    }

                    parent = folderNode;
                }

                // Add file leaf.
                string fileName = parts[parts.Length - 1];
                int fileId = _nextId++;

                string label = fileName;
                if (file.Source == AggregationPlanFileSource.Dependency) label = $"{fileName}  (dep)";

                TreeViewItem fileItem = new(fileId, parent.depth + 1, label);
                AddChild(parent, fileItem);

                _fileById[fileId] = file;
                _idByDisplayPath[displayPath] = fileId;

                // Checkbox true => DO NOT STRIP.
                bool doNotStrip = _seedDoNotStripPaths != null && _seedDoNotStripPaths.Contains(displayPath);
                _doNotStripCheckboxStateById[fileId] = doNotStrip;

                sortKeyById[fileId] = $"1|{displayPath}";
            }

            SortRecursively(root, sortKeyById);
            SetupDepthsFromParentsAndChildren(root);
            return root;
        }

        /// <inheritdoc />
        protected override void RowGUI(RowGUIArgs args)
        {
            if (args.item == null) return;

            bool isFile = _fileById.ContainsKey(args.item.id);
            if (!isFile)
            {
                base.RowGUI(args);
                return;
            }

            // Reserve space on the right for the checkbox + button.
            Rect rowRect = args.rowRect;

            float controlsWidth = ButtonWidth + Spacing + CheckboxWidth + RightPadding;
            Rect labelRect = rowRect;
            labelRect.xMax = Mathf.Max(labelRect.xMin, rowRect.xMax - controlsWidth);

            Rect checkboxRect = rowRect;
            checkboxRect.xMin = rowRect.xMax - (ButtonWidth + Spacing + CheckboxWidth + RightPadding);
            checkboxRect.xMax = checkboxRect.xMin + CheckboxWidth;
            checkboxRect.yMin += 1f;
            checkboxRect.height = EditorGUIUtility.singleLineHeight;

            Rect buttonRect = rowRect;
            buttonRect.xMin = rowRect.xMax - (ButtonWidth + RightPadding);
            buttonRect.xMax = rowRect.xMax - RightPadding;
            buttonRect.yMin += 1f;
            buttonRect.height = EditorGUIUtility.singleLineHeight;

            // Draw label with proper indentation/selection visuals.
            RowGUIArgs labelArgs = args;
            labelArgs.rowRect = labelRect;
            base.RowGUI(labelArgs);

            // Checkbox behavior:
            // - true  => do NOT strip implementation for this file
            // - false => strip implementation (when stripper is enabled globally)
            _doNotStripCheckboxStateById.TryGetValue(args.item.id, out bool currentDoNotStrip);
            bool nextDoNotStrip = EditorGUI.Toggle(checkboxRect, currentDoNotStrip);

            if (nextDoNotStrip != currentDoNotStrip)
            {
                _doNotStripCheckboxStateById[args.item.id] = nextDoNotStrip;

                AggregationPlanFile file = _fileById[args.item.id];
                string displayPath = file?.DisplayPath ?? string.Empty;
                if (!string.IsNullOrEmpty(displayPath)) _onDoNotStripChanged?.Invoke(displayPath, nextDoNotStrip);
            }

            // Get Deps behavior:
            // When clicked, scan dependencies of this file and mark checkbox = true (doNotStrip)
            // for every dependency that exists in the current tree list.
            if (GUI.Button(buttonRect, "Get Deps"))
            {
                AggregationPlanFile file = _fileById[args.item.id];
                string rootDisplayPath = file?.DisplayPath ?? string.Empty;

                if (!string.IsNullOrEmpty(rootDisplayPath) && _resolveDependenciesByDisplayPath != null)
                {
                    IReadOnlyList<string> deps = _resolveDependenciesByDisplayPath.Invoke(rootDisplayPath);
                    if (deps != null && deps.Count > 0)
                    {
                        for (int i = 0; i < deps.Count; i++)
                        {
                            string depPath = deps[i];
                            if (string.IsNullOrWhiteSpace(depPath)) continue;

                            if (_idByDisplayPath.TryGetValue(depPath, out int depId))
                            {
                                // Mark dependency as do-not-strip (checkbox true).
                                _doNotStripCheckboxStateById[depId] = true;
                                _onDoNotStripChanged?.Invoke(depPath, true);
                            }
                        }

                        // Force UI update this frame.
                        GUI.changed = true;
                    }
                }
            }
        }

        /// <inheritdoc />
        protected override void DoubleClickedItem(int id)
        {
            if (!_fileById.TryGetValue(id, out AggregationPlanFile file) || file == null) return;

            string displayPath = file.DisplayPath ?? string.Empty;

            // Prefer opening via AssetDatabase if possible.
            if (displayPath.StartsWith("Assets/", StringComparison.Ordinal) ||
                string.Equals(displayPath, "Assets", StringComparison.Ordinal))
            {
                MonoScript script = AssetDatabase.LoadAssetAtPath<MonoScript>(displayPath);
                if (script != null)
                {
                    AssetDatabase.OpenAsset(script);
                    return;
                }

                UnityEngine.Object obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(displayPath);
                if (obj != null)
                {
                    AssetDatabase.OpenAsset(obj);
                    return;
                }
            }

            // Fallback: open external file if absolute exists.
            if (!string.IsNullOrWhiteSpace(file.AbsolutePath))
                UnityEditorInternal.InternalEditorUtility.OpenFileAtLineExternal(file.AbsolutePath, 1);
        }

        /// <inheritdoc />
        protected override void ContextClickedItem(int id)
        {
            if (!_fileById.TryGetValue(id, out AggregationPlanFile file) || file == null) return;

            GenericMenu menu = new();

            menu.AddItem(new GUIContent("Copy Display Path"), false,
                () => { EditorGUIUtility.systemCopyBuffer = file.DisplayPath ?? string.Empty; });

            menu.AddItem(new GUIContent("Copy Absolute Path"), false,
                () => { EditorGUIUtility.systemCopyBuffer = file.AbsolutePath ?? string.Empty; });

            menu.AddSeparator(string.Empty);

            menu.AddItem(new GUIContent("Open"), false, () => DoubleClickedItem(id));

            menu.AddItem(new GUIContent("Ping Asset"), false, () =>
            {
                string displayPath = file.DisplayPath ?? string.Empty;
                if (displayPath.StartsWith("Assets/", StringComparison.Ordinal) ||
                    string.Equals(displayPath, "Assets", StringComparison.Ordinal))
                {
                    UnityEngine.Object obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(displayPath);
                    if (obj != null) EditorGUIUtility.PingObject(obj);
                }
            });

            menu.ShowAsContext();
        }

        private static void AddChild(TreeViewItem parent, TreeViewItem child)
        {
            if (parent == null || child == null) return;

            if (parent.children == null) parent.children = new List<TreeViewItem>();

            parent.children.Add(child);
        }

        private static void SortRecursively(TreeViewItem item, Dictionary<int, string> sortKeyById)
        {
            if (item == null || item.children == null || item.children.Count == 0) return;

            item.children = item.children
                .OrderBy(c =>
                {
                    if (c == null) return "2|";

                    return sortKeyById.TryGetValue(c.id, out string key) ? key : $"2|{c.displayName}";
                }, StringComparer.Ordinal)
                .ToList();

            for (int i = 0; i < item.children.Count; i++)
            {
                SortRecursively(item.children[i], sortKeyById);
            }
        }
    }
}