using System;
using System.Linq;
using UnityEngine;

namespace LegendaryTools
{
    public class NestedType : ScriptableObject, ISerializationCallbackReceiver
    {
        [SerializeField] private Guid id;
        [SerializeField] private string displayName;
        [SerializeField] private NestedType[] parents;
        [SerializeField] private NestedTypes container;

        /// <summary>
        /// Initializes a new instance of the <see cref="NestedType"/> class.
        /// </summary>
        private void Awake()
        {
            id = Guid.NewGuid();
        }

        /// <summary>
        /// Unique identifier for the NestedType.
        /// </summary>
        public Guid Id
        {
            get => id;
            set
            {
                if (id != value) id = value;
            }
        }

        /// <summary>
        /// Display name for the NestedType.
        /// </summary>
        public string DisplayName
        {
            get => displayName;
            set => displayName = value;
        }

        /// <summary>
        /// Array of parent NestedTypes.
        /// </summary>
        public NestedType[] Parents
        {
            get => parents;
            set
            {
                if (parents != value)
                {
                    // Validate for circular references
                    if (value != null && Container != null)
                    {
                        NestedType[] tempParents = parents;
                        parents = value;
                        if (Container.HasCircularDependencies(this))
                        {
                            parents = tempParents; // Revert if circular dependency detected
                            return;
                        }
                    }

                    parents = value;
                    // Invalidate cache in container
                    Container?.InvalidateCache();
                }
            }
        }

        /// <summary>
        /// Gets the container NestedTypes instance this NestedType belongs to.
        /// </summary>
        public NestedTypes Container
        {
            get => container;
            internal set => container = value;
        }

        /// <summary>
        /// Gets a value indicating whether this NestedType has no children.
        /// </summary>
        public bool IsLeafType
        {
            get
            {
                if (Container == null) return true;
                // During cache building, use childrenCache directly to avoid recursion
                if (Container.IsBuildingCache) return !Container.GetChildrenFromCache(this).Any();
                return !Container.GetChildren(this).Any();
            }
        }

        /// <summary>
        /// Gets a value indicating whether this NestedType has no parents.
        /// </summary>
        public bool IsRoot => parents == null || !parents.Any();

        public void OnBeforeSerialize()
        {
        }

        public void OnAfterDeserialize()
        {
            if (id == Guid.Empty) id = Guid.NewGuid();
        }
    }
}