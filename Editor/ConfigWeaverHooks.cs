using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace LegendaryTools.Editor
{
    [InitializeOnLoad]
    public static class ConfigWeaverHooks
    {
        static ConfigWeaverHooks()
        {
            EditorApplication.playModeStateChanged += LogPlayModeState;
        }
        
        private static void LogPlayModeState(PlayModeStateChange newState)
        {
            switch (newState)
            {
                case PlayModeStateChange.EnteredEditMode:
                    TryRunWeaverIf(WeaveExecType.EnteredEditMode);
                    break;
                case PlayModeStateChange.ExitingEditMode:
                    TryRunWeaverIf(WeaveExecType.ExitingEditMode);
                    break;
                case PlayModeStateChange.EnteredPlayMode:
                    break;
                case PlayModeStateChange.ExitingPlayMode:
                    break;
            }
        }
        
        [UnityEditor.Callbacks.DidReloadScripts]
        private static void OnAfterAssemblyReload()
        {
            TryRunWeaverIf(WeaveExecType.AfterCompile);
        }

        private static void TryRunWeaverIf(WeaveExecType weaveExecType)
        {
            List<ScriptableObject> serializedScriptableObject =  EditorExtensions.FindAssetsByType<ScriptableObject>();

            foreach (ScriptableObject scriptableObject in serializedScriptableObject)
            {
                if (scriptableObject is IWeaveExec weaveExec)
                {
                    if(weaveExec.WeaveExecType == weaveExecType)
                        weaveExec.RunWeaver();
                }
            }

            List<(IWeaveExec, GameObject)> allGameObjects = new List<(IWeaveExec, GameObject)>();
            allGameObjects.AddRange(EditorExtensions.FindPrefabsOfType<IWeaveExec>());
            allGameObjects.AddRange(EditorExtensions.FindSceneObjectsOfType<IWeaveExec>());
            
            foreach ((IWeaveExec, GameObject) gameObject in allGameObjects)
            {
                if (gameObject.Item1 is IWeaveExec weaveExec)
                {
                    if(weaveExec.WeaveExecType == weaveExecType)
                        weaveExec.RunWeaver();
                }
            }
        }
    }
}