using System;
using System.Collections.Generic;
using System.IO;
#if UNITY_EDITOR
using UnityEditor;
#endif
#if ODIN_INSPECTOR
using Sirenix.Serialization;
using Sirenix.OdinInspector;
#endif
using UnityEngine;

namespace LegendaryTools
{
#if ODIN_INSPECTOR
    /// <summary>
    /// Finds all ScriptableObjects of type C, then makes sure that an enum of type E exists with one entry per config.
    /// If the enum file is out-of-date it will be (re)generated. After Unity recompiles the enum, the mapping dictionaries
    /// (Configs and InvertedConfigs) are updated from the configMapping.
    /// </summary>
    public abstract class DictionaryConfigEnumWeaver<E, C> : SerializedScriptableObject, IWeaveExec, IDictionaryConfigEnumWeaver<E, C>
        where C : ScriptableObject
        where E : struct, Enum, IConvertible
    {
        // Exposed dictionaries (read-only)
        [ShowInInspector]
        public Dictionary<E, C> Configs => configs;
        public Dictionary<C, E> InvertedConfigs => invertedConfigs;
        
        [OdinSerialize, HideInInspector]
        private Dictionary<E, C> configs = new Dictionary<E, C>();

        [OdinSerialize, HideInInspector]
        private Dictionary<C, E> invertedConfigs = new Dictionary<C, E>();

        // Temporarily holds the mapping from a valid enum name to its config.
        [OdinSerialize, HideInInspector]
        protected Dictionary<string, C> configMapping;

        [ShowInInspector, ReadOnly]
        public WeaveExecType WeaveExecType => WeaveExecType.AfterCompile;

        /// <summary>
        /// Once the enum type is available (after compilation), populate the dictionaries.
        /// </summary>
        /// <param name="mapping">Mapping from enum name to config.</param>
        protected virtual void Populate(Dictionary<string, C> mapping)
        {
            // Clear any previous data.
            configs.Clear();
            invertedConfigs.Clear();
            foreach (KeyValuePair<string, C> pair in mapping)
            {
                // Convert the string key (which was used to generate the enum) back into the enum value.
                if (Enum.TryParse<E>(pair.Key, out E enumValue))
                {
                    configs[enumValue] = pair.Value;
                    invertedConfigs[pair.Value] = enumValue;
                }
                else
                {
                    Debug.LogWarning($"Could not parse '{pair.Key}' to enum {typeof(E)}.");
                }
            }
#if UNITY_EDITOR
            EditorUtility.SetDirty(this);
#endif
        }

#if UNITY_EDITOR
        /// <summary>
        /// Updates the enum file (if needed) from all ScriptableObjects of type C.
        /// If the enum does not require regeneration the dictionaries are updated immediately.
        /// </summary>
        [Button]
        public virtual void Update()
        {
            // Find all ScriptableObjects of type C.
            List<C> allConfigs = EditorExtensions.FindAssetsByType<C>();

            // Build a new mapping from a valid enum name to each config.
            Dictionary<string, C> newMapping = new Dictionary<string, C>();
            List<string> configEnumNames = new List<string>();

            foreach (C config in allConfigs)
            {
                // Ensure the name is valid as an enum entry.
                string enumName = config.name.FilterEnumName();
                if (!newMapping.ContainsKey(enumName))
                {
                    newMapping.Add(enumName, config);
                    configEnumNames.Add(enumName);
                }
                else
                {
                    Debug.LogWarning($"Duplicate enum entry detected: '{enumName}' from config '{config.name}'.");
                }
            }

            // Determine whether the enum type E already exists and matches the current configs.
            Type enumType = typeof(E);
            string[] currentEnumNames = Enum.GetNames(enumType);
            bool enumNeedsUpdate = false;

            // If counts differ, or if any name is missing…
            if (currentEnumNames.Length != configEnumNames.Count)
            {
                enumNeedsUpdate = true;
            }
            else
            {
                foreach (string name in configEnumNames)
                {
                    if (!Enum.IsDefined(enumType, name))
                    {
                        enumNeedsUpdate = true;
                        break;
                    }
                }
            }

            // Save the new mapping; this mapping will be used by Populate() after compile.
            configMapping = newMapping;

            if (enumNeedsUpdate)
            {
                // Find where to create/update the enum file.
                List<MonoScript> enumCodeFiles = EditorExtensions.FindAssetsByName<MonoScript>(enumType.Name);
                string assetPath = AssetDatabase.GetAssetPath(this);
                if (enumCodeFiles.Count > 0)
                    assetPath = AssetDatabase.GetAssetPath(enumCodeFiles[0]);
                string folder = Path.GetDirectoryName(assetPath);

                // Regenerate or update the enum file.
                WeaverUtils.Enum(configEnumNames.ToArray(), enumType.Namespace, enumType.Name, folder, true);
                Debug.Log($"Enum {enumType.Name} was regenerated because it did not match the configs.");
            }
            else
            {
                // No enum update was required; update the dictionaries now.
                Populate(configMapping);
                // Clear the mapping since it is no longer needed.
                configMapping = null;
            }
        }

        /// <summary>
        /// Creates a new instance of C, stores it as an asset, and updates the mapping.
        /// </summary>
        /// <param name="configName">The desired name for the new config.</param>
        [Button]
        public void CreateConfig(string configName)
        {
            C newConfig = CreateInstance<C>();
            newConfig.name = configName;
            string assetPath = AssetDatabase.GetAssetPath(this);
            string folder = Path.GetDirectoryName(assetPath);
            string newAssetPath = Path.Combine(folder, configName + ".asset");
            AssetDatabase.CreateAsset(newConfig, newAssetPath);
            AssetDatabase.SaveAssets();
            Update();
        }

        [ContextMenu("Force Set Dirty")]
        public void ForceSetDirty()
        {
            EditorUtility.SetDirty(this);
        }
#endif

        /// <summary>
        /// Called by the weave system after compile. If a config mapping exists, populate the dictionaries.
        /// Otherwise, trigger an update.
        /// </summary>
        public void RunWeaver()
        {
#if UNITY_EDITOR
            if (configMapping != null)
            {
                Populate(configMapping);
                configMapping = null;
            }
            else
            {
                Update();
            }
#endif
        }
    }
#endif
}