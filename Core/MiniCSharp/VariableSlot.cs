using System;

namespace LegendaryTools.MiniCSharp
{
    internal sealed class VariableSlot
    {
        public VariableSlot(Type type, object value)
        {
            Type = type ?? typeof(object);
            Value = value;
        }

        public Type Type { get; }

        public object Value { get; set; }
    }
}