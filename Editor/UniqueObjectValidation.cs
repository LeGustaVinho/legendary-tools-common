using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace LegendaryTools.Editor
{
    [InitializeOnLoad]
    public static class UniqueObjectValidation
    {
        static UniqueObjectValidation()
        {
            EditorApplication.hierarchyChanged += CheckUniqueObjects;
            EditorApplication.projectChanged += CheckUniqueObjects;
            EditorApplication.playModeStateChanged += LogPlayModeState;
        }
        
        private static void LogPlayModeState(PlayModeStateChange newState)
        {
            CheckUniqueObjects();
        }
        
        [UnityEditor.Callbacks.DidReloadScripts]
        private static void OnAfterAssemblyReload()
        {
            CheckUniqueObjects();
        }

        private static void CheckUniqueObjects()
        {
            UniqueObjectListing.UniqueObjects.Clear();
            List<UniqueScriptableObject> allUniqueScriptableObjects = EditorExtensions.FindAssetsByType<UniqueScriptableObject>();
            UniqueBehaviour[] allUniqueBehaviours =
                Object.FindObjectsByType<UniqueBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID);

            foreach (UniqueBehaviour obj in allUniqueBehaviours)
            {
                obj.Validate();
            }
            
            foreach (UniqueScriptableObject obj in allUniqueScriptableObjects)
            {
                obj.Validate();
            }
        }
    }
}