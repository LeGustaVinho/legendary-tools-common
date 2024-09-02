using System.Collections.Generic;
using UnityEngine;

namespace LegendaryTools
{
    public class ConfigListing<T> : ScriptableObject, IWeaveExec
        where T : ScriptableObject
    {
        public WeaveExecType weaveExecType;
        public WeaveExecType WeaveExecType => weaveExecType;

        public List<T> Configs = new List<T>();

        [ContextMenu("RunWeaver")]
#if ODIN_INSPECTOR
        [Sirenix.OdinInspector.Button("Update")]
#endif
        public void RunWeaver()
        {
#if UNITY_EDITOR
            Configs = EditorExtensions.FindAssetsByType<T>();
            UnityEditor.EditorUtility.SetDirty(this);
#endif
        }
        
#if UNITY_EDITOR && ODIN_INSPECTOR
        [Sirenix.OdinInspector.Button]
        public void CreateConfig(string configName)
        {
            T newConfig = CreateInstance<T>();
            newConfig.name = configName;
            string configPath = UnityEditor.AssetDatabase.GetAssetPath(this);
            string configFolder = System.IO.Path.GetDirectoryName(configPath);
            UnityEditor.AssetDatabase.CreateAsset(newConfig, System.IO.Path.Combine(configFolder, configName + ".asset"));
            UnityEditor.AssetDatabase.SaveAssets();
            RunWeaver();
        }
#endif
#if UNITY_EDITOR
        [ContextMenu("Force Set Dirty")]
        public void ForceSetDirty()
        {
            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif
    }
}