using System.Collections.Generic;
using System.IO;
#if ODIN_INSPECTOR
using Sirenix.OdinInspector;
using Sirenix.Serialization;
#endif
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;

namespace LegendaryTools
{
#if ODIN_INSPECTOR
    public abstract class ConfigEnumWeaver<C> : SerializedScriptableObject, IWeaveExec
        where C : ScriptableObject
    {
        public string WeaveNamespaceName;
        public string WeaveEnumName;

        [ShowInInspector] [ReadOnly] 
        public WeaveExecType WeaveExecType => WeaveExecType.AfterCompile;
        
        [OdinSerialize][HideInInspector]
        protected Dictionary<string, C> configMapping;

        [Button]
        public virtual void Update()
        {
#if UNITY_EDITOR
            configMapping = new Dictionary<string, C>();
            List<C> allConfigs = EditorExtensions.FindAssetsByType<C>();
            List<string> configEnumNames = new List<string>();
            
            foreach (C config in allConfigs)
            {
                string enumName = config.name.Replace(" ", "");
                configEnumNames.Add(enumName);
                configMapping.Add(enumName, config);
            }

            List<MonoScript> enumCodeFile = EditorExtensions.FindAssetsByName<MonoScript>(WeaveEnumName);
            string configPath = AssetDatabase.GetAssetPath(this);
            
            if (enumCodeFile.Count > 0)
            {
                configPath = AssetDatabase.GetAssetPath(enumCodeFile[0]);
            }
            
            string configFolder = Path.GetDirectoryName(configPath);
            
            WeaverUtils.Enum(configEnumNames.ToArray(), WeaveNamespaceName, WeaveEnumName, configFolder);
#endif
        }
        
        public void RunWeaver()
        {
            if (configMapping != null)
            {
                Populate(configMapping);
                configMapping = null;
            }
        }

        protected abstract void Populate(Dictionary<string, C> configMapping);
    }
#endif
}