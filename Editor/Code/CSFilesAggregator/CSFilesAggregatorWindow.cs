using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace LegendaryTools.Editor
{
    public sealed class CSFilesAggregatorWindow : EditorWindow
    {
        private const string PrefsKey = "LegendaryTools.CSFilesAggregatorWindow.State.v1";

        [Serializable]
        private sealed class PersistedState
        {
            public List<string> Paths = new List<string>();

            public bool IncludeSubfolders;
            public bool RemoveUsings;
            public bool StripImplementations;

            public bool ResolveDependencies;
            public int DependencyDepth = 1;
            public int ReportMaxItems = 40;
        }

        private readonly List<string> paths = new();

        private bool includeSubfolders;
        private bool removeUsings;
        private bool stripImplementations;

        private bool resolveDependencies;
        private int dependencyDepth = 1;

        private int reportMaxItems = 40;

        private string aggregatedText = string.Empty;
        private Vector2 aggregatedScroll;

        private CSFilesAggregatorController controller;

        [MenuItem("Tools/LegendaryTools/Code/C# File Aggregator")]
        public static void ShowWindow()
        {
            GetWindow<CSFilesAggregatorWindow>("C# File Aggregator");
        }

        private void OnEnable()
        {
            controller ??= new CSFilesAggregatorController();
            LoadState();
        }

        private void OnDisable()
        {
            SaveState();
        }

        private void OnDestroy()
        {
            SaveState();
        }

        private void OnGUI()
        {
            GUILayout.Label("Settings", EditorStyles.boldLabel);

            DrawAddButtons();
            DrawDropArea();
            DrawPathList();

            GUILayout.Space(10);

            includeSubfolders = EditorGUILayout.Toggle("Include subfolders", includeSubfolders);
            removeUsings = EditorGUILayout.Toggle("Remove 'using' declarations", removeUsings);
            stripImplementations = EditorGUILayout.Toggle("Strip implementations (keep signatures)", stripImplementations);

            GUILayout.Space(6);

            resolveDependencies = EditorGUILayout.Toggle("Resolve dependencies (Assets-only)", resolveDependencies);
            using (new EditorGUI.DisabledScope(!resolveDependencies))
            {
                dependencyDepth = EditorGUILayout.IntField("Dependency depth", Mathf.Max(0, dependencyDepth));
                reportMaxItems = EditorGUILayout.IntField("Report max items", Mathf.Clamp(reportMaxItems, 5, 500));
            }

            GUILayout.Space(10);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Aggregate .cs Files"))
                AggregateNow();

            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(aggregatedText)))
            {
                if (GUILayout.Button("Copy Aggregated Text", GUILayout.MaxWidth(170)))
                {
                    EditorGUIUtility.systemCopyBuffer = aggregatedText ?? string.Empty;
                    ShowCopyToast();
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(10);
            GUILayout.Label("Aggregated Content", EditorStyles.boldLabel);

            DrawAggregatedTextAreaWithScroll();

            if (GUI.changed)
                SaveState();
        }

        private void DrawAggregatedTextAreaWithScroll()
        {
            aggregatedScroll = EditorGUILayout.BeginScrollView(aggregatedScroll, GUILayout.ExpandHeight(true));

            EditorGUI.BeginChangeCheck();
            aggregatedText = EditorGUILayout.TextArea(aggregatedText, GUILayout.ExpandHeight(true));
            if (EditorGUI.EndChangeCheck())
            {
                // Keep state consistent if the user edits the text manually.
                SaveState();
            }

            EditorGUILayout.EndScrollView();
        }

        private void ShowCopyToast()
        {
            // Unity doesn't have a true toast API everywhere; this gives lightweight feedback.
            ShowNotification(new GUIContent("Aggregated text copied to clipboard."));
        }

        private void DrawAddButtons()
        {
            GUILayout.BeginHorizontal();

            if (GUILayout.Button("Add Folder"))
            {
                string selected = EditorUtility.OpenFolderPanel("Select folder", Application.dataPath, string.Empty);
                if (!string.IsNullOrEmpty(selected))
                {
                    string rel = CSFilesAggregatorUtils.TryToProjectRelativePath(selected);
                    TryAddPath(rel);
                }
            }

            if (GUILayout.Button("Add .cs File"))
            {
                string selected = EditorUtility.OpenFilePanel("Select .cs file", Application.dataPath, "cs");
                if (!string.IsNullOrEmpty(selected))
                {
                    string rel = CSFilesAggregatorUtils.TryToProjectRelativePath(selected);
                    TryAddPath(rel);
                }
            }

            GUILayout.EndHorizontal();
        }

        private void DrawDropArea()
        {
            Rect dropArea = GUILayoutUtility.GetRect(0.0f, 50.0f, GUILayout.ExpandWidth(true));
            GUI.Box(dropArea, "Drag and Drop Folders or .cs Files Here");

            Event evt = Event.current;

            if (evt.type == EventType.DragUpdated && dropArea.Contains(evt.mousePosition))
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                evt.Use();
                return;
            }

            if (evt.type == EventType.DragPerform && dropArea.Contains(evt.mousePosition))
            {
                DragAndDrop.AcceptDrag();
                foreach (string p in DragAndDrop.paths)
                {
                    bool isDir = Directory.Exists(p);
                    bool isCs = File.Exists(p) && p.EndsWith(".cs", StringComparison.OrdinalIgnoreCase);

                    if (!isDir && !isCs)
                        continue;

                    string rel = CSFilesAggregatorUtils.TryToProjectRelativePath(p);
                    TryAddPath(rel);
                }

                evt.Use();
            }
        }

        private void DrawPathList()
        {
            GUILayout.Label("Selected Folders and Files:");

            for (int i = 0; i < paths.Count; i++)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(paths[i]);

                if (GUILayout.Button("Remove", GUILayout.MaxWidth(70)))
                {
                    paths.RemoveAt(i);
                    i--;
                    SaveState();
                }

                GUILayout.EndHorizontal();
            }

            if (paths.Count > 0)
            {
                GUILayout.Space(4);
                if (GUILayout.Button("Clear All"))
                {
                    paths.Clear();
                    SaveState();
                }
            }
        }

        private void TryAddPath(string relOrAbs)
        {
            if (string.IsNullOrWhiteSpace(relOrAbs))
                return;

            if (!paths.Contains(relOrAbs))
            {
                paths.Add(relOrAbs);
                SaveState();
            }
        }

        private void AggregateNow()
        {
            if (paths.Count == 0)
            {
                EditorUtility.DisplayDialog("Error", "Please add at least one folder or .cs file.", "OK");
                return;
            }

            CSFilesAggregatorController.Options options = new(
                includeSubfolders,
                removeUsings,
                stripImplementations,
                resolveDependencies,
                Mathf.Max(0, dependencyDepth),
                Mathf.Clamp(reportMaxItems, 5, 500));

            CSFilesAggregatorController.AggregationResult result = controller.Aggregate(paths, options);

            aggregatedText = result.AggregatedText;
            EditorGUIUtility.systemCopyBuffer = aggregatedText;

            // Optional: scroll to top after aggregation so user sees the start.
            aggregatedScroll = Vector2.zero;

            CSFilesAggregatorUtils.ShowSingleReportPrompt(result.ReportText);

            SaveState();
        }

        private void SaveState()
        {
            PersistedState state = new PersistedState
            {
                Paths = new List<string>(paths),

                IncludeSubfolders = includeSubfolders,
                RemoveUsings = removeUsings,
                StripImplementations = stripImplementations,

                ResolveDependencies = resolveDependencies,
                DependencyDepth = dependencyDepth,
                ReportMaxItems = reportMaxItems
            };

            string json = EditorJsonUtility.ToJson(state);
            EditorPrefs.SetString(PrefsKey, json);
        }

        private void LoadState()
        {
            if (!EditorPrefs.HasKey(PrefsKey))
                return;

            string json = EditorPrefs.GetString(PrefsKey, string.Empty);
            if (string.IsNullOrEmpty(json))
                return;

            PersistedState state = new PersistedState();

            try
            {
                EditorJsonUtility.FromJsonOverwrite(json, state);
            }
            catch
            {
                return;
            }

            paths.Clear();
            if (state.Paths != null)
                paths.AddRange(state.Paths);

            includeSubfolders = state.IncludeSubfolders;
            removeUsings = state.RemoveUsings;
            stripImplementations = state.StripImplementations;

            resolveDependencies = state.ResolveDependencies;
            dependencyDepth = Mathf.Max(0, state.DependencyDepth);
            reportMaxItems = Mathf.Clamp(state.ReportMaxItems, 5, 500);
        }
    }
}
