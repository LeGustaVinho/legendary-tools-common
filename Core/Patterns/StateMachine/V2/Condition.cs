using System;

namespace LegendaryTools.StateMachineV2
{
    public abstract class Condition<T> where T : IEquatable<T>
    {
        public T Name;
        public ParameterType Type { get; protected set; }

        public abstract bool Evaluate(T name, ParameterState<T> parameterState);
    }
}