using System;
using System.Collections.Generic;
using LegendaryTools.TagSystem;
using UnityEngine;

namespace LegendaryTools.AttributeSystem
{
    [CreateAssetMenu(fileName = "EntityConfig", menuName = "Tools/AttributeSystem/EntityConfig")]
    public class EntityConfig : 
#if ODIN_INSPECTOR
        Sirenix.OdinInspector.SerializedScriptableObject
#else
        ScriptableObject
#endif
    {
        private const string CLONE = "(Clone)";

        public bool IsClone { get; private set; }

        public EntityData Data;

        /// <summary>
        /// Faz uma cópia/clonagem do próprio EntityConfig, inclusive clonando seus atributos.
        /// </summary>
        public virtual T Clone<T>(IEntity parent) where T : EntityConfig
        {
            T clone = CreateInstance<T>();
            clone.name = name + CLONE;
            clone.IsClone = true;
            clone.Data = Data.Clone(parent);

            return clone;
        }
    }
}