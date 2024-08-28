using System.Collections.Generic;
using System.IO;
#if ODIN_INSPECTOR
using Sirenix.OdinInspector;
#endif
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;

namespace LegendaryTools
{
#if ODIN_INSPECTOR
    public class DictionaryConfigNamesWeaver<C> : SerializedScriptableObject, IWeaveExec
        where C : ScriptableObject
    {
        public string WeaveNamespaceName;
        public string WeaveClassName;
        
        [ShowInInspector]
        public WeaveExecType WeaveExecType { get; set; }
    
        [ShowInInspector]
        public Dictionary<string, C> Configs = new Dictionary<string, C>();
        
        public Dictionary<C, string> InvertedConfigs = new Dictionary<C, string>();
        
        [Button]
        public virtual void Update()
        {
#if UNITY_EDITOR
            Configs = new Dictionary<string, C>();
            List<C> allConfigs = EditorExtensions.FindAssetsByType<C>();
            List<string> names = new List<string>();
            
            foreach (C config in allConfigs)
            {
                names.Add(config.name);
                Configs.Add(config.name, config);
                InvertedConfigs.AddOrUpdate(config, config.name);
            }

            List<MonoScript> enumCodeFile = EditorExtensions.FindAssetsByName<MonoScript>(WeaveClassName);
            string configPath = AssetDatabase.GetAssetPath(this);
            
            if (enumCodeFile.Count > 0)
            {
                configPath = AssetDatabase.GetAssetPath(enumCodeFile[0]);
            }
            
            string configFolder = Path.GetDirectoryName(configPath);
            
            WeaverUtils.ClassConstantNameListing(names.ToArray(), WeaveNamespaceName, WeaveClassName, configFolder);
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
#endif

        public void RunWeaver()
        {
            Update();
        }
    }
#endif
}