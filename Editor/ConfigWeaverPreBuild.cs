using System.Collections.Generic;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace LegendaryTools.Editor
{
    public class ConfigWeaverPreBuild : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;
        
        public void OnPreprocessBuild(BuildReport report)
        {
            Debug.Log("[ConfigWeaverPreBuild] Config weaver pre build started !");
            List<ScriptableObject> serializedScriptableObject =  EditorExtensions.FindAssetsByType<ScriptableObject>();

            foreach (ScriptableObject scriptableObject in serializedScriptableObject)
            {
                if (scriptableObject is IWeaveExec weaveExec)
                {
                    weaveExec.RunWeaver();
                }
            }
        }
    }
}