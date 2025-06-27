using UnityEditor;
using UnityEngine;
using LegendaryTools.Systems.AssetProvider;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using Newtonsoft.Json;
using Object = UnityEngine.Object;

namespace LegendaryTools.Systems.ScreenFlow.Editor
{
    public static class ScreenFlowConfigCreator
    {
        private static readonly string s_isWaitingRecompile = "ScreenFlowConfigCreator_WaitingRecompile";
        private static readonly string s_pendingOperationsKey = "ScreenFlowConfigCreator_PendingOperations";
        private static readonly string[] AssetLoaderTypes = new[]
        {
            nameof(HardRefAssetLoaderConfig),
            nameof(ResourcesAssetLoaderConfig),
            #if ASSET_PROVIDER_HAS_ADDRESSABLES
            nameof(AddressablesAssetLoaderConfig)
            #endif
        };

        // Struct to store pending operations, marked as Serializable for JSON
        [System.Serializable]
        private struct PendingOperation
        {
            public string PrefabPath; // Store path instead of GameObject to persist across recompilation
            public bool IsScreen;
            public string PrefabName;
            public string ConfigPath; // Store path to ScreenConfig or PopupConfig
            public bool NeedsComponent; // Indicates if a concrete component needs to be added
        }

        [MenuItem("Assets/Create/ScreenFlow/Create Screen or Popup Config", false, 80)]
        private static void CreateScreenOrPopupConfig()
        {
            Object[] selectedObjects = Selection.GetFiltered(typeof(GameObject), SelectionMode.Assets);

            if (selectedObjects.Length == 0)
            {
                EditorUtility.DisplayDialog("Error", "Please select one or more prefabs in the Project window.", "OK");
                return;
            }

            // Show dialog to choose config type and asset loader type
            bool isScreen = EditorUtility.DisplayDialog("Config Type", "Create a ScreenConfig or PopupConfig?", "ScreenConfig", "PopupConfig");
            int loaderChoice = EditorUtility.DisplayDialogComplex("Asset Loader Type", 
                "Choose the Asset Loader type:", 
                AssetLoaderTypes[0], 
                AssetLoaderTypes[1], 
                AssetLoaderTypes[2]);

            if (loaderChoice == -1) return; // User cancelled

            List<PendingOperation> pendingOperations = new List<PendingOperation>();
            EditorPrefs.SetBool(s_isWaitingRecompile, true);

            foreach (Object selectedObject in selectedObjects)
            {
                GameObject prefab = (GameObject)selectedObject;
                string prefabPath = AssetDatabase.GetAssetPath(prefab);
                string directory = Path.GetDirectoryName(prefabPath);
                string prefabName = SanitizeClassName(Path.GetFileNameWithoutExtension(prefabPath));

                // Check if prefab already has ScreenBase or PopupBase
                GameObject prefabInstance = PrefabUtility.LoadPrefabContents(prefabPath);
                bool hasComponent = prefabInstance.GetComponent<ScreenBase>() != null;
                PrefabUtility.UnloadPrefabContents(prefabInstance);

                // Create a folder for this prefab's assets
                string prefabFolder = Path.Combine(directory, prefabName);
                if (!AssetDatabase.IsValidFolder(prefabFolder))
                {
                    AssetDatabase.CreateFolder(directory, prefabName);
                }

                // Move the prefab to the appropriate folder
                string newPrefabPath;
                if (loaderChoice == 1) // ResourcesAssetLoaderConfig
                {
                    string resourcesFolder = Path.Combine(prefabFolder, "Resources");
                    if (!AssetDatabase.IsValidFolder(resourcesFolder))
                    {
                        AssetDatabase.CreateFolder(prefabFolder, "Resources");
                    }
                    newPrefabPath = Path.Combine(resourcesFolder, Path.GetFileName(prefabPath));
                }
                else
                {
                    newPrefabPath = Path.Combine(prefabFolder, Path.GetFileName(prefabPath));
                }

                string uniquePrefabPath = AssetDatabase.GenerateUniqueAssetPath(newPrefabPath);
                if (AssetDatabase.MoveAsset(prefabPath, uniquePrefabPath) != string.Empty)
                {
                    Debug.LogWarning($"[ScreenFlowConfigCreator] Failed to move prefab {prefabPath} to {uniquePrefabPath}.");
                    continue;
                }
                prefabPath = uniquePrefabPath; // Update prefabPath to new location

                // Create concrete class only if no component exists
                string classFilePath = null;
                if (!hasComponent)
                {
                    classFilePath = CreateConcreteClass(isScreen, prefabFolder, prefabName);
                }

                // Create AssetLoaderConfig
                AssetLoaderConfig assetLoaderConfig = CreateAssetLoaderConfig(loaderChoice, prefabFolder, prefabName, prefab);

                // Create ScreenConfig or PopupConfig
                UIEntityBaseConfig config = CreateUIEntityConfig(isScreen, prefabFolder, prefabName, assetLoaderConfig);

                // Store pending operation to add component (if needed) and config after compilation
                pendingOperations.Add(new PendingOperation
                {
                    PrefabPath = prefabPath,
                    IsScreen = isScreen,
                    PrefabName = prefabName,
                    ConfigPath = AssetDatabase.GetAssetPath(config),
                    NeedsComponent = !hasComponent
                });
            }

            // Serialize pending operations to EditorPrefs
            string json = JsonConvert.SerializeObject(pendingOperations);
            EditorPrefs.SetString(s_pendingOperationsKey, json);

            // Save and refresh assets to trigger compilation
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        [MenuItem("Assets/Create/ScreenFlow/Create Screen or Popup Config", true)]
        private static bool CreateScreenOrPopupConfigValidation()
        {
            // Only show menu item if at least one prefab is selected
            return Selection.GetFiltered(typeof(GameObject), SelectionMode.Assets).Length > 0;
        }

        [UnityEditor.Callbacks.DidReloadScripts]
        private static void OnAfterAssemblyReload()
        {
            if (EditorPrefs.GetBool(s_isWaitingRecompile))
            {
                EditorApplication.delayCall += OnInspectorReloadComplete;
            }
        }

        private static void OnInspectorReloadComplete()
        {
            EditorApplication.delayCall -= OnInspectorReloadComplete;
            EditorPrefs.SetBool(s_isWaitingRecompile, false);

            // Deserialize pending operations from EditorPrefs
            string json = EditorPrefs.GetString(s_pendingOperationsKey, string.Empty);
            List<PendingOperation> pendingOperations = string.IsNullOrEmpty(json)
                ? new List<PendingOperation>()
                : JsonConvert.DeserializeObject<List<PendingOperation>>(json);

            // Add components to prefabs (if needed)
            foreach (var operation in pendingOperations)
            {
                if (operation.NeedsComponent)
                {
                    GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(operation.PrefabPath);
                    if (prefab != null)
                    {
                        AddConcreteComponent(prefab, operation.IsScreen, operation.PrefabName);
                    }
                    else
                    {
                        Debug.LogWarning($"[ScreenFlowConfigCreator] Could not load prefab at path {operation.PrefabPath}. Component not added.");
                    }
                }
            }

            // Find all ScreenFlowConfig assets
            string[] guids = AssetDatabase.FindAssets("t:ScreenFlowConfig");
            List<ScreenFlowConfig> screenFlowConfigs = new List<ScreenFlowConfig>();
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                ScreenFlowConfig config = AssetDatabase.LoadAssetAtPath<ScreenFlowConfig>(path);
                if (config != null)
                {
                    screenFlowConfigs.Add(config);
                }
            }

            // Select or create ScreenFlowConfig
            ScreenFlowConfig selectedConfig;
            if (screenFlowConfigs.Count == 0)
            {
                // Create a new ScreenFlowConfig
                selectedConfig = ScriptableObject.CreateInstance<ScreenFlowConfig>();
                string configPath = AssetDatabase.GenerateUniqueAssetPath("Assets/ScreenFlowConfig.asset");
                AssetDatabase.CreateAsset(selectedConfig, configPath);
                Debug.Log($"[ScreenFlowConfigCreator] Created new ScreenFlowConfig at {configPath}");
            }
            else if (screenFlowConfigs.Count == 1)
            {
                selectedConfig = screenFlowConfigs[0];
            }
            else
            {
                // Prompt user to select a ScreenFlowConfig
                string[] configNames = screenFlowConfigs.Select(c => c.name).ToArray();
                int choice = EditorUtility.DisplayDialogComplex("Select ScreenFlowConfig", 
                    "Multiple ScreenFlowConfig assets found. Please select one to add the Screens/Popups to:", 
                    configNames[0], 
                    configNames.Length > 1 ? configNames[1] : "Cancel", 
                    "Create New");

                if (choice == 2 || choice == -1) // Create New or Cancel
                {
                    selectedConfig = ScriptableObject.CreateInstance<ScreenFlowConfig>();
                    string configPath = AssetDatabase.GenerateUniqueAssetPath("Assets/ScreenFlowConfig.asset");
                    AssetDatabase.CreateAsset(selectedConfig, configPath);
                    Debug.Log($"[ScreenFlowConfigCreator] Created new ScreenFlowConfig at {configPath}");
                }
                else
                {
                    selectedConfig = screenFlowConfigs[choice];
                }
            }

            // Add ScreenConfig or PopupConfig to the selected ScreenFlowConfig
            foreach (var operation in pendingOperations)
            {
                UIEntityBaseConfig config = AssetDatabase.LoadAssetAtPath<UIEntityBaseConfig>(operation.ConfigPath);
                if (config == null)
                {
                    Debug.LogWarning($"[ScreenFlowConfigCreator] Could not load config at path {operation.ConfigPath}. Skipping addition to ScreenFlowConfig.");
                    continue;
                }

                if (operation.IsScreen)
                {
                    ScreenConfig screenConfig = config as ScreenConfig;
                    if (!selectedConfig.Screens.Contains(screenConfig))
                        selectedConfig.Screens = selectedConfig.Screens.AddItem(screenConfig);
                }
                else
                {
                    PopupConfig popupConfig = config as PopupConfig;
                    if (!selectedConfig.Popups.Contains(popupConfig))
                        selectedConfig.Popups = selectedConfig.Popups.AddItem(popupConfig);
                }
            }

            // Mark the selected ScreenFlowConfig as dirty
            EditorUtility.SetDirty(selectedConfig);

            // Clear serialized data
            EditorPrefs.DeleteKey(s_pendingOperationsKey);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog("Success", 
                $"Created {(pendingOperations.Any(op => op.IsScreen) ? "ScreenConfig" : "PopupConfig")}, concrete class (if needed), modified {(pendingOperations.Count > 1 ? "prefabs" : "prefab")}, and added to ScreenFlowConfig successfully!", 
                "OK");
        }

