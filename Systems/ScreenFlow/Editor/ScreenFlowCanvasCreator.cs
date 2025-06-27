using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace LegendaryTools.Systems.ScreenFlow.Editor
{
    public static class ScreenFlowCanvasCreator
    {
        [MenuItem("Tools/LegendaryTools/ScreenFlow/Setup ScreenFlow", false, 10)]
        private static void CreateScreenFlowCanvas()
        {
            // Create Canvas
            GameObject canvasGO = new GameObject("[Canvas] ScreenFlow");
            Canvas canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            // Add CanvasScaler with specified settings
            CanvasScaler canvasScaler = canvasGO.AddComponent<CanvasScaler>();
            canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            canvasScaler.referenceResolution = new Vector2(1920, 1080);
            canvasScaler.matchWidthOrHeight = 1f; // Match height

            // Add GraphicRaycaster
            canvasGO.AddComponent<GraphicRaycaster>();

            // Add ScreenFlow component
            ScreenFlow screenFlow = canvasGO.AddComponent<ScreenFlow>();
            screenFlow.ForceSingleInstance = true;
            screenFlow.IsPersistent = true;
            screenFlow.AutoInitializeOnStart = true;

            // Find or create ScreenFlowConfig
            ScreenFlowConfig config = FindOrCreateScreenFlowConfig();
            screenFlow.Config = config;
            
            if(config.Screens.Length > 0)
                screenFlow.StartScreen = config.Screens[0];
            
            // Ensure the Canvas is properly positioned in the hierarchy
            if (Selection.activeGameObject != null)
            {
                canvasGO.transform.SetParent(Selection.activeGameObject.transform, false);
            }

            // Select the newly created Canvas
            Selection.activeGameObject = canvasGO;
            Undo.RegisterCreatedObjectUndo(canvasGO, "Create ScreenFlow Canvas");

            // Mark scene as dirty
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
        }

        private static ScreenFlowConfig FindOrCreateScreenFlowConfig()
        {
            // Search for existing ScreenFlowConfig
            string[] guids = AssetDatabase.FindAssets("t:ScreenFlowConfig");
            if (guids.Length > 0)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                return AssetDatabase.LoadAssetAtPath<ScreenFlowConfig>(path);
            }

            // Create new ScreenFlowConfig if none exists
            ScreenFlowConfig config = ScriptableObject.CreateInstance<ScreenFlowConfig>();
            string configPath = AssetDatabase.GenerateUniqueAssetPath("Assets/ScreenFlowConfig.asset");
            AssetDatabase.CreateAsset(config, configPath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[ScreenFlowCanvasCreator] Created new ScreenFlowConfig at {configPath}");
            return config;
        }

        [MenuItem("GameObject/UI/ScreenFlow Canvas", true)]
        private static bool CreateScreenFlowCanvasValidation()
        {
            // Always allow creation of ScreenFlow Canvas
            return true;
        }
    }
}