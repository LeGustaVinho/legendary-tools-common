using System;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace LegendaryTools
{
    public abstract class UniqueScriptableObject : UnityObject, IUnique
    {
#if ODIN_INSPECTOR 
        [Sirenix.OdinInspector.HorizontalGroup("Guid")]
#endif
        [SerializeField] private string guid;
        
        public virtual string Name
        {
            get
            {
                Validate();
                return name;
            }
        }

        public string Guid
        {
            get
            {
                Validate();
                return guid;
            }
        }

        public GameObject GameObject => null;
        public ScriptableObject ScriptableObject => this;
        public UniqueType Type => UniqueType.ScriptableObject;

        [ContextMenu("Assign New Guid")]
#if ODIN_INSPECTOR 
        [Sirenix.OdinInspector.HorizontalGroup("Guid", width: 150)]
        [Sirenix.OdinInspector.Button]
#endif
        public void AssignNewGuid()
        {
            guid = UniqueObjectListing.AllocateNewGuidFor(this);
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
        
        // Called by Unity
        public void Awake()
        {
            UniqueObjectListing.PrepareForValidate();
            Validate();
        }

        // Called by Unity
        public void OnEnable()
        {
            Validate();
        }
        
        public virtual void Validate()
        {
            if (string.IsNullOrEmpty(guid))
            {
                AssignNewGuid();
            }
            else
            {
                if (UniqueObjectListing.UniqueObjects.TryGetValue(guid, out IUnique uniqueObject))
                {
                    if (uniqueObject is UniqueScriptableObject existingSO && existingSO != this)
                        OnGuidCollisionDetected(uniqueObject);
                }
                else
                {
                    UniqueObjectListing.UniqueObjects.Add(guid, this);
                }
            }
        }
        
#if UNITY_EDITOR
        protected virtual void OnValidate()
        {
            Validate();
        }
#endif

        private void OnGuidCollisionDetected(IUnique uniqueScriptableObject)
        {
            Debug.Log($"[UniqueScriptableObject:OnValidate] Guid {guid} collision detected with " +
                      $"{name} and {uniqueScriptableObject.Name}, assigning new Guid.");
            AssignNewGuid();
        }

        /// <summary>
        /// For ScriptableObjects, Unity may invoke OnDisable when they’re unloaded.
        /// Remove the entry if it still points to this instance.
        /// </summary>
        protected virtual void OnDisable()
        {
            if (!string.IsNullOrEmpty(guid) 
                && UniqueObjectListing.UniqueObjects.TryGetValue(guid, out IUnique registered)
                && registered == this)
            {
                UniqueObjectListing.UniqueObjects.Remove(guid);
            }
        }
    }
}