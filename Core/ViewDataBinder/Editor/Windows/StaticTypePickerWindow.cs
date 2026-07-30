using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEditor;
using UnityEngine;

namespace LegendaryTools.ViewBinding.Editor
{
    public sealed class StaticTypePickerWindow : PopupWindowContent
    {
        private const int PageSize = 100;
        private const double SearchIndexBudgetSeconds = 0.006d;

        private static IReadOnlyList<Type> cachedTypes;
        private static IReadOnlyList<AssemblyTypeGroup> cachedAssemblyGroups;

        private readonly Action<Type> onSelected;
        private readonly IReadOnlyList<Type> types;
        private readonly IReadOnlyList<AssemblyTypeGroup> assemblyGroups;
        private readonly bool allowNone;
        private readonly bool groupByAssembly;
        private readonly Dictionary<string, bool> assemblyFoldouts =
            new Dictionary<string, bool>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> visibleTypeCounts =
            new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly List<SearchGroup> searchGroups = new List<SearchGroup>();
        private string search = string.Empty;
        private string indexedSearch = string.Empty;
        private int searchAssemblyIndex;
        private Vector2 scroll;

        public StaticTypePickerWindow(
            Action<Type> onSelected,
            bool staticTypesOnly = true,
            bool allowNone = false)
        {
            this.onSelected = onSelected;
            this.allowNone = allowNone;
            groupByAssembly = !staticTypesOnly;
            types = staticTypesOnly ? GetStaticTypes() : null;
            assemblyGroups = staticTypesOnly ? null : GetAssemblyGroups();
        }

        public override Vector2 GetWindowSize()
        {
            return new Vector2(520f, 420f);
        }

        public override void OnGUI(Rect rect)
        {
            EditorGUILayout.Space(4f);
            GUI.SetNextControlName("BindingTypeSearch");
            string nextSearch = EditorGUILayout.TextField(search, EditorStyles.toolbarSearchField);
            if (!string.Equals(nextSearch, search, StringComparison.Ordinal))
            {
                search = nextSearch;
                RestartSearchIndex();
            }
            EditorGUILayout.Space(4f);

            if (groupByAssembly)
            {
                ProcessSearchIndex();
                DrawIndexingStatus();
            }

            scroll = EditorGUILayout.BeginScrollView(scroll);

            if (allowNone && GUILayout.Button("(None)", EditorStyles.label))
            {
                onSelected?.Invoke(null);
                editorWindow.Close();
            }

            if (groupByAssembly)
            {
                DrawAssemblyGroups();
            }
            else
            {
                IEnumerable<Type> filtered = types;
                if (!string.IsNullOrWhiteSpace(search))
                {
                    filtered = filtered.Where(type =>
                        type.FullName != null &&
                        type.FullName.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0);
                }

                foreach (Type type in filtered.Take(300))
                {
                    DrawType(type);
                }
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawAssemblyGroups()
        {
            bool searching = !string.IsNullOrWhiteSpace(search);
            if (searching)
            {
                for (int i = 0; i < searchGroups.Count; i++)
                {
                    SearchGroup searchGroup = searchGroups[i];
                    DrawLoadedGroup(
                        searchGroup.Group,
                        searchGroup.Types,
                        true);
                }

                return;
            }

            for (int i = 0; i < assemblyGroups.Count; i++)
            {
                DrawAssemblyGroup(assemblyGroups[i]);
            }
        }

        private void DrawAssemblyGroup(AssemblyTypeGroup group)
        {
            bool expanded = assemblyFoldouts.TryGetValue(group.Key, out bool stored) && stored;
            string count = group.IsLoaded ? $" ({group.Types.Length})" : string.Empty;
            bool nextExpanded = EditorGUILayout.Foldout(
                expanded,
                group.Label + count,
                true);
            assemblyFoldouts[group.Key] = nextExpanded;

            if (!nextExpanded)
            {
                return;
            }

            group.EnsureLoaded();
            DrawTypePage(group, group.Types);
        }

        private void DrawLoadedGroup(
            AssemblyTypeGroup group,
            IReadOnlyList<Type> groupTypes,
            bool forceExpanded)
        {
            bool expanded = forceExpanded ||
                            (assemblyFoldouts.TryGetValue(group.Key, out bool stored) && stored);
            bool nextExpanded = EditorGUILayout.Foldout(
                expanded,
                $"{group.Label} ({groupTypes.Count})",
                true);
            if (!forceExpanded)
            {
                assemblyFoldouts[group.Key] = nextExpanded;
            }

            if (nextExpanded)
            {
                DrawTypePage(group, groupTypes);
            }
        }

        private void DrawTypePage(AssemblyTypeGroup group, IReadOnlyList<Type> groupTypes)
        {
            int visibleCount = visibleTypeCounts.TryGetValue(group.Key, out int stored)
                ? stored
                : PageSize;
            int count = Math.Min(visibleCount, groupTypes.Count);

            EditorGUI.indentLevel++;
            for (int i = 0; i < count; i++)
            {
                DrawType(groupTypes[i]);
            }

            if (count < groupTypes.Count &&
                GUILayout.Button(
                    $"Show {Math.Min(PageSize, groupTypes.Count - count)} more...",
                    EditorStyles.miniButton))
            {
                visibleTypeCounts[group.Key] = count + PageSize;
            }
            EditorGUI.indentLevel--;
        }

        private void RestartSearchIndex()
        {
            indexedSearch = search.Trim();
            searchAssemblyIndex = 0;
            searchGroups.Clear();
            visibleTypeCounts.Clear();
        }

        private void ProcessSearchIndex()
        {
            if (string.IsNullOrWhiteSpace(indexedSearch) ||
                searchAssemblyIndex >= assemblyGroups.Count ||
                Event.current.type != EventType.Layout)
            {
                return;
            }

            double deadline = EditorApplication.timeSinceStartup + SearchIndexBudgetSeconds;
            do
            {
                AssemblyTypeGroup group = assemblyGroups[searchAssemblyIndex++];
                group.EnsureLoaded();

                Type[] matches = group.Types
                    .Where(type => type.FullName.IndexOf(
                        indexedSearch,
                        StringComparison.OrdinalIgnoreCase) >= 0)
                    .ToArray();
                if (matches.Length > 0)
                {
                    searchGroups.Add(new SearchGroup(group, matches));
                }
            }
            while (searchAssemblyIndex < assemblyGroups.Count &&
                   EditorApplication.timeSinceStartup < deadline);

            if (searchAssemblyIndex < assemblyGroups.Count)
            {
                editorWindow?.Repaint();
            }
        }

        private void DrawIndexingStatus()
        {
            if (!string.IsNullOrWhiteSpace(indexedSearch) &&
                searchAssemblyIndex < assemblyGroups.Count)
            {
                EditorGUILayout.LabelField(
                    $"Indexing assemblies... {searchAssemblyIndex}/{assemblyGroups.Count}",
                    EditorStyles.miniLabel);
            }
        }

        private void DrawType(Type type)
        {
            string label = type.FullName ?? type.Name;
            if (GUILayout.Button(label, EditorStyles.label))
            {
                onSelected?.Invoke(type);
                editorWindow.Close();
            }
        }

        private static string GetAssemblyDisplayName(Assembly assembly)
        {
            return assembly.GetName().Name ?? assembly.FullName ?? "Unknown Assembly";
        }

        private static IReadOnlyList<Type> GetStaticTypes()
        {
            if (cachedTypes != null)
            {
                return cachedTypes;
            }

            var result = new List<Type>();
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    result.AddRange(assembly
                        .GetTypes()
                        .Where(HasBindableStaticMember));
                }
                catch (ReflectionTypeLoadException exception)
                {
                    result.AddRange(exception.Types
                        .Where(type => type != null && HasBindableStaticMember(type)));
                }
            }

            cachedTypes = result
                .Distinct()
                .OrderBy(type => type.FullName, StringComparer.Ordinal)
                .ToArray();
            return cachedTypes;
        }

