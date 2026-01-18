using CSharpRegexStripper;
using UnityEditor;
using UnityEngine;

namespace LegendaryTools.Editor.Code.CSFilesAggregator
{
    /// <summary>
    /// View-only EditorWindow for aggregating C# files.
    /// </summary>
    public sealed class CSFilesAggregatorWindow : EditorWindow
    {
        private enum Tab
        {
            Selection = 0,
            Settings = 1,
            Output = 2
        }

        private CSFilesAggregatorController _controller;
        private Vector2 _scrollPosition;
        private Tab _activeTab;

        [MenuItem("Tools/LegendaryTools/Code/C# File Aggregator")]
        public static void ShowWindow()
        {
            GetWindow<CSFilesAggregatorWindow>("C# File Aggregator");
        }

        private void OnEnable()
        {
            _controller = CSFilesAggregatorCompositionRoot.CreateController();
            _controller.StateChanged += Repaint;
        }

        private void OnDisable()
        {
            if (_controller != null)
            {
                _controller.StateChanged -= Repaint;
            }
        }

        private void OnGUI()
        {
            if (_controller == null)
            {
                EditorGUILayout.HelpBox("Controller not initialized.", MessageType.Error);
                return;
            }

            DrawToolbar();

            EditorGUILayout.Space(6);

            switch (_activeTab)
            {
                case Tab.Selection:
                    DrawSelectionTab();
                    break;

                case Tab.Settings:
                    DrawSettingsTab();
                    break;

                case Tab.Output:
                    DrawOutputTab();
                    break;
            }
        }

        private void DrawToolbar()
        {
            string[] tabs = { "Selection", "Settings", "Output" };
            _activeTab = (Tab)GUILayout.Toolbar((int)_activeTab, tabs);
        }

        private void DrawSelectionTab()
        {
            CSFilesAggregatorState state = _controller.State;

            EditorGUILayout.LabelField("Selection", EditorStyles.boldLabel);

            DrawAddButtons();
            DrawDropArea();

            EditorGUILayout.Space(8);

            DrawSelectedPathsList(state);
        }

