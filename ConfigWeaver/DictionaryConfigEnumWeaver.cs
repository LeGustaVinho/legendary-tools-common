using System;
using System.Collections.Generic;
#if ODIN_INSPECTOR
using Sirenix.OdinInspector;
#endif
using UnityEngine;

namespace LegendaryTools
{
#if ODIN_INSPECTOR
    public class DictionaryConfigEnumWeaver<E, C> : ConfigEnumWeaver<C>
        where C : ScriptableObject
        where E : struct, Enum, IConvertible
    {
        [ShowInInspector]
        public Dictionary<E, C> Configs = new Dictionary<E, C>();
        
        public Dictionary<C, E> InvertedConfigs = new Dictionary<C, E>();

        protected override void Populate(Dictionary<string, C> configMapping)
        {
            Configs.Clear();
            foreach (KeyValuePair<string, C> pair in configMapping)
            {
                E enumType = pair.Key.GetEnumValue<E>();
                Configs.Add(enumType, pair.Value);
                InvertedConfigs.AddOrUpdate(pair.Value, enumType);
            }
        }
    }
#endif
}