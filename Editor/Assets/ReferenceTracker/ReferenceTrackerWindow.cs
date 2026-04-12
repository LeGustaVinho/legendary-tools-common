using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace LegendaryTools.Editor
{
    /// <summary>
    /// Renders the Reference Tracker editor window.
    /// </summary>
    public sealed class ReferenceTrackerWindow : EditorWindow
    {
        private const string WindowTitle = "Reference Tracker";
        private const string MainMenuPath = "Tools/LegendaryTools/Assets/Reference Tracker";
        private const string GameObjectMenuPath = "GameObject/Reference Tracker/Find References In Current Scope";
        private const string TransformContextPath = "CONTEXT/Transform/Reference Tracker/Find References In Current Scope";
        private const string ComponentContextPath = "CONTEXT/Component/Reference Tracker/Find References In Current Scope";

        private static readonly ReferenceTrackerScopeResolver ScopeResolver = new ReferenceTrackerScopeResolver();
        private static readonly ReferenceTrackerSearchService SearchService = new ReferenceTrackerSearchService(ScopeResolver);
        private static readonly ReferenceTrackerGroupingService GroupingService = new ReferenceTrackerGroupingService();
        private static readonly ReferenceTrackerSelectionService SelectionService = new ReferenceTrackerSelectionService();
        private static readonly ReferenceTrackerWindowController Controller =
            new ReferenceTrackerWindowController(ScopeResolver, SearchService, GroupingService, SelectionService);

        [SerializeField] private ReferenceTrackerWindowState _state = new ReferenceTrackerWindowState();

        private readonly Dictionary<string, bool> _groupStates = new Dictionary<string, bool>(System.StringComparer.Ordinal);

        private Vector2 _scroll;

        [MenuItem(MainMenuPath)]
        private static void OpenWindow()
        {
            ReferenceTrackerWindow window = GetWindow<ReferenceTrackerWindow>(WindowTitle);
            window.minSize = new Vector2(780f, 360f);
            window.Show();
        }

        [MenuItem(GameObjectMenuPath, false, 49)]
        private static void FindFromGameObjectMenu(MenuCommand command)
        {
            GameObject gameObject = command.context as GameObject;
            if (gameObject == null)
            {
                gameObject = Selection.activeGameObject;
            }

            if (gameObject != null)
            {
                OpenWithTargetAndSearch(gameObject);
            }
        }

        [MenuItem(GameObjectMenuPath, true)]
        private static bool ValidateFindFromGameObjectMenu()
        {
            return Selection.activeGameObject != null;
        }

        [MenuItem(TransformContextPath)]
        private static void FindFromTransformContext(MenuCommand command)
        {
            Transform transform = command.context as Transform;
            if (transform != null)
            {
                OpenWithTargetAndSearch(transform.gameObject);
            }
        }

        [MenuItem(ComponentContextPath)]
        private static void FindFromComponentContext(MenuCommand command)
        {
            Component component = command.context as Component;
            if (component != null)
            {
                OpenWithTargetAndSearch(component);
            }
        }

        private static void OpenWithTargetAndSearch(UnityEngine.Object target)
        {
            ReferenceTrackerWindow window = GetWindow<ReferenceTrackerWindow>(WindowTitle);
            window.minSize = new Vector2(780f, 360f);
            window.EnsureState();
            window._state.Target = target;
            window._state.SearchScopes = ScopeResolver.GetCurrentScope();
            window._groupStates.Clear();
            Controller.RunSearch(window._state);
            window.Show();
            window.Focus();
        }

        private void OnGUI()
        {
            EnsureState();
            Controller.NormalizeScopes(_state);

            DrawToolbar();
            DrawStatus();
            DrawResults();
        }

        private void EnsureState()
        {
            if (_state == null)
            {
                _state = new ReferenceTrackerWindowState();
            }
        }

        private void DrawToolbar()
        {
            EditorGUILayout.Space(6f);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(WindowTitle, EditorStyles.boldLabel);
                EditorGUILayout.Space(4f);

                EditorGUI.BeginChangeCheck();
                UnityEngine.Object newTarget = EditorGUILayout.ObjectField("Target", _state.Target, typeof(UnityEngine.Object), true);
                if (EditorGUI.EndChangeCheck())
                {
                    _state.Target = newTarget;
                }

                EditorGUI.BeginChangeCheck();
                ReferenceTrackerSearchScope newSearchScopes = DrawSearchScopes(_state.SearchScopes);
                if (EditorGUI.EndChangeCheck())
                {
                    _state.SearchScopes = ScopeResolver.Normalize(newSearchScopes);
                }

                EditorGUI.BeginChangeCheck();
                ReferenceTrackerGroupMode newGroupMode =
                    (ReferenceTrackerGroupMode)EditorGUILayout.EnumPopup("Group By", _state.GroupMode);
                if (EditorGUI.EndChangeCheck())
                {
                    _groupStates.Clear();
                    Controller.SetGroupMode(_state, newGroupMode);
                }

                EditorGUILayout.Space(4f);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Use Selection", GUILayout.Height(24f)))
                    {
                        Controller.UseSelection(_state);
                    }

                    using (new EditorGUI.DisabledScope(!ReferenceTrackerSearchService.IsSupportedTarget(_state.Target)))
                    {
                        if (GUILayout.Button("Search", GUILayout.Height(24f)))
                        {
                            _groupStates.Clear();
                            Controller.RunSearch(_state);
                        }
                    }

                    if (GUILayout.Button("Clear", GUILayout.Height(24f)))
                    {
                        _groupStates.Clear();
                        Controller.ClearResults(_state);
                    }
                }

                EditorGUILayout.Space(6f);

                EditorGUILayout.HelpBox(ScopeResolver.GetDescription(_state.SearchScopes), MessageType.Info);

                if (_state.Target != null && !ReferenceTrackerSearchService.IsSupportedTarget(_state.Target))
                {
                    EditorGUILayout.HelpBox(
                        "This tool supports GameObject and Component targets only.",
                        MessageType.Warning);
                }
            }
        }

        private ReferenceTrackerSearchScope DrawSearchScopes(ReferenceTrackerSearchScope scopes)
        {
            bool currentScene = (scopes & ReferenceTrackerSearchScope.CurrentScene) != 0;
            bool prefabMode = (scopes & ReferenceTrackerSearchScope.PrefabMode) != 0;
            bool prefabModeAvailable = ScopeResolver.IsPrefabModeAvailable;

            EditorGUILayout.LabelField("Scopes");
            EditorGUI.indentLevel++;
            currentScene = EditorGUILayout.ToggleLeft("Current Scene", currentScene);

            using (new EditorGUI.DisabledScope(!prefabModeAvailable))
            {
                prefabMode = EditorGUILayout.ToggleLeft("Prefab Mode", prefabMode && prefabModeAvailable);
            }

            EditorGUI.indentLevel--;

            ReferenceTrackerSearchScope newScopes = ReferenceTrackerSearchScope.None;

            if (currentScene)
            {
                newScopes |= ReferenceTrackerSearchScope.CurrentScene;
            }

            if (prefabMode && prefabModeAvailable)
            {
                newScopes |= ReferenceTrackerSearchScope.PrefabMode;
            }

            return newScopes;
        }

        private void DrawStatus()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(_state.Status, EditorStyles.wordWrappedLabel);

                if (_state.Results.Count > 0)
                {
                    GUILayout.FlexibleSpace();
                    EditorGUILayout.LabelField(
                        string.Format("{0} result(s) - {1:F1} ms", _state.Results.Count, _state.LastSearchDurationMs),
                        EditorStyles.miniLabel,
                        GUILayout.Width(150f));
                }
            }

            EditorGUILayout.Space(4f);
        }

        private void DrawResults()
        {
            if (_state.Groups.Count == 0)
            {
                return;
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            for (int i = 0; i < _state.Groups.Count; i++)
            {
                ReferenceTrackerGroupBucket bucket = _state.Groups[i];
                bool isExpanded = GetGroupState(bucket.Key);
                isExpanded = EditorGUILayout.BeginFoldoutHeaderGroup(isExpanded, string.Format("{0} ({1})", bucket.Key, bucket.Items.Count));
                _groupStates[bucket.Key] = isExpanded;

                if (isExpanded)
                {
                    EditorGUILayout.Space(2f);

                    for (int j = 0; j < bucket.Items.Count; j++)
                    {
                        DrawResult(bucket.Items[j]);
                    }

                    EditorGUILayout.Space(6f);
                }

                EditorGUILayout.EndFoldoutHeaderGroup();
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawResult(ReferenceTrackerUsageResult result)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(result.HostGameObjectPath, EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Component", result.HostComponentLabel);
                EditorGUILayout.LabelField("Property", string.Format("{0} ({1})", result.PropertyDisplayName, result.PropertyPath));
                EditorGUILayout.LabelField("Reference Type", result.ReferenceTypeLabel);

                EditorGUILayout.ObjectField("Referenced", result.ReferencedObject, typeof(UnityEngine.Object), true);

                EditorGUILayout.Space(3f);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Ping", GUILayout.Width(70f)))
                    {
                        ReferenceTrackerEditorActions.Ping(result);
                    }

                    if (GUILayout.Button("Select", GUILayout.Width(70f)))
                    {
                        ReferenceTrackerEditorActions.Select(result);
                    }

                    using (new EditorGUI.DisabledScope(!ReferenceTrackerEditorActions.CanOpenOwningPrefab(result.HostGameObject)))
                    {
                        if (GUILayout.Button("Open Prefab", GUILayout.Width(100f)))
                        {
                            ReferenceTrackerEditorActions.OpenOwningPrefab(result.HostGameObject);
                        }
                    }

                    if (GUILayout.Button("Copy Path", GUILayout.Width(90f)))
                    {
                        EditorGUIUtility.systemCopyBuffer = result.HostGameObjectPath;
                    }

                    GUILayout.FlexibleSpace();
                }
            }
        }

        private bool GetGroupState(string key)
        {
            bool state;
            if (_groupStates.TryGetValue(key, out state))
            {
                return state;
            }

            _groupStates[key] = true;
            return true;
        }
    }
}
