using UnityEngine;

namespace LegendaryTools
{
    public abstract class UniqueScriptableObject : 
#if ODIN_INSPECTOR
        Sirenix.OdinInspector.SerializedScriptableObject, IUnique
#else
        ScriptableObject, IUnique
#endif
    {
        [SerializeField] private string guid;
        public string Name => name;
        public string Guid => guid;
        
        [ContextMenu("Assign New Guid")]
#if ODIN_INSPECTOR 
        [Sirenix.OdinInspector.Button]
#endif
        public void AssignNewGuid()
        {
            string newGuid;
            do
            {
                newGuid = System.Guid.NewGuid().ToString();
            } while (UniqueObjectListing.UniqueObjects.ContainsKey(newGuid));
            
            guid = newGuid;
            UniqueObjectListing.UniqueObjects.Add(guid, this);
            UnityExtension.SetDirty(this);
        }

        public static bool TryGetValue(string guid, out UniqueScriptableObject uniqueScriptableObject)
        {
            bool result = UniqueObjectListing.UniqueObjects.TryGetValue(guid, out IUnique uniqueObject);
            uniqueScriptableObject = uniqueObject as UniqueScriptableObject;
            return result;
        }
        
        public static bool Contains(string guid)
        {
            return UniqueObjectListing.UniqueObjects.ContainsKey(guid);
        }
        
#if UNITY_EDITOR
        protected virtual void OnValidate()
        {
            bool allowAssignGuid = !UnityExtension.IsInPrefabMode();
            
            if (string.IsNullOrEmpty(guid))
            {
                if(allowAssignGuid)
                    AssignNewGuid();
            }
            else
            {
                if (UniqueObjectListing.UniqueObjects.TryGetValue(guid, out IUnique uniqueBehaviour))
                {
                    if ((UniqueScriptableObject)uniqueBehaviour != this)
                    {
                        if (allowAssignGuid)
                            OnGuidCollisionDetected(uniqueBehaviour);
                    }
                }
                else
                    UniqueObjectListing.UniqueObjects.Add(guid, this);
            }
        }

        private void OnGuidCollisionDetected(IUnique uniqueScriptableObject)
        {
            Debug.Log($"[UniqueScriptableObject:OnValidate] Guid {guid} collision detected with {name} and {uniqueScriptableObject.Name}, assigning new Guid.");
            AssignNewGuid();
        }
#endif
    }
}