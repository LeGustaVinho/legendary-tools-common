using System;
using System.Collections.Generic;
using UnityEngine;

namespace LegendaryTools.AttributeSystemV2
{
    /// <summary>
    /// ScriptableObject describing a type of entity (template/config).
    /// </summary>
    [CreateAssetMenu(menuName = "RPG/Entity Definition", fileName = "NewEntityDefinition")]
    public class EntityDefinition : ScriptableObject
    {
        [SerializeField]
        [Tooltip("Unique ID for this EntityDefinition. Auto-generated and kept unique in the project.")]
        private string _id;

        /// <summary>
        /// Unique ID for this entity definition.
        /// </summary>
        public string Id => _id;

        public string entityName;

        [Tooltip("Attributes that this entity starts with.")]
        public List<AttributeEntry> attributes = new();

#if UNITY_EDITOR
        /// <summary>
        /// Utility to reset all attributes to their default values from definitions.
        /// Editor-only helper.
        /// </summary>
        public void ResetAllAttributesToDefinitions()
        {
            foreach (AttributeEntry entry in attributes)
            {
                entry?.ResetToDefinitionDefault();
            }
        }

        private void OnValidate()
        {
            EnsureUniqueId();
        }

        private void Reset()
        {
            EnsureUniqueId();
        }

        private void EnsureUniqueId()
        {
            if (string.IsNullOrEmpty(_id))
            {
                _id = Guid.NewGuid().ToString("N");
                UnityEditor.EditorUtility.SetDirty(this);
            }

            string thisPath = UnityEditor.AssetDatabase.GetAssetPath(this);
            string[] guids = UnityEditor.AssetDatabase.FindAssets("t:" + nameof(EntityDefinition));

            foreach (string assetGuid in guids)
            {
                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(assetGuid);
                if (path == thisPath)
                    continue;

                EntityDefinition other = UnityEditor.AssetDatabase.LoadAssetAtPath<EntityDefinition>(path);
                if (other == null)
                    continue;

                if (other._id == _id && !string.IsNullOrEmpty(_id))
                {
                    _id = Guid.NewGuid().ToString("N");
                    UnityEditor.EditorUtility.SetDirty(this);
                    break;
                }
            }
        }
#endif
    }
}