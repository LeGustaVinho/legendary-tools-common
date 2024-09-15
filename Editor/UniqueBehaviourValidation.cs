using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace LegendaryTools.Editor
{
    [InitializeOnLoad]
    public static class UniqueBehaviourValidation
    {
        static UniqueBehaviourValidation()
        {
            EditorApplication.hierarchyChanged += EnsureUniqueGuids;
        }

        private static void EnsureUniqueGuids()
        {
            UniqueBehaviour[] allObjects =
                Object.FindObjectsByType<UniqueBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID);
            HashSet<string> existingGuids = new HashSet<string>();

            foreach (UniqueBehaviour obj in allObjects)
            {
                if (obj.gameObject.IsPrefab())
                {
                    if(!obj.gameObject.IsInScene())
                        continue;
                }

                if (string.IsNullOrEmpty(obj.Guid) || !existingGuids.Add(obj.Guid))
                {
                    obj.AssignNewGuid();
                }
            }
        }
    }
}