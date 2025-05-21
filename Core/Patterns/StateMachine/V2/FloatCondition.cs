using System;

namespace LegendaryTools.StateMachineV2
{
    public class FloatCondition<T> : Condition<T> where T : IEquatable<T>
    {
        public FloatParameterCondition Condition;
        public float Value;

        public FloatCondition(T name)
        {
            Name = name;
            Type = ParameterType.Float;
        }

        public FloatCondition(T name, FloatParameterCondition condition, float value) : this(name)
        {
            Condition = condition;
            Value = value;
        }

        public override bool Evaluate(T name, ParameterState<T> parameterState)
        {
            if (!Name.Equals(name)) return false;
            return Condition switch
            {
                FloatParameterCondition.Greater => parameterState.Value > Value,
                FloatParameterCondition.Less => parameterState.Value < Value,
                _ => false
            };
        }
    }
}