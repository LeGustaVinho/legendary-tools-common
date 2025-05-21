using System;

namespace LegendaryTools.StateMachineV2
{
    public class ParameterState<T> where T : IEquatable<T>
    {
        public T Name;
        public ParameterType Type;
        public float Value;

        public ParameterState(T name, ParameterType type, float value)
        {
            Name = name;
            Type = type;
            Value = value;
        }
    }
}