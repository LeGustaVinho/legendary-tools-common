using UnityEngine;
using UnityEditor;
using UnityEngine.Video;
using System.Collections.Generic;

namespace LegendaryTools.Editor
{
    public class AssetHistoryWindow : EditorWindow
    {
        // List to store selection history (index 0 is the most recent user selection)
        private List<Object> selectionHistory = new List<Object>();

        // Variable to track the last selected asset
        private Object lastSelected = null;

        // Flag to ignore the next selection change triggered by a navigation command (Back or history selection)
        private bool ignoreNextSelectionChange = false;

        // Pointer to track the current position in the history for navigation (0 = latest)
        private int currentHistoryIndex = 0;

        private const int HISTORY_SIZE = 20;

        // Create a menu item to open the window (Window > Asset History)
        [MenuItem("Tools/LegendaryTools/Asset History")]
        public static void ShowWindow()
        {
            AssetHistoryWindow window = GetWindow<AssetHistoryWindow>("Asset History");
            window.Show();
        }

        private void OnEnable()
        {
            // Initialize history if there is an active selection
            if (Selection.activeObject != null)
            {
                lastSelected = Selection.activeObject;
                selectionHistory.Insert(0, lastSelected);
                currentHistoryIndex = 0;
            }
        }

        private void Update()
        {
            // If no change in selection, do nothing.
            if (Selection.activeObject == lastSelected)
                return;

            // If this change was triggered by our navigation, ignore adding it to history.
            if (ignoreNextSelectionChange)
            {
                ignoreNextSelectionChange = false;
                lastSelected = Selection.activeObject;
                return;
            }

            // A manual selection change has occurred, so add it to the history.
            lastSelected = Selection.activeObject;
            if (lastSelected != null)
            {
                // Avoid duplicate consecutive entries.
                if (selectionHistory.Count == 0 || selectionHistory[0] != lastSelected)
                {
                    selectionHistory.Insert(0, lastSelected);
                    currentHistoryIndex = 0; // Reset navigation pointer for a new manual selection.
                    // Limit the history to the HISTORY_SIZE most recent assets.
                    if (selectionHistory.Count > HISTORY_SIZE)
                        selectionHistory.RemoveAt(selectionHistory.Count - 1);
                }
                else
                {
                    // Reset pointer if the current selection is the same as the latest entry.
                    currentHistoryIndex = 0;
                }
            }

            Repaint();
        }

        // Returns a color associated with the asset type.
        private Color GetColorForAsset(Object asset)
        {
            if (asset is Sprite)
                return new Color(1f, 0.75f, 0.8f); // Light pink for Sprite.
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
                return new Color(0.3f, 0.7f, 0.9f); // Custom color for AudioMixer.
            else if (asset is Material)
                return Color.red;
            else if (asset is Shader)
                return new Color(1f, 0.65f, 0f); // Orange.
            else if (asset is AnimationClip)
                return new Color(0.5f, 0f, 0.5f); // Purple.
            else if (asset is SceneAsset)
                return Color.grey;
            else if (asset is GameObject && PrefabUtility.IsPartOfPrefabAsset(asset))
                return new Color(0.5f, 0.8f, 1f); // Light blue for Prefab.
            else if (asset is TextAsset)
                return new Color(0.9f, 0.9f, 0.9f); // Light grey for TextAsset.
            else if (asset is VideoClip)
                return new Color(0.7f, 0.4f, 0.1f); // Brownish for VideoClip.
            return Color.white;
        }

        // Returns a tag (label) corresponding to the asset type.
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
            else if (asset is TextAsset)
                return "Text";
            else if (asset is VideoClip)
                return "VideoClip";
            return "Asset";
        }

        private void OnGUI()
        {
            GUILayout.Space(10);

            GUILayout.BeginHorizontal();
            // Back button: Goes back one step in the history sequence.
            if (GUILayout.Button("Back"))
            {
                // Only navigate if there is a next (older) asset in the history.
                if (currentHistoryIndex < selectionHistory.Count - 1)
                {
                    ignoreNextSelectionChange = true;
                    currentHistoryIndex++;
                    Selection.activeObject = selectionHistory[currentHistoryIndex];
                }
            }

            // Clear History button: Resets the history, preserving only the current selection.
            if (GUILayout.Button("Clear History"))
            {
                if (Selection.activeObject != null)
                {
                    selectionHistory.Clear();
                    selectionHistory.Add(Selection.activeObject);
                    currentHistoryIndex = 0;
                    lastSelected = Selection.activeObject;
                }
                else
                {
                    selectionHistory.Clear();
                    currentHistoryIndex = 0;
                    lastSelected = null;
                }
            }

            GUILayout.EndHorizontal();

            GUILayout.Space(10);
            GUILayout.Label($"Last {HISTORY_SIZE} selected assets:", EditorStyles.boldLabel);

            // Display buttons for each asset in the history.
            for (int i = 0; i < selectionHistory.Count; i++)
            {
                Object asset = selectionHistory[i];
                if (asset == null)
                    continue;

                GUI.backgroundColor = GetColorForAsset(asset);
                // Show the asset tag and name. When clicked, navigate to that asset in the history.
                if (GUILayout.Button($"[{GetTagForAsset(asset)}] {asset.name}"))
                {
                    ignoreNextSelectionChange = true;
                    Selection.activeObject = asset;
                    currentHistoryIndex = i; // Update the history pointer to the selected asset's position.
                }

                GUI.backgroundColor = Color.white;
            }
        }
    }
}