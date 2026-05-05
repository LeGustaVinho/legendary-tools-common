using System;

namespace LegendaryTools.MiniCSharp
{
    internal readonly struct RuntimeValue
    {
        public RuntimeValue(object value, Type type)
        {
            Value = value;
            Type = type ?? value?.GetType() ?? typeof(object);
        }

        public object Value { get; }

        public Type Type { get; }

        public static RuntimeValue From(object value)
        {
            return new RuntimeValue(value, value?.GetType() ?? typeof(object));
        }
    }
}