        private static string SanitizeClassName(string name)
        {
            // Remove invalid characters and ensure the name starts with a letter
            string sanitized = Regex.Replace(name, "[^a-zA-Z0-9_]", "");
            if (string.IsNullOrEmpty(sanitized) || char.IsDigit(sanitized[0]))
            {
                sanitized = "ScreenFlow" + sanitized;
            }
            return sanitized;
        }

        private static string CreateConcreteClass(bool isScreen, string folderPath, string prefabName)
        {
            string className = $"{prefabName}{(isScreen ? "Screen" : "Popup")}";
            string filePath = Path.Combine(folderPath, $"{className}.cs");
            string baseClass = isScreen ? "ScreenBase" : "PopupBase";
            string namespaceName = "ScreenFlow.CodeGenerator";

            // Create the concrete class content
            StringBuilder classContent = new StringBuilder();
            classContent.AppendLine($"using System.Threading.Tasks;");
            classContent.AppendLine($"using LegendaryTools.Systems.ScreenFlow;");
            classContent.AppendLine();
            classContent.AppendLine($"namespace {namespaceName}");
            classContent.AppendLine($"{{");
            classContent.AppendLine($"    public class {className} : {baseClass}");
            classContent.AppendLine($"    {{");
            if (!isScreen)
            {
                classContent.AppendLine($"        public override void OnGoToBackground(System.Object args)");
                classContent.AppendLine($"        {{");
                classContent.AppendLine($"            // Implement background behavior here");
                classContent.AppendLine($"        }}");
                classContent.AppendLine();
            }
            classContent.AppendLine($"        public override async Task Show(System.Object args)");
            classContent.AppendLine($"        {{");
            classContent.AppendLine($"            // Implement show behavior here");
            classContent.AppendLine($"            await Task.Yield();");
            classContent.AppendLine($"        }}");
            classContent.AppendLine();
            classContent.AppendLine($"        public override async Task Hide(System.Object args)");
            classContent.AppendLine($"        {{");
            classContent.AppendLine($"            // Implement hide behavior here");
            classContent.AppendLine($"            await Task.Yield();");
            classContent.AppendLine($"        }}");
            classContent.AppendLine($"    }}");
            classContent.AppendLine($"}}");

            // Write the class file
            File.WriteAllText(filePath, classContent.ToString());
            AssetDatabase.ImportAsset(filePath);

            return filePath;
        }

