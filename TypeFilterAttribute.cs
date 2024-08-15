namespace LegendaryTools
{
    using System;
    using UnityEngine;
    
    public class TypeFilterAttribute : PropertyAttribute
    {
        public Type BaseType { get; private set; }

        public TypeFilterAttribute(Type baseType)
        {
            BaseType = baseType;
        }
    }
}