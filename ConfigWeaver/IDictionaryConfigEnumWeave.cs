using System;
using System.Collections.Generic;
using UnityEngine;

namespace LegendaryTools
{
    public interface IDictionaryConfigEnumWeaver<E, C>
        where C : ScriptableObject
        where E : struct, Enum, IConvertible
    {
        public Dictionary<E, C> Configs { get; }
        public Dictionary<C, E> InvertedConfigs { get; }
    }
}