        private static AssetLoaderConfig CreateAssetLoaderConfig(int loaderChoice, string folderPath, string prefabName, GameObject prefab)
        {
            string loaderPath = Path.Combine(folderPath, $"{prefabName}_{AssetLoaderTypes[loaderChoice]}.asset");
            AssetLoaderConfig config;

            switch (loaderChoice)
            {
                case 0: // HardRefAssetLoaderConfig
                    config = ScriptableObject.CreateInstance<HardRefAssetLoaderConfig>();
                    ((HardRefAssetLoaderConfig)config).SetField("HardReference", prefab);
                    break;
                case 1: // ResourcesAssetLoaderConfig
                    config = ScriptableObject.CreateInstance<ResourcesAssetLoaderConfig>();
                    string resourcesPath = GetResourcesRelativePath(AssetDatabase.GetAssetPath(prefab));
                    if (!string.IsNullOrEmpty(resourcesPath))
                    {
                        ((ResourcesAssetLoaderConfig)config).SetField("ResourcePathReference", 
                            new ResourcePathReference { resourcePath = resourcesPath });
                    }
                    else
                    {
                        Debug.LogWarning($"[ScreenFlowConfigCreator] Prefab {prefabName} is not in a Resources folder. ResourcePath will be empty.");
                    }
                    break;
#if ASSET_PROVIDER_HAS_ADDRESSABLES
                case 2: // AddressablesAssetLoaderConfig
                    config = ScriptableObject.CreateInstance<AddressablesAssetLoaderConfig>();
                    break;
#endif
                default:
                    throw new System.ArgumentException("Invalid loader choice");
            }

            AssetDatabase.CreateAsset(config, AssetDatabase.GenerateUniqueAssetPath(loaderPath));
            return config;
        }

