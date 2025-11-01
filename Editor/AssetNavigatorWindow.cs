using UnityEngine;
using UnityEditor;
using UnityEngine.Video;
using System.Collections.Generic;
using System.Linq;

namespace LegendaryTools.Editor
{
    public class AssetNavigatorWindow : EditorWindow
    {
        // Lists to store selection history (index 0 is the most recent user selection)
        private List<Object> assetHistory = new(); // For non-GameObject assets
        private List<GameObject> gameObjectHistory = new(); // For GameObjects
        private List<Object> assetPalette = new(); // For persistent Asset Palette

        // Variable to track the last selected object
        private Object lastSelected = null;

        // Flag to ignore the next selection change triggered by navigation
        private bool ignoreNextSelectionChange = false;

        // Pointers to track the current position in each history (0 = latest)
        private int currentAssetHistoryIndex = 0;
        private int currentGameObjectHistoryIndex = 0;

        private const int HISTORY_SIZE = 20;
        private const string PALETTE_PREFS_KEY = "LegendaryTools_AssetPalette_GUIDs";

        // Scroll position for the whole window
        private Vector2 scrollPos; // Maintains a single scroll view for the whole GUI

        // Create a menu item to open the window (Window > Asset History)
        [MenuItem("Tools/LegendaryTools/Asset Navigator Window")]
        public static void ShowWindow()
        {
            AssetNavigatorWindow window = GetWindow<AssetNavigatorWindow>("Asset History");
            window.Show();
        }

        private void OnEnable()
        {
            // Initialize histories if there is an active selection
            if (Selection.activeObject != null)
            {
                lastSelected = Selection.activeObject;
                if (lastSelected is GameObject go)
                {
                    gameObjectHistory.Insert(0, go);
                    currentGameObjectHistoryIndex = 0;
                }
                else
                {
                    assetHistory.Insert(0, lastSelected);
                    currentAssetHistoryIndex = 0;
                }
            }

            // Load Asset Palette from EditorPrefs
            LoadAssetPalette();
        }

        private void OnDisable()
        {
            // Save Asset Palette to EditorPrefs
            SaveAssetPalette();
        }

        private void Update()
        {
            // If no change in selection, do nothing
            if (Selection.activeObject == lastSelected)
                return;

            // If this change was triggered by our navigation, ignore adding it to history
            if (ignoreNextSelectionChange)
            {
                ignoreNextSelectionChange = false;
                lastSelected = Selection.activeObject;
                return;
            }

            // A manual selection change has occurred, so add it to the appropriate history
            lastSelected = Selection.activeObject;
            if (lastSelected != null)
            {
                if (lastSelected is GameObject go)
                {
                    // Avoid duplicate consecutive GameObject entries
                    if (gameObjectHistory.Count == 0 || gameObjectHistory[0] != go)
                    {
                        gameObjectHistory.Insert(0, go);
                        currentGameObjectHistoryIndex = 0; // Reset navigation pointer
                        // Limit the history to HISTORY_SIZE
                        if (gameObjectHistory.Count > HISTORY_SIZE)
                            gameObjectHistory.RemoveAt(gameObjectHistory.Count - 1);
                    }
                    else
                    {
                        currentGameObjectHistoryIndex = 0;
                    }
                }
                else
                {
                    // Avoid duplicate consecutive asset entries
                    if (assetHistory.Count == 0 || assetHistory[0] != lastSelected)
                    {
                        assetHistory.Insert(0, lastSelected);
                        currentAssetHistoryIndex = 0; // Reset navigation pointer
                        // Limit the history to HISTORY_SIZE
                        if (assetHistory.Count > HISTORY_SIZE)
                            assetHistory.RemoveAt(assetHistory.Count - 1);
                    }
                    else
                    {
                        currentAssetHistoryIndex = 0;
                    }
                }
            }

            Repaint();
        }

        // Load Asset Palette from EditorPrefs
        private void LoadAssetPalette()
        {
            assetPalette.Clear();
            string guidString = EditorPrefs.GetString(PALETTE_PREFS_KEY, "");
            if (!string.IsNullOrEmpty(guidString))
            {
                string[] guids = guidString.Split(';');
                foreach (string guid in guids)
                {
                    if (!string.IsNullOrEmpty(guid))
                    {
                        string path = AssetDatabase.GUIDToAssetPath(guid);
                        Object asset = AssetDatabase.LoadAssetAtPath<Object>(path);
                        if (asset != null && (!(asset is GameObject) || PrefabUtility.IsPartOfPrefabAsset(asset)))
                            assetPalette.Add(asset);
                    }
                }
            }
        }

