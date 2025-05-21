using System;
using UnityEngine;

namespace LegendaryTools
{
    public class TypeFilterAttribute : PropertyAttribute
    {
        public Type BaseType { get; private set; }

        public TypeFilterAttribute(Type baseType)
        {
            BaseType = baseType;
        }
    }
}