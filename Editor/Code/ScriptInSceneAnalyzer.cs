using UnityEngine.SceneManagement;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.Linq;
using System.Collections.Generic;

namespace LegendaryTools.Editor
{
    public class ScriptInSceneAnalyzer : EditorWindow
    {
        private Vector2 scrollPosition;
        private List<(GameObject go, MonoBehaviour[] customScripts)> gameObjectsWithScripts = new();
        private int currentPage = 0;
        private int itemsPerPage = 10;
        private bool includeTextMeshPro = true;
        private bool includeDOTween = true;
        private bool includeCinemachine = true;
        private bool includeNavMesh = true;
        private bool includeParticleSystem = true;
        private string ignoredNamespaces = "";
        private string ignoredClassNames = "";
        private List<GameObject> ignoredGameObjects = new();

        [MenuItem("Tools/LegendaryTools/Code/Script In Scene Analyzer")]
        public static void ShowWindow()
        {
            GetWindow<ScriptInSceneAnalyzer>("Script In Scene Analyzer");
        }

        private void OnEnable()
        {
            // Refresh the list when the window is enabled
            RefreshGameObjectList();
        }

        private void OnGUI()
        {
            GUILayout.Label("Scene Analyzer", EditorStyles.boldLabel);

            // Configuration section
            EditorGUILayout.LabelField("Script Inclusion Settings", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            includeTextMeshPro = EditorGUILayout.Toggle("Include TextMeshPro Scripts", includeTextMeshPro);
            includeDOTween = EditorGUILayout.Toggle("Include DOTween Scripts", includeDOTween);
            includeCinemachine = EditorGUILayout.Toggle("Include Cinemachine Scripts", includeCinemachine);
            includeNavMesh = EditorGUILayout.Toggle("Include NavMesh Scripts", includeNavMesh);
            includeParticleSystem = EditorGUILayout.Toggle("Include ParticleSystem Scripts", includeParticleSystem);
            EditorGUILayout.LabelField("Ignored Namespaces (comma-separated)", EditorStyles.label);
            ignoredNamespaces = EditorGUILayout.TextField(ignoredNamespaces);
            EditorGUILayout.LabelField("Ignored Class Names (comma-separated)", EditorStyles.label);
            ignoredClassNames = EditorGUILayout.TextField(ignoredClassNames);

            // Ignored GameObjects field
            EditorGUILayout.LabelField("Ignored GameObjects", EditorStyles.label);
            EditorGUILayout.BeginVertical(GUI.skin.box);
            for (int i = 0; i < ignoredGameObjects.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                ignoredGameObjects[i] =
                    (GameObject)EditorGUILayout.ObjectField(ignoredGameObjects[i], typeof(GameObject), true);
                if (GUILayout.Button("Remove", GUILayout.Width(60)))
                {
                    ignoredGameObjects.RemoveAt(i);
                    i--;
                }

                EditorGUILayout.EndHorizontal();
            }

            if (GUILayout.Button("Add GameObject")) ignoredGameObjects.Add(null);
            EditorGUILayout.EndVertical();

            if (EditorGUI.EndChangeCheck()) RefreshGameObjectList(); // Refresh list when settings change

            // Refresh button
            if (GUILayout.Button("Refresh")) RefreshGameObjectList();

            if (gameObjectsWithScripts.Count == 0)
            {
                EditorGUILayout.HelpBox("No GameObjects with custom MonoBehaviours found.", MessageType.Info);
                return;
            }

            // Pagination controls
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(
                $"Page {currentPage + 1} of {Mathf.CeilToInt((float)gameObjectsWithScripts.Count / itemsPerPage)}");
            GUI.enabled = currentPage > 0;
            if (GUILayout.Button("Previous")) currentPage--;
            GUI.enabled = currentPage < (gameObjectsWithScripts.Count - 1) / itemsPerPage;
            if (GUILayout.Button("Next")) currentPage++;
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();

            // Display items for the current page
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.ExpandHeight(true));

            int startIndex = currentPage * itemsPerPage;
            int endIndex = Mathf.Min(startIndex + itemsPerPage, gameObjectsWithScripts.Count);

            for (int i = startIndex; i < endIndex; i++)
            {
                (GameObject go, MonoBehaviour[] scripts) = gameObjectsWithScripts[i];
                EditorGUILayout.ObjectField(go, typeof(GameObject), true);

                EditorGUI.indentLevel++;
                foreach (MonoBehaviour script in scripts)
                {
                    if (script != null)
                        EditorGUILayout.ObjectField(script.GetType().Name, script, typeof(MonoBehaviour), true);
                }

                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndScrollView();
        }

        private void RefreshGameObjectList()
        {
            gameObjectsWithScripts.Clear();
            currentPage = 0;

            // Iterate through all open scenes
            for (int i = 0; i < EditorSceneManager.sceneCount; i++)
            {
                Scene scene = EditorSceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;

                // Get all root GameObjects in the scene
                GameObject[] rootGameObjects = scene.GetRootGameObjects();
                foreach (GameObject go in rootGameObjects)
                {
                    CollectGameObjectsWithCustomScripts(go);
                }
            }
        }

        private void CollectGameObjectsWithCustomScripts(GameObject go)
        {
            if (go == null) return;

            // Skip if GameObject or any of its parents is in ignoredGameObjects
            if (ignoredGameObjects.Any(ignored =>
                    ignored != null &&
                    (go == ignored || go.transform.IsChildOf(ignored.transform))))
                return;

            MonoBehaviour[] components = go.GetComponents<MonoBehaviour>();
            string[] ignoredNamespaceList = string.IsNullOrWhiteSpace(ignoredNamespaces)
                ? new string[0]
                : ignoredNamespaces.Split(',').Select(ns => ns.Trim()).Where(ns => !string.IsNullOrEmpty(ns)).ToArray();
            string[] ignoredClassNameList = string.IsNullOrWhiteSpace(ignoredClassNames)
                ? new string[0]
                : ignoredClassNames.Split(',').Select(cn => cn.Trim()).Where(cn => !string.IsNullOrEmpty(cn)).ToArray();

            MonoBehaviour[] customScripts = components
                .Where(comp =>
                    comp != null &&
                    comp.GetType() != null &&
                    !ignoredClassNameList.Contains(comp.GetType().Name) &&
                    (
                        comp.GetType().Namespace == null || // Include scripts with no namespace
                        (
                            !comp.GetType().Namespace.StartsWith("UnityEngine") &&
                            !comp.GetType().Namespace.StartsWith("UnityEditor") &&
                            (includeTextMeshPro || !comp.GetType().Namespace.StartsWith("TMPro")) &&
                            (includeDOTween || !comp.GetType().Namespace.StartsWith("DG.Tweening")) &&
                            (includeCinemachine || !comp.GetType().Namespace.StartsWith("Cinemachine")) &&
                            (includeNavMesh || !comp.GetType().Namespace.StartsWith("UnityEngine.AI")) &&
                            (includeParticleSystem ||
                             !comp.GetType().Namespace.StartsWith("UnityEngine.ParticleSystemModules")) &&
                            !ignoredNamespaceList.Any(ns => comp.GetType().Namespace.StartsWith(ns))
                        )
                    ))
                .ToArray();

            if (customScripts.Length > 0) gameObjectsWithScripts.Add((go, customScripts));

            // Check children recursively
            foreach (Transform child in go.transform)
            {
                if (child != null) CollectGameObjectsWithCustomScripts(child.gameObject);
            }
        }
    }
}