        private static IReadOnlyList<AssemblyTypeGroup> GetAssemblyGroups()
        {
            if (cachedAssemblyGroups != null)
            {
                return cachedAssemblyGroups;
            }

            cachedAssemblyGroups = AppDomain.CurrentDomain.GetAssemblies()
                .Distinct()
                .OrderBy(GetAssemblyDisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(assembly => assembly.FullName, StringComparer.Ordinal)
                .Select(assembly => new AssemblyTypeGroup(assembly))
                .ToArray();
            return cachedAssemblyGroups;
        }

        private static bool IsBindableInstanceType(Type type)
        {
            return type != null &&
                   !string.IsNullOrWhiteSpace(type.FullName) &&
                   type.FullName[0] != '<' &&
                   !type.IsDefined(typeof(CompilerGeneratedAttribute), false) &&
                   !type.ContainsGenericParameters &&
                   type != typeof(void) &&
                   !(type.IsAbstract && type.IsSealed) &&
                   !type.IsPointer &&
                   !type.IsByRef;
        }

        private static bool HasBindableStaticMember(Type type)
        {
            if (type == null || type.ContainsGenericParameters)
            {
                return false;
            }

            const BindingFlags flags = BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy;

            if (type.GetFields(flags).Any(field => !field.IsSpecialName))
            {
                return true;
            }

            return type.GetProperties(flags).Any(property => property.GetIndexParameters().Length == 0);
        }

        private sealed class AssemblyTypeGroup
        {
            private readonly Assembly assembly;
            private Type[] types;

            public AssemblyTypeGroup(Assembly assembly)
            {
                this.assembly = assembly;
                Label = GetAssemblyDisplayName(assembly);
                Key = assembly.FullName ?? Label;
            }

            public string Key { get; }

            public string Label { get; }

            public bool IsLoaded => types != null;

            public Type[] Types => types ?? Array.Empty<Type>();

            public void EnsureLoaded()
            {
                if (types != null)
                {
                    return;
                }

                IEnumerable<Type> assemblyTypes;
                try
                {
                    assemblyTypes = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException exception)
                {
                    assemblyTypes = exception.Types;
                }
                catch
                {
                    types = Array.Empty<Type>();
                    return;
                }

                types = assemblyTypes
                    .Where(IsBindableInstanceType)
                    .Distinct()
                    .OrderBy(type => type.FullName, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
        }

        private readonly struct SearchGroup
        {
            public SearchGroup(AssemblyTypeGroup group, Type[] types)
            {
                Group = group;
                Types = types;
            }

            public AssemblyTypeGroup Group { get; }

            public Type[] Types { get; }
        }
    }
}
