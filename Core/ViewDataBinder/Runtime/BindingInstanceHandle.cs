using System;

namespace LegendaryTools.ViewBinding
{
    public readonly struct BindingInstanceHandle
    {
        public BindingInstanceHandle(object instance, Type type, bool isStatic)
        {
            Instance = instance;
            Type = type;
            IsStatic = isStatic;
        }

        public object Instance { get; }

        public Type Type { get; }

        public bool IsStatic { get; }

        public bool IsValid => Type != null && (IsStatic || Instance != null);
    }
}
