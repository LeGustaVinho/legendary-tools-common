using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace HierarchyDecorator
{
    [InitializeOnLoad]
    internal static class HierarchyDecorator
    {
        public static event Action OnSettings;

        private static Settings s_Settings;
        public static Settings Settings
        {
            get
            {
                if (s_Settings == null)
                {
                    s_Settings = GetOrCreateSettings();
                    OnSettings?.Invoke();
                }

                return s_Settings;
            }
        }

        static HierarchyDecorator()
        {
            OnSettings -= UpdateComponentData;
            OnSettings += UpdateComponentData;

            EditorApplication.delayCall -= HierarchyManager.Initialize;
            EditorApplication.delayCall += HierarchyManager.Initialize;
        }

        private static void UpdateComponentData()
        {
            Settings.Components.UpdateData();
            Settings.Components.UpdateComponents(true);
        }

        private static Settings GetOrCreateSettings()
        {
            bool hasLocalSettingsFile = HasLocalSettingsFile();
            Settings settings = global::HierarchyDecorator.Settings.instance;
            settings.EnsureInitialized();

            if (hasLocalSettingsFile)
            {
                return settings;
            }

            if (TryLoadLegacySettings(out Settings legacySettings))
            {
                EditorUtility.CopySerialized(legacySettings, settings);
                settings.EnsureInitialized();
            }
            else
            {
                settings.SetDefaults(EditorGUIUtility.isProSkin);
            }

            settings.SaveSettings();
            return settings;
        }

        private static bool HasLocalSettingsFile()
        {
            string projectPath = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrEmpty(projectPath))
            {
                return false;
            }

            string settingsPath = Path.Combine(projectPath, Constants.Paths.LOCAL_SETTINGS_PATH);
            return File.Exists(settingsPath);
        }

        private static bool TryLoadLegacySettings(out Settings settings)
        {
            settings = AssetDatabase.LoadAssetAtPath<Settings>(Constants.Paths.LEGACY_ASSET_PATH);
            return settings != null;
        }

        public static SerializedObject GetSerializedSettings()
        {
            return new SerializedObject(GetOrCreateSettings());
        }

        public class HierarchyDecoratorProcessor : AssetPostprocessor
        {
            static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths, bool didDomainReload)
            {
                if (importedAssets.Length > 0)
                {
                    UpdateComponentData();
                }
            }
        }
    }
}