        private void DrawSettingsTab()
        {
            CSFilesAggregatorState state = _controller.State;

            EditorGUILayout.LabelField("Settings", EditorStyles.boldLabel);

            bool includeSubfolders = EditorGUILayout.Toggle("Include subfolders", state.IncludeSubfolders);
            if (includeSubfolders != state.IncludeSubfolders)
            {
                _controller.SetIncludeSubfolders(includeSubfolders);
            }

            bool removeUsings = EditorGUILayout.Toggle("Remove 'using' declarations", state.RemoveUsings);
            if (removeUsings != state.RemoveUsings)
            {
                _controller.SetRemoveUsings(removeUsings);
            }

            bool appendDelimiters = EditorGUILayout.Toggle("Append end markers (End of file/folder)", state.AppendDelimiters);
            if (appendDelimiters != state.AppendDelimiters)
            {
                _controller.SetAppendDelimiters(appendDelimiters);
            }

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Implementation Stripper", EditorStyles.boldLabel);

            bool useStripper = EditorGUILayout.Toggle("Strip implementation (method bodies)", state.UseImplementationStripper);
            if (useStripper != state.UseImplementationStripper)
            {
                _controller.SetUseImplementationStripper(useStripper);
            }

            using (new EditorGUI.DisabledScope(!state.UseImplementationStripper))
            {
                MethodBodyMode bodyMode = (MethodBodyMode)EditorGUILayout.EnumPopup("Method body mode", state.StripMethodBodyMode);
                if (bodyMode != state.StripMethodBodyMode)
                {
                    _controller.SetStripMethodBodyMode(bodyMode);
                }

                bool convertProps = EditorGUILayout.Toggle("Convert non-auto properties", state.StripConvertNonAutoProperties);
                if (convertProps != state.StripConvertNonAutoProperties)
                {
                    _controller.SetStripConvertNonAutoProperties(convertProps);
                }

                bool mask = EditorGUILayout.Toggle("Mask strings & comments", state.StripMaskStringsAndComments);
                if (mask != state.StripMaskStringsAndComments)
                {
                    _controller.SetStripMaskStringsAndComments(mask);
                }

                bool skipInterface = EditorGUILayout.Toggle("Skip interface members", state.StripSkipInterfaceMembers);
                if (skipInterface != state.StripSkipInterfaceMembers)
                {
                    _controller.SetStripSkipInterfaceMembers(skipInterface);
                }

                bool skipAbstract = EditorGUILayout.Toggle("Skip abstract members", state.StripSkipAbstractMembers);
                if (skipAbstract != state.StripSkipAbstractMembers)
                {
                    _controller.SetStripSkipAbstractMembers(skipAbstract);
                }
            }

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Dependency Scan", EditorStyles.boldLabel);

            bool includeDeps = EditorGUILayout.Toggle("Include dependencies of selected files", state.IncludeDependencies);
            if (includeDeps != state.IncludeDependencies)
            {
                _controller.SetIncludeDependencies(includeDeps);
            }

            using (new EditorGUI.DisabledScope(!state.IncludeDependencies))
            {
                int maxDepth = EditorGUILayout.IntSlider("Max depth", state.DependencyMaxDepth, 0, 10);
                if (maxDepth != state.DependencyMaxDepth)
                {
                    _controller.SetDependencyMaxDepth(maxDepth);
                }

                bool ignorePackages = EditorGUILayout.Toggle("Ignore Packages/", state.DependencyIgnorePackagesFolder);
                if (ignorePackages != state.DependencyIgnorePackagesFolder)
                {
                    _controller.SetDependencyIgnorePackagesFolder(ignorePackages);
                }

                bool ignoreCache = EditorGUILayout.Toggle("Ignore Library/PackageCache/", state.DependencyIgnorePackageCache);
                if (ignoreCache != state.DependencyIgnorePackageCache)
                {
                    _controller.SetDependencyIgnorePackageCache(ignoreCache);
                }

                bool ignoreUnresolved = EditorGUILayout.Toggle("Ignore unresolved types", state.DependencyIgnoreUnresolvedTypes);
                if (ignoreUnresolved != state.DependencyIgnoreUnresolvedTypes)
                {
                    _controller.SetDependencyIgnoreUnresolvedTypes(ignoreUnresolved);
                }

                bool includeInputsInResult = EditorGUILayout.Toggle("Include input files in result", state.DependencyIncludeInputFilesInResult);
                if (includeInputsInResult != state.DependencyIncludeInputFilesInResult)
                {
                    _controller.SetDependencyIncludeInputFilesInResult(includeInputsInResult);
                }

                bool includeVirtual = EditorGUILayout.Toggle("Include in-memory virtual paths", state.DependencyIncludeInMemoryVirtualPathsInResult);
                if (includeVirtual != state.DependencyIncludeInMemoryVirtualPathsInResult)
                {
                    _controller.SetDependencyIncludeInMemoryVirtualPathsInResult(includeVirtual);
                }

                EditorGUILayout.HelpBox(
                    "When enabled, the tool rebuilds the Type Index before scanning dependencies to ensure results are up to date.",
                    MessageType.Info);
            }

            EditorGUILayout.Space(6);
            EditorGUILayout.HelpBox("Settings and selection persist between assembly reloads.", MessageType.Info);
        }

        private void DrawOutputTab()
        {
            CSFilesAggregatorState state = _controller.State;

            EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();

            using (new EditorGUI.DisabledScope(state.Paths.Count == 0))
            {
                if (GUILayout.Button("Aggregate .cs Files"))
                {
                    _controller.Aggregate();
                }
            }

            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(state.AggregatedText)))
            {
                if (GUILayout.Button("Copy", GUILayout.MaxWidth(100)))
                {
                    _controller.CopyAggregatedTextToClipboard();
                }
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(8);

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition, GUILayout.ExpandHeight(true));
            string editedText = EditorGUILayout.TextArea(state.AggregatedText ?? string.Empty, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();

            if (!string.Equals(editedText, state.AggregatedText, System.StringComparison.Ordinal))
            {
                _controller.SetAggregatedText(editedText);
            }
        }

        private void DrawAddButtons()
        {
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("Add Folder"))
            {
                _controller.RequestAddFolder();
            }

            if (GUILayout.Button("Add .cs File"))
            {
                _controller.RequestAddFile();
            }

            EditorGUILayout.EndHorizontal();
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
                _controller.AddPathsFromDragAndDrop(DragAndDrop.paths);
                evt.Use();
            }
        }

        private void DrawSelectedPathsList(CSFilesAggregatorState state)
        {
            EditorGUILayout.LabelField("Selected Folders and Files:");

            if (state.Paths.Count == 0)
            {
                EditorGUILayout.HelpBox("No paths selected. Add a folder or a .cs file.", MessageType.Info);
                return;
            }

            for (int i = 0; i < state.Paths.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(state.Paths[i]);

                if (GUILayout.Button("Remove", GUILayout.MaxWidth(70)))
                {
                    _controller.RemovePathAt(i);
                }

                EditorGUILayout.EndHorizontal();
            }
        }
    }
}
