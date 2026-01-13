using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace LegendaryTools.Editor
{
    public sealed class CSFilesAggregatorWindow : EditorWindow
    {
        private readonly List<string> paths = new();

        private bool includeSubfolders;
        private bool removeUsings;

        private bool resolveDependencies;
        private int dependencyDepth = 1;

        private int reportMaxItems = 40;

        private string aggregatedText = string.Empty;

        private CSFilesAggregatorController controller;

        [MenuItem("Tools/LegendaryTools/Code/C# File Aggregator")]
        public static void ShowWindow()
        {
            GetWindow<CSFilesAggregatorWindow>("C# File Aggregator");
        }

        private void OnEnable()
        {
            controller ??= new CSFilesAggregatorController();
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

            GUILayout.Space(6);

            resolveDependencies = EditorGUILayout.Toggle("Resolve dependencies (Assets-only)", resolveDependencies);
            using (new EditorGUI.DisabledScope(!resolveDependencies))
            {
                dependencyDepth = EditorGUILayout.IntField("Dependency depth", Mathf.Max(0, dependencyDepth));
                reportMaxItems = EditorGUILayout.IntField("Report max items", Mathf.Clamp(reportMaxItems, 5, 500));

                EditorGUILayout.HelpBox(
                    "When Resolve dependencies is enabled, dependency contents are always included in the aggregated output.",
                    MessageType.Info);
            }

            GUILayout.Space(10);

            if (GUILayout.Button("Aggregate .cs Files")) AggregateNow();

            GUILayout.Space(10);
            GUILayout.Label("Aggregated Content", EditorStyles.boldLabel);

            aggregatedText = EditorGUILayout.TextArea(aggregatedText, GUILayout.ExpandHeight(true));
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
                    if (!paths.Contains(rel))
                        paths.Add(rel);
                }
            }

            if (GUILayout.Button("Add .cs File"))
            {
                string selected = EditorUtility.OpenFilePanel("Select .cs file", Application.dataPath, "cs");
                if (!string.IsNullOrEmpty(selected))
                {
                    string rel = CSFilesAggregatorUtils.TryToProjectRelativePath(selected);
                    if (!paths.Contains(rel))
                        paths.Add(rel);
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
                    string rel = CSFilesAggregatorUtils.TryToProjectRelativePath(p);

                    bool isDir = Directory.Exists(p);
                    bool isCs = File.Exists(p) && p.EndsWith(".cs");

                    if (!isDir && !isCs)
                        continue;

                    if (!paths.Contains(rel))
                        paths.Add(rel);
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
                }

                GUILayout.EndHorizontal();
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
                resolveDependencies,
                Mathf.Max(0, dependencyDepth),
                Mathf.Clamp(reportMaxItems, 5, 500));

            CSFilesAggregatorController.AggregationResult result = controller.Aggregate(paths, options);

            aggregatedText = result.AggregatedText;
            EditorGUIUtility.systemCopyBuffer = aggregatedText;

            CSFilesAggregatorUtils.ShowSingleReportPrompt(result.ReportText);
        }
    }
}