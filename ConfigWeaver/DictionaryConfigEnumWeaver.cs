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
    public abstract class DictionaryConfigEnumWeaver<E, C> : SerializedScriptableObject, IWeaveExec, IDictionaryConfigEnumWeaver<E, C>
        where C : ScriptableObject
        where E : struct, Enum, IConvertible
    {
        [ShowInInspector] public Dictionary<E, C> Configs => configs;
        public Dictionary<C, E> InvertedConfigs => invertedConfigs;
        
        [OdinSerialize][HideInInspector]
        private Dictionary<E, C> configs = new Dictionary<E, C>();
        [OdinSerialize][HideInInspector]
        private Dictionary<C, E> invertedConfigs = new Dictionary<C, E>();

        [OdinSerialize][HideInInspector]
        protected Dictionary<string, C> configMapping;

        [ShowInInspector][ReadOnly] 
        public WeaveExecType WeaveExecType => WeaveExecType.AfterCompile;

        protected virtual void Populate(Dictionary<string, C> configMapping)
        {
            configs.Clear();
            foreach (KeyValuePair<string, C> pair in configMapping)
            {
                E enumType = pair.Key.GetEnumValue<E>();
                configs.Add(enumType, pair.Value);
                invertedConfigs.AddOrUpdate(pair.Value, enumType);
            }
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
#endif
        }

        [Button]
        public virtual void Update()
        {
#if UNITY_EDITOR
            configMapping = new Dictionary<string, C>();
            List<C> allConfigs = EditorExtensions.FindAssetsByType<C>();
            List<string> configEnumNames = new List<string>();
            
            foreach (C config in allConfigs)
            {
                string enumName = config.name.FilterEnumName();
                configEnumNames.Add(enumName);
                configMapping.Add(enumName, config);
            }

            Type enumType = typeof(E);

            bool allFound = true;
            foreach (KeyValuePair<string, C> pair in configMapping)
            {
                if (Enum.TryParse<E>(pair.Key, out E parsedEnum))
                {
                    if (!configs.TryAdd(parsedEnum, pair.Value))
                        allFound = false;
                    if (!invertedConfigs.TryAdd(pair.Value, parsedEnum))
                        allFound = false;
                }
            }

            if (!allFound)
            {
                List<MonoScript> enumCodeFile = EditorExtensions.FindAssetsByName<MonoScript>(enumType.Name);
                string configPath = AssetDatabase.GetAssetPath(this);

                if (enumCodeFile.Count > 0)
                    configPath = AssetDatabase.GetAssetPath(enumCodeFile[0]);

                string configFolder = Path.GetDirectoryName(configPath);
                WeaverUtils.Enum(configEnumNames.ToArray(), enumType.Namespace, enumType.Name, configFolder, true);
            }
#endif
        }
#if UNITY_EDITOR
        [Button]
        public void CreateConfig(string configName)
        {
            C newConfig = CreateInstance<C>();
            newConfig.name = configName;
            string configPath = AssetDatabase.GetAssetPath(this);
            string configFolder = Path.GetDirectoryName(configPath);
            AssetDatabase.CreateAsset(newConfig, Path.Combine(configFolder, configName + ".asset"));
            AssetDatabase.SaveAssets();
            Update();
        }

        [ContextMenu("Force Set Dirty")]
        public void ForceSetDirty()
        {
            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif
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