        // Save Asset Palette to EditorPrefs
        private void SaveAssetPalette()
        {
            string[] guids = assetPalette
                .Where(asset => asset != null)
                .Select(asset => AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(asset)))
                .Distinct()
                .ToArray();
            EditorPrefs.SetString(PALETTE_PREFS_KEY, string.Join(";", guids));
        }

        // Returns a color associated with the asset type
        private Color GetColorForAsset(Object asset)
        {
            if (asset is Sprite)
                return new Color(1f, 0.75f, 0.8f); // Light pink for Sprite
            else if (asset is Texture || asset is Texture2D)
                return Color.cyan;
            else if (asset is Font)
                return Color.blue;
            else if (asset is ScriptableObject)
                return Color.green;
            else if (asset is MonoScript)
                return Color.yellow;
            else if (asset is AudioClip)
                return Color.magenta;
            else if (asset.GetType().Name == "AudioMixer")
                return new Color(0.3f, 0.7f, 0.9f); // Custom color for AudioMixer
            else if (asset is Material)
                return Color.red;
            else if (asset is Shader)
                return new Color(1f, 0.65f, 0f); // Orange
            else if (asset is AnimationClip)
                return new Color(0.5f, 0f, 0.5f); // Purple
            else if (asset is SceneAsset)
                return Color.grey;
            else if (asset is GameObject && PrefabUtility.IsPartOfPrefabAsset(asset))
                return new Color(0.5f, 0.8f, 1f); // Light blue for Prefab
            else if (asset is GameObject)
                return new Color(0.8f, 0.8f, 0f); // Yellowish for GameObject
            else if (asset is TextAsset)
                return new Color(0.9f, 0.9f, 0.9f); // Light grey for TextAsset
            else if (asset is VideoClip)
                return new Color(0.7f, 0.4f, 0.1f); // Brownish for VideoClip
            return Color.white;
        }

        // Returns a tag (label) corresponding to the asset type
        private string GetTagForAsset(Object asset)
        {
            if (asset is Sprite)
                return "Sprite";
            else if (asset is Texture || asset is Texture2D)
                return "Texture";
            else if (asset is Font)
                return "Font";
            else if (asset is ScriptableObject)
                return "ScriptableObject";
            else if (asset is MonoScript)
                return "Script";
            else if (asset is AudioClip)
                return "Audio";
            else if (asset.GetType().Name == "AudioMixer")
                return "AudioMixer";
            else if (asset is Material)
                return "Material";
            else if (asset is Shader)
                return "Shader";
            else if (asset is AnimationClip)
                return "AnimationClip";
            else if (asset is SceneAsset)
                return "Scene";
            else if (asset is GameObject && PrefabUtility.IsPartOfPrefabAsset(asset))
                return "Prefab";
            else if (asset is GameObject)
                return "GameObject";
            else if (asset is TextAsset)
                return "Text";
            else if (asset is VideoClip)
                return "VideoClip";
            return "Asset";
        }

