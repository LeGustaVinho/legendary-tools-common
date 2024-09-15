using UnityEngine;
using System;

namespace LegendaryTools
{
    [Serializable]
    public class SerializableType
    {
        [SerializeField] private string typeName;

        public Type Type
        {
            get => TypeExtension.FindType(typeName);
            set => typeName = value.AssemblyQualifiedName;
        }

        public SerializableType(Type type)
        {
            Type = type;
        }
    }
}