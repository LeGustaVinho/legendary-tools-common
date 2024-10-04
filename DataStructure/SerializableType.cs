using UnityEngine;
using System;

namespace LegendaryTools
{
    public abstract class BaseSerializableType
    {
        public abstract Type Type { get; set; }

        public override int GetHashCode()
        {
            return Type.GetHashCode();
        }
        
        public static implicit operator Type(BaseSerializableType baseSerializableType)
        {
            return baseSerializableType.Type;
        }
    }
    
    [Serializable]
    public class SerializableType : BaseSerializableType
    {
        [SerializeField] private string typeName;

        public override Type Type
        {
            get => TypeExtension.FindType(typeName);
            set => typeName = value.AssemblyQualifiedName;
        }

        public SerializableType(Type type)
        {
            Type = type;
        }
        
        public static implicit operator SerializableType(Type systemType)
        {
            return new SerializableType(systemType);
        }
    }
}