        private void OnGUI()
        {
            // Begin a single scroll view wrapping the entire GUI content.
            // This ensures the whole window scrolls as one unit regardless of section heights.
            scrollPos = EditorGUILayout.BeginScrollView(scrollPos);

            GUILayout.Space(10);

            // Asset History Section
            GUILayout.Label("Asset History", EditorStyles.boldLabel);
            GUILayout.BeginHorizontal();
            // Back button for Asset history
            if (GUILayout.Button("Back (Assets)"))
                if (currentAssetHistoryIndex < assetHistory.Count - 1)
                {
                    ignoreNextSelectionChange = true;
                    currentAssetHistoryIndex++;
                    Selection.activeObject = assetHistory[currentAssetHistoryIndex];
                }

            // Clear Asset History button
            if (GUILayout.Button("Clear Asset History"))
            {
                if (Selection.activeObject != null && !(Selection.activeObject is GameObject))
                {
                    assetHistory.Clear();
                    assetHistory.Add(Selection.activeObject);
                    currentAssetHistoryIndex = 0;
                    lastSelected = Selection.activeObject;
                }
                else
                {
                    assetHistory.Clear();
                    currentAssetHistoryIndex = 0;
                    if (Selection.activeObject == null)
                        lastSelected = null;
                }
            }

            GUILayout.EndHorizontal();

            GUILayout.Space(5);
            GUILayout.Label($"Last {HISTORY_SIZE} selected assets:", EditorStyles.boldLabel);

            // Display buttons for each asset in the asset history
            for (int i = 0; i < assetHistory.Count; i++)
            {
                Object asset = assetHistory[i];
                if (asset == null)
                    continue;

                GUI.backgroundColor = GetColorForAsset(asset);
                if (GUILayout.Button($"[{GetTagForAsset(asset)}] {asset.name}"))
                {
                    ignoreNextSelectionChange = true;
                    Selection.activeObject = asset;
                    currentAssetHistoryIndex = i;
                }

                GUI.backgroundColor = Color.white;
            }

            GUILayout.Space(20);

            // GameObject History Section
            GUILayout.Label("GameObject History", EditorStyles.boldLabel);
            GUILayout.BeginHorizontal();
            // Back button for GameObject history
            if (GUILayout.Button("Back (GameObjects)"))
                if (currentGameObjectHistoryIndex < gameObjectHistory.Count - 1)
                {
                    ignoreNextSelectionChange = true;
                    currentGameObjectHistoryIndex++;
                    Selection.activeObject = gameObjectHistory[currentGameObjectHistoryIndex];
                }

            // Clear GameObject History button
            if (GUILayout.Button("Clear GameObject History"))
            {
                if (Selection.activeObject != null && Selection.activeObject is GameObject go)
                {
                    gameObjectHistory.Clear();
                    gameObjectHistory.Add(go);
                    currentGameObjectHistoryIndex = 0;
                    lastSelected = Selection.activeObject;
                }
                else
                {
                    gameObjectHistory.Clear();
                    currentGameObjectHistoryIndex = 0;
                    if (Selection.activeObject == null)
                        lastSelected = null;
                }
            }

            GUILayout.EndHorizontal();

            GUILayout.Space(5);
            GUILayout.Label($"Last {HISTORY_SIZE} selected GameObjects:", EditorStyles.boldLabel);

            // Display buttons for each GameObject in the GameObject history
            for (int i = 0; i < gameObjectHistory.Count; i++)
            {
                GameObject go = gameObjectHistory[i];
                if (go == null)
                    continue;

                GUI.backgroundColor = GetColorForAsset(go);
                if (GUILayout.Button($"[{GetTagForAsset(go)}] {go.name}"))
                {
                    ignoreNextSelectionChange = true;
                    Selection.activeObject = go;
                    currentGameObjectHistoryIndex = i;
                }

                GUI.backgroundColor = Color.white;
            }

            GUILayout.Space(20);

            // Asset Palette Section
            GUILayout.Label("Asset Palette", EditorStyles.boldLabel);
            GUILayout.BeginHorizontal();
            // Clear Asset Palette button
            if (GUILayout.Button("Clear Asset Palette"))
            {
                assetPalette.Clear();
                SaveAssetPalette();
            }

            GUILayout.EndHorizontal();

            GUILayout.Space(5);
            GUILayout.Label("Drag and drop assets here or click to select:", EditorStyles.boldLabel);

            // Drag and Drop Area
            Rect dropArea = GUILayoutUtility.GetRect(0f, 50f, GUILayout.ExpandWidth(true));
            GUI.Box(dropArea, "Drop Assets Here");
            Event evt = Event.current;
            if (dropArea.Contains(evt.mousePosition))
            {
                if (evt.type == EventType.DragUpdated)
                {
                    DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                    Event.current.Use();
                }
                else if (evt.type == EventType.DragPerform)
                {
                    DragAndDrop.AcceptDrag();
                    foreach (Object draggedObject in DragAndDrop.objectReferences)
                    {
                        if (draggedObject != null && (!(draggedObject is GameObject) ||
                                                      PrefabUtility.IsPartOfPrefabAsset(draggedObject)))
                            if (!assetPalette.Contains(draggedObject))
                            {
                                assetPalette.Add(draggedObject);
                                SaveAssetPalette();
                            }
                    }

                    Event.current.Use();
                }
            }

            // Display buttons for each asset in the Asset Palette
            for (int i = 0; i < assetPalette.Count; i++)
            {
                Object asset = assetPalette[i];
                if (asset == null)
                    continue;

                GUILayout.BeginHorizontal();
                GUI.backgroundColor = GetColorForAsset(asset);
                if (GUILayout.Button($"[{GetTagForAsset(asset)}] {asset.name}"))
                {
                    ignoreNextSelectionChange = true;
                    Selection.activeObject = asset;
                    EditorGUIUtility.PingObject(asset); // Highlight in Project window
                }

                GUI.backgroundColor = Color.white;

                // Remove button for individual asset
                if (GUILayout.Button("Remove", GUILayout.Width(60)))
                {
                    assetPalette.RemoveAt(i);
                    SaveAssetPalette();
                    break; // Exit loop to avoid modifying collection during iteration
                }

                GUILayout.EndHorizontal();
            }

            // End of the single scroll view wrapping the entire GUI
            EditorGUILayout.EndScrollView();
        }
    }
}