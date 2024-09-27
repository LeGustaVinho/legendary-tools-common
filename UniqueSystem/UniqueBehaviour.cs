using System;
using UnityEngine;

namespace LegendaryTools
{
    public class UniqueBehaviour : 
#if ODIN_INSPECTOR
        Sirenix.OdinInspector.SerializedMonoBehaviour, IUnique
#else
        MonoBehaviour, IUnique
#endif
    {
        [SerializeField] private string guid;
        public virtual string Name => gameObject != null ? gameObject.name : string.Empty;
        public string Guid => guid;
        
        [ContextMenu("Assign New Guid")]
#if ODIN_INSPECTOR 
        [Sirenix.OdinInspector.Button]
#endif
        public void AssignNewGuid()
        {
            guid = UniqueObjectListing.AllocateNewGuidFor(this);
            this.SetDirty();
        }

        public static bool TryGetValue(string guid, out UniqueBehaviour uniqueBehaviour)
        {
            bool result = UniqueObjectListing.UniqueObjects.TryGetValue(guid, out IUnique uniqueObject);
            uniqueBehaviour = uniqueObject as UniqueBehaviour;
            return result;
        }
        
        public static bool Contains(string guid)
        {
            return UniqueObjectListing.UniqueObjects.ContainsKey(guid);
        }
        
        protected virtual void Awake()
        {
            if (string.IsNullOrEmpty(guid) && !gameObject.IsPrefab())
            {
                AssignNewGuid();
            }

            if (UniqueObjectListing.UniqueObjects.TryGetValue(guid, out IUnique uniqueBehaviour))
            {
                if ((UniqueBehaviour)uniqueBehaviour != this)
                    OnGuidCollisionDetected(uniqueBehaviour);
            }
            else
                UniqueObjectListing.UniqueObjects.Add(guid, this);
        }
        
#if UNITY_EDITOR
        protected virtual void OnValidate()
        {
            bool allowAssignGuid = !UnityExtension.IsInPrefabMode();
            
            if (gameObject.IsPrefab() || this.IsPrefab())
            {
                allowAssignGuid = false;
            }
            
            if (string.IsNullOrEmpty(guid))
            {
                if(allowAssignGuid)
                    AssignNewGuid();
            }
            else
            {
                if (UniqueObjectListing.UniqueObjects.TryGetValue(guid, out IUnique uniqueBehaviour))
                {
                    //Unity does an assemblyReload leaving the Dictionary entries null
                    if (uniqueBehaviour == null)
                    {
                        UniqueBehaviour[] allUniqueBehaviours = FindObjectsByType<UniqueBehaviour>(FindObjectsInactive.Include,
                            FindObjectsSortMode.None);
                        foreach (UniqueBehaviour behaviour in allUniqueBehaviours)
                        {
                            UniqueObjectListing.UniqueObjects.AddOrUpdate(guid, behaviour);
                            if (guid == behaviour.Guid) uniqueBehaviour = behaviour;
                        }
                    }
                
                    if ((UniqueBehaviour)uniqueBehaviour != this)
                    {
                        if (allowAssignGuid)
                            OnGuidCollisionDetected(uniqueBehaviour);
                    }
                }
                else
                    UniqueObjectListing.UniqueObjects.Add(guid, this);
            }
        }

        private void OnGuidCollisionDetected(IUnique uniqueBehaviour)
        {
            Debug.Log($"[UniqueBehaviour:OnValidate] Guid {guid} collision detected with {gameObject.name} and {uniqueBehaviour.Name}, assigning new Guid.");
            AssignNewGuid();
        }
#endif
    }
}