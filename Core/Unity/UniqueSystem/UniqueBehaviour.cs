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
            if (gameObject.IsPrefab() && !gameObject.IsInScene())
            {
                Debug.LogWarning($"[UniqueBehaviour:AssignNewGuid] Only scene objects can have Guids.");
                return;
            }
            guid = UniqueObjectListing.AllocateNewGuidFor(this);
            Debug.Log($"Assigning new Guid to {gameObject.name} = {guid}");
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
            UniqueObjectListing.PrepareForValidate();
            Validate();
        }

        public virtual void Validate()
        {
#if UNITY_EDITOR
            if (gameObject.IsPrefab() && !gameObject.IsInScene())
            {
                if (!string.IsNullOrEmpty(guid))
                {
                    guid = string.Empty;
                    this.SetDirty();
                }
                return;
            }
#endif
            if (string.IsNullOrEmpty(guid))
            {
                AssignNewGuid();
            }
            else
            {
                if (UniqueObjectListing.UniqueObjects.TryGetValue(Guid, out IUnique uniqueObject))
                {
                    try
                    {
                        // Attempt to provoke object-destroyed exception
                        if (uniqueObject == null || uniqueObject.GameObject == null)
                        {
                            UniqueObjectListing.UniqueObjects.AddOrUpdate(Guid, this);
                        }
                        else
                        {
                            if (!ReferenceEquals(uniqueObject, this))
                                OnGuidCollisionDetected(uniqueObject);
                        }
                    }
                    catch (Exception ex)
                    {
                        //object destroyed exception: uniqueBehaviour.GameObject will be destroyed after AssemblyReload,EnterPlayMode and ExitingPlayMode
                        Debug.LogException(ex);
                        UniqueObjectListing.UniqueObjects.AddOrUpdate(Guid, this);
                    }
                }
                else
                {
                    UniqueObjectListing.UniqueObjects.Add(guid, this);
                }
            }
        }

        private void OnGuidCollisionDetected(IUnique uniqueBehaviour)
        {
            AssignNewGuid();
            Debug.LogWarning(
                $"[UniqueBehaviour:OnValidate] Guid {guid} collision detected with " +
                $"{gameObject.name} and {uniqueBehaviour.GameObject.name}, assigning new Guid."
            );
        }

        /// <summary>
        /// Remove this object's GUID from the dictionary upon destruction.
        /// </summary>
        protected virtual void OnDestroy()
        {
            if (!string.IsNullOrEmpty(guid) 
                && UniqueObjectListing.UniqueObjects.TryGetValue(guid, out IUnique uniqueObject) 
                && ReferenceEquals(uniqueObject, this))
            {
                UniqueObjectListing.UniqueObjects.Remove(guid);
            }
        }
    }
}