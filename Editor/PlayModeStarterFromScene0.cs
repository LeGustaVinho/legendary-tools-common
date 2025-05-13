using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LegendaryTools.Editor
{
    [InitializeOnLoad]
    public static class PlayModeStarterFromScene0
    {
        // Menu item path and keys to save preferences
        private const string MENU_ITEM_PATH = "Tools/LegendaryTools/Start From Scene 0";
        private const string EDITOR_PREF_KEY = "PlayModeStartFromScene0";
        private const string PREVIOUS_SCENES_KEY = "PreviousScenesPaths";
        private const string ACTIVE_SCENE_KEY = "ActiveScenePath";

        // Structure to store scene path and loaded state
        [System.Serializable]
        private class SceneState
        {
            public string path;
            public bool isLoaded;
        }

        // Structure to store all scene states
        [System.Serializable]
        private class SceneStateList
        {
            public List<SceneState> scenes = new();
        }

        static PlayModeStarterFromScene0()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        /// <summary>
        /// Toggles the flag to start Play Mode from scene 0.
        /// </summary>
        [MenuItem(MENU_ITEM_PATH)]
        public static void ToggleStartFromScene0()
        {
            bool flag = EditorPrefs.GetBool(EDITOR_PREF_KEY, false);
            flag = !flag;
            EditorPrefs.SetBool(EDITOR_PREF_KEY, flag);
            Menu.SetChecked(MENU_ITEM_PATH, flag);
        }

        /// <summary>
        /// Validates and updates the checkmark state in the menu item.
        /// </summary>
        [MenuItem(MENU_ITEM_PATH, true)]
        public static bool ToggleStartFromScene0Validate()
        {
            bool flag = EditorPrefs.GetBool(EDITOR_PREF_KEY, false);
            Menu.SetChecked(MENU_ITEM_PATH, flag);
            return true;
        }

        /// <summary>
        /// Callback to handle Play Mode state changes.
        /// </summary>
        /// <param name="state">The new play mode state.</param>
        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            bool flag = EditorPrefs.GetBool(EDITOR_PREF_KEY, false);

            if (state == PlayModeStateChange.ExitingEditMode)
            {
                // When entering Play Mode, if the flag is enabled
                if (flag)
                {
                    // Save the active scene and all scenes (including their loaded state)
                    Scene activeScene = EditorSceneManager.GetActiveScene();
                    SceneStateList sceneStateList = new();

                    for (int i = 0; i < EditorSceneManager.sceneCount; i++)
                    {
                        Scene scene = EditorSceneManager.GetSceneAt(i);
                        sceneStateList.scenes.Add(new SceneState
                        {
                            path = scene.path,
                            isLoaded = scene.isLoaded
                        });
                    }

                    // Store the active scene path and all scene states as JSON
                    if (sceneStateList.scenes.Count > 0)
                    {
                        EditorPrefs.SetString(ACTIVE_SCENE_KEY, activeScene.path);
                        EditorPrefs.SetString(PREVIOUS_SCENES_KEY, JsonUtility.ToJson(sceneStateList));
                    }

                    // If the active scene is not the one at index 0, load scene 0
                    if (activeScene.buildIndex != 0 && EditorBuildSettings.scenes.Length > 0)
                    {
                        string scenePath = EditorBuildSettings.scenes[0].path;
                        if (!string.IsNullOrEmpty(scenePath))
                            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                    }
                }
            }
            else if (state == PlayModeStateChange.EnteredEditMode)
            {
                // When returning to Edit Mode, restore the previous active scene and scene states
                if (flag && EditorPrefs.HasKey(PREVIOUS_SCENES_KEY) && EditorPrefs.HasKey(ACTIVE_SCENE_KEY))
                {
                    string activeScenePath = EditorPrefs.GetString(ACTIVE_SCENE_KEY);
                    SceneStateList sceneStateList =
                        JsonUtility.FromJson<SceneStateList>(EditorPrefs.GetString(PREVIOUS_SCENES_KEY));

                    // Open the first scene as the main scene
                    if (sceneStateList.scenes.Count > 0 && !string.IsNullOrEmpty(sceneStateList.scenes[0].path))
                        EditorSceneManager.OpenScene(sceneStateList.scenes[0].path, OpenSceneMode.Single);

                    // Load or skip scenes based on their previous loaded state
                    for (int i = 1; i < sceneStateList.scenes.Count; i++)
                    {
                        string scenePath = sceneStateList.scenes[i].path;
                        bool wasLoaded = sceneStateList.scenes[i].isLoaded;

                        if (!string.IsNullOrEmpty(scenePath))
                            // Load the scene additively if it was previously loaded
                            EditorSceneManager.OpenScene(scenePath,
                                wasLoaded ? OpenSceneMode.Additive : OpenSceneMode.AdditiveWithoutLoading);
                    }

                    // Set the active scene
                    if (!string.IsNullOrEmpty(activeScenePath))
                    {
                        Scene activeScene = SceneManager.GetSceneByPath(activeScenePath);
                        if (activeScene.IsValid()) EditorSceneManager.SetActiveScene(activeScene);
                    }

                    // Clean up stored preferences
                    EditorPrefs.DeleteKey(PREVIOUS_SCENES_KEY);
                    EditorPrefs.DeleteKey(ACTIVE_SCENE_KEY);
                }
            }
        }
    }
}