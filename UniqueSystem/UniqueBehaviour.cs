using System;
using UnityEngine;

namespace LegendaryTools
{
    public class UniqueBehaviour : UnityBehaviour, IUnique
    {
#if ODIN_INSPECTOR 
        [Sirenix.OdinInspector.HorizontalGroup("Guid")]
#endif
        [SerializeField] private string guid;
        public virtual string Name => gameObject != null ? gameObject.name : string.Empty;
        public string Guid => guid;
        public GameObject GameObject => gameObject;
        public ScriptableObject ScriptableObject => null;
        public UniqueType Type => UniqueType.GameObject;

        [ContextMenu("Assign New Guid")]
#if ODIN_INSPECTOR 
        [Sirenix.OdinInspector.HorizontalGroup("Guid", width: 150)]
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
                try
                {
                    if (uniqueBehaviour == null || uniqueBehaviour.GameObject != null)
                        UniqueObjectListing.UniqueObjects.AddOrUpdate(Guid, this);
                    else
                    {
                        if ((UniqueBehaviour)uniqueBehaviour != this)
                            OnGuidCollisionDetected(uniqueBehaviour);
                    }
                }
                catch (Exception)
                {
                    UniqueObjectListing.UniqueObjects.AddOrUpdate(Guid, this);
                }
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
            AssignNewGuid();
            Debug.LogWarning($"[UniqueBehaviour:OnValidate] Guid {guid} collision detected with {gameObject.name} and another object, assigning new Guid.");
        }
#endif
    }
}