        private static UIEntityBaseConfig CreateUIEntityConfig(bool isScreen, string folderPath, string prefabName, AssetLoaderConfig assetLoaderConfig)
        {
            string configPath = Path.Combine(folderPath, $"{prefabName}{(isScreen ? "Screen" : "Popup")}.asset");
            UIEntityBaseConfig config = isScreen 
                ? ScriptableObject.CreateInstance<ScreenConfig>() 
                : ScriptableObject.CreateInstance<PopupConfig>();

            config.name = prefabName;
            config.SetField("AssetLoaderConfig", assetLoaderConfig);

            // Set default values for ScreenConfig
            if (isScreen)
            {
                ScreenConfig screenConfig = (ScreenConfig)config;
                screenConfig.SetField("AllowPopups", true);
                screenConfig.SetField("CanMoveBackFromHere", true);
                screenConfig.SetField("CanMoveBackToHere", true);
                screenConfig.SetField("BackKeyBehaviour", BackKeyBehaviour.ScreenMoveBack);
                screenConfig.SetField("PopupBehaviourOnScreenTransition", PopupsBehaviourOnScreenTransition.HideFirstThenTransit);
            }

            AssetDatabase.CreateAsset(config, AssetDatabase.GenerateUniqueAssetPath(configPath));
            return config;
        }

        private static void AddConcreteComponent(GameObject prefab, bool isScreen, string prefabName)
        {
            // Load the prefab in edit mode
            GameObject prefabInstance = PrefabUtility.LoadPrefabContents(AssetDatabase.GetAssetPath(prefab));

            // Check if component already exists (redundant check for safety)
            bool hasComponent = prefabInstance.GetComponent<ScreenBase>() != null;

            if (!hasComponent)
            {
                // Find the concrete component type across all loaded assemblies
                string componentTypeName = $"{prefabName}{(isScreen ? "Screen" : "Popup")}";
                System.Type componentType = System.AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(assembly => assembly.GetTypes())
                    .FirstOrDefault(t => t.Name == componentTypeName && t.Namespace == "ScreenFlow.CodeGenerator");

                if (componentType != null)
                {
                    prefabInstance.AddComponent(componentType);
                    PrefabUtility.SaveAsPrefabAsset(prefabInstance, AssetDatabase.GetAssetPath(prefab));
                }
                else
                {
                    Debug.LogWarning($"[ScreenFlowConfigCreator] Could not find concrete type {componentTypeName} in namespace ScreenFlow.CodeGenerator. Component not added to prefab {prefab.name}.");
                }
            }

            // Unload the prefab instance
            PrefabUtility.UnloadPrefabContents(prefabInstance);
        }

        private static string GetResourcesRelativePath(string assetPath)
        {
            string resourcesPath = "/Resources/";
            int index = assetPath.IndexOf(resourcesPath);
            if (index >= 0)
            {
                string relativePath = assetPath.Substring(index + resourcesPath.Length);
                int extIndex = relativePath.LastIndexOf('.');
                if (extIndex >= 0)
                {
                    relativePath = relativePath.Substring(0, extIndex);
                }
                return relativePath;
            }
            return string.Empty;
        }

        // Helper method to set private/serialized fields via reflection
        private static void SetField(this Object obj, string fieldName, object value)
        {
            var fieldInfo = obj.GetType().GetField(fieldName, System.Reflection.BindingFlags.Instance | 
                                                          System.Reflection.BindingFlags.Public | 
                                                          System.Reflection.BindingFlags.NonPublic);
            if (fieldInfo != null)
            {
                fieldInfo.SetValue(obj, value);
            }
            else
            {
                Debug.LogWarning($"[ScreenFlowConfigCreator] Could not find field {fieldName} in {obj.GetType().Name}");
            }
        }
    }
}