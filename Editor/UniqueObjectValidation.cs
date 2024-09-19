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
        }
        
        [UnityEditor.Callbacks.DidReloadScripts]
        private static void OnAfterAssemblyReload()
        {
            CheckUniqueObjects();
        }

        private static void CheckUniqueObjects()
        {
            List<UniqueScriptableObject> allUniqueScriptableObjects = EditorExtensions.FindAssetsByType<UniqueScriptableObject>();
            UniqueBehaviour[] allUniqueBehaviours =
                Object.FindObjectsByType<UniqueBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID);

            foreach (UniqueBehaviour obj in allUniqueBehaviours)
            {
                if (obj.gameObject.IsPrefab())
                {
                    if(!obj.gameObject.IsInScene())
                        continue;
                }

                if (string.IsNullOrEmpty(obj.Guid) || !UniqueObjectListing.UniqueObjects.ContainsKey(obj.Guid))
                {
                    obj.AssignNewGuid();
                }
            }
            
            foreach (UniqueScriptableObject obj in allUniqueScriptableObjects)
            {
                if (string.IsNullOrEmpty(obj.Guid) || !UniqueObjectListing.UniqueObjects.ContainsKey(obj.Guid))
                {
                    obj.AssignNewGuid();
                }
            }
        }
    }
}