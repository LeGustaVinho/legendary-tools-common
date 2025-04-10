using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace LegendaryTools.Editor
{
    [InitializeOnLoad]
    public static class PlayModeStarterFromScene0
    {
        // Menu item path and keys to save preferences
        private const string MenuItemPath = "Tools/Start From Scene 0";
        private const string EditorPrefKey = "PlayModeStartFromScene0";
        private const string PreviousSceneKey = "PreviousScenePath";

        static PlayModeStarterFromScene0()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        /// <summary>
        /// Toggles the flag to start Play Mode from scene 0.
        /// </summary>
        [MenuItem(MenuItemPath)]
        public static void ToggleStartFromScene0()
        {
            bool flag = EditorPrefs.GetBool(EditorPrefKey, false);
            flag = !flag;
            EditorPrefs.SetBool(EditorPrefKey, flag);
            Menu.SetChecked(MenuItemPath, flag);
        }

        /// <summary>
        /// Validates and updates the checkmark state in the menu item.
        /// </summary>
        [MenuItem(MenuItemPath, true)]
        public static bool ToggleStartFromScene0Validate()
        {
            bool flag = EditorPrefs.GetBool(EditorPrefKey, false);
            Menu.SetChecked(MenuItemPath, flag);
            return true;
        }

        /// <summary>
        /// Callback to handle Play Mode state changes.
        /// </summary>
        /// <param name="state">The new play mode state.</param>
        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            bool flag = EditorPrefs.GetBool(EditorPrefKey, false);

            if (state == PlayModeStateChange.ExitingEditMode)
            {
                // When entering Play Mode, if the flag is enabled
                if (flag)
                {
                    Scene currentScene = EditorSceneManager.GetActiveScene();
                    // If the current scene is not the one at index 0, store its path to restore later
                    if (currentScene.buildIndex != 0 && EditorBuildSettings.scenes.Length > 0)
                    {
                        EditorPrefs.SetString(PreviousSceneKey, currentScene.path);
                        string scenePath = EditorBuildSettings.scenes[0].path;
                        if (!string.IsNullOrEmpty(scenePath))
                        {
                            EditorSceneManager.OpenScene(scenePath);
                        }
                    }
                }
            }
            else if (state == PlayModeStateChange.EnteredEditMode)
            {
                // When returning to Edit Mode, restore the previous scene if it was stored
                if (flag && EditorPrefs.HasKey(PreviousSceneKey))
                {
                    string previousScene = EditorPrefs.GetString(PreviousSceneKey);
                    if (!string.IsNullOrEmpty(previousScene))
                    {
                        EditorSceneManager.OpenScene(previousScene);
                    }

                    EditorPrefs.DeleteKey(PreviousSceneKey);
                }
            }
        }
    }
}