using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace LegendaryTools.ViewBinding.Editor
{
    public sealed class BindingMemberPickerWindow : PopupWindowContent
    {
        private const int MaxSearchResults = 250;

        private readonly IReadOnlyList<BindingMemberDescriptor> roots;
        private readonly Action<BindingMemberDescriptor> onSelected;
        private readonly Dictionary<string, bool> expanded = new Dictionary<string, bool>();
        private readonly List<BindingMemberDescriptor> indexedDescriptors = new List<BindingMemberDescriptor>();
        private readonly List<string> indexedTerms = new List<string>();
        private readonly List<BindingMemberDescriptor> searchResults = new List<BindingMemberDescriptor>();
        private readonly bool requireReadable;
        private readonly bool requireWritable;
        private readonly Type compatibleType;
        private readonly Func<string, int, IReadOnlyList<BindingMemberDescriptor>> searchProvider;

        private string search = string.Empty;
        private bool searchIndexBuilt;
        private bool searchResultsTruncated;
        private Vector2 scroll;

        public BindingMemberPickerWindow(
            IReadOnlyList<BindingMemberDescriptor> roots,
            bool requireReadable,
            bool requireWritable,
            Action<BindingMemberDescriptor> onSelected,
            Type compatibleType = null,
            Func<string, int, IReadOnlyList<BindingMemberDescriptor>> searchProvider = null)
        {
            this.roots = roots ?? Array.Empty<BindingMemberDescriptor>();
            this.requireReadable = requireReadable;
            this.requireWritable = requireWritable;
            this.onSelected = onSelected;
            this.compatibleType = compatibleType;
            this.searchProvider = searchProvider;
        }

        public override Vector2 GetWindowSize()
        {
            return new Vector2(620f, 460f);
        }

        public override void OnGUI(Rect rect)
        {
            EditorGUILayout.Space(4f);

            EditorGUI.BeginChangeCheck();
            search = EditorGUILayout.TextField(search, EditorStyles.toolbarSearchField);
            if (EditorGUI.EndChangeCheck())
            {
                RefreshSearchResults();
            }

            EditorGUILayout.Space(3f);

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("Member", EditorStyles.miniBoldLabel);
                GUILayout.FlexibleSpace();
                GUILayout.Label("Type", BindingInspectorStyles.TypeLabelStyle, GUILayout.Width(220f));
            }

            scroll = EditorGUILayout.BeginScrollView(scroll);

            if (string.IsNullOrWhiteSpace(search))
            {
                DrawDescriptors(roots, 0);
            }
            else
            {
                DrawSearchResults();
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawDescriptors(IReadOnlyList<BindingMemberDescriptor> descriptors, int depth)
        {
            for (int i = 0; i < descriptors.Count; i++)
            {
                DrawDescriptor(descriptors[i], depth, false);
            }
        }

        private void DrawSearchResults()
        {
            for (int i = 0; i < searchResults.Count; i++)
            {
                DrawDescriptor(searchResults[i], 0, true);
            }

            if (searchResultsTruncated)
            {
                EditorGUILayout.HelpBox(
                    $"Showing the first {MaxSearchResults} matches. Refine the search to narrow the result set.",
                    MessageType.Info);
            }
            else if (searchResults.Count == 0)
            {
                EditorGUILayout.LabelField("No matching members found.", EditorStyles.centeredGreyMiniLabel);
            }
        }

        private void DrawDescriptor(BindingMemberDescriptor descriptor, int depth, bool searchMode)
        {
            bool hasChildren = descriptor.CanExpand;
            bool isExpanded = !searchMode && GetExpanded(descriptor.Path);

            Rect row = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
            Rect foldoutRect = new Rect(row.x + depth * 14f, row.y, 14f, row.height);
            Rect buttonRect = new Rect(foldoutRect.xMax, row.y, row.width - depth * 14f - 220f - 18f, row.height);
            Rect typeRect = new Rect(row.xMax - 220f, row.y, 220f, row.height);

            if (hasChildren && !searchMode)
            {
                bool nextExpanded = EditorGUI.Foldout(foldoutRect, isExpanded, GUIContent.none, false);
                if (nextExpanded != isExpanded)
                {
                    expanded[descriptor.Path] = nextExpanded;
                    isExpanded = nextExpanded;
                }
            }

            bool selectable = IsSelectable(descriptor);
            bool isCompatible = compatibleType != null && descriptor.ValueType == compatibleType;
            Color previousContentColor = GUI.contentColor;

            if (isCompatible)
            {
                GUI.contentColor = new Color(0.35f, 0.85f, 0.45f);
            }

            using (new EditorGUI.DisabledScope(!selectable))
            {
                string label = searchMode
                    ? GetSearchResultLabel(descriptor)
                    : descriptor.Name;

                if (GUI.Button(buttonRect, label, EditorStyles.label))
                {
                    onSelected?.Invoke(descriptor);
                    editorWindow.Close();
                }
            }

            string access = GetAccessSuffix(descriptor);
            string typeName = GetFriendlyTypeName(descriptor.ValueType) + access;
            GUI.Label(typeRect, typeName, BindingInspectorStyles.TypeLabelStyle);
            GUI.contentColor = previousContentColor;

            if (hasChildren && isExpanded)
            {
                DrawDescriptors(descriptor.Children, depth + 1);
            }
        }

        private void RefreshSearchResults()
        {
            searchResults.Clear();
            searchResultsTruncated = false;

            if (string.IsNullOrWhiteSpace(search))
            {
                return;
            }

            if (searchProvider != null)
            {
                IReadOnlyList<BindingMemberDescriptor> providedResults = searchProvider(
                    search.Trim(),
                    MaxSearchResults);

                for (int i = 0; i < providedResults.Count; i++)
                {
                    searchResults.Add(providedResults[i]);
                }

                searchResultsTruncated = providedResults.Count >= MaxSearchResults;
                return;
            }

            EnsureSearchIndex();
            string normalizedSearch = search.Trim().ToLowerInvariant();

            for (int i = 0; i < indexedDescriptors.Count; i++)
            {
                if (indexedTerms[i].IndexOf(normalizedSearch, StringComparison.Ordinal) < 0)
                {
                    continue;
                }

                searchResults.Add(indexedDescriptors[i]);
                if (searchResults.Count >= MaxSearchResults)
                {
                    searchResultsTruncated = i < indexedDescriptors.Count - 1;
                    break;
                }
            }
        }

        private void EnsureSearchIndex()
        {
            if (searchIndexBuilt)
            {
                return;
            }

            searchIndexBuilt = true;
            var stack = new Stack<BindingMemberDescriptor>();

            for (int i = roots.Count - 1; i >= 0; i--)
            {
                stack.Push(roots[i]);
            }

            while (stack.Count > 0)
            {
                BindingMemberDescriptor descriptor = stack.Pop();
                indexedDescriptors.Add(descriptor);

                string typeName = descriptor.ValueType?.FullName ?? string.Empty;
                string displayPath = GetSearchResultLabel(descriptor);
                indexedTerms.Add(
                    $"{descriptor.Name}\n{displayPath}\n{descriptor.Path}\n{typeName}".ToLowerInvariant());

                if (!descriptor.CanExpand)
                {
                    continue;
                }

                IReadOnlyList<BindingMemberDescriptor> children = descriptor.Children;
                for (int i = children.Count - 1; i >= 0; i--)
                {
                    stack.Push(children[i]);
                }
            }
        }

        private bool IsSelectable(BindingMemberDescriptor descriptor)
        {
            if (requireReadable && !descriptor.CanRead)
            {
                return false;
            }

            if (requireWritable && !descriptor.CanWrite)
            {
                return false;
            }

            return descriptor.CanRead || descriptor.CanWrite;
        }

        private bool GetExpanded(string path)
        {
            return expanded.TryGetValue(path, out bool value) && value;
        }

        private static string GetSearchResultLabel(BindingMemberDescriptor descriptor)
        {
            string displayPath = ComponentBindingPath.GetDisplayPath(descriptor.Path);
            return displayPath.StartsWith("$", StringComparison.Ordinal)
                ? descriptor.Name
                : displayPath;
        }

        private static string GetAccessSuffix(BindingMemberDescriptor descriptor)
        {
            if (descriptor.CanRead && descriptor.CanWrite)
            {
                return string.Empty;
            }

            if (descriptor.CanRead)
            {
                return "  [R]";
            }

            if (descriptor.CanWrite)
            {
                return "  [W]";
            }

            return "  [-]";
        }

        private static string GetFriendlyTypeName(Type type)
        {
            if (type == null)
            {
                return "Unknown";
            }

            if (!type.IsGenericType)
            {
                return type.Name;
            }

            string name = type.Name;
            int tickIndex = name.IndexOf('`');
            if (tickIndex >= 0)
            {
                name = name.Substring(0, tickIndex);
            }

            string arguments = string.Join(", ", type.GetGenericArguments().Select(GetFriendlyTypeName));
            return $"{name}<{arguments}>";
        }
    }
}
