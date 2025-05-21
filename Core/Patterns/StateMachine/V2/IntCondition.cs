using System;

namespace LegendaryTools.StateMachineV2
{
    public class IntCondition<T> : Condition<T> where T : IEquatable<T>
    {
        public IntParameterCondition Condition;
        public int Value;
        
        public IntCondition(T name)
        {
            Name = name;
            Type = ParameterType.Int;
        }
        
        public IntCondition(T name, IntParameterCondition condition, int value) : this(name)
        {
            Condition = condition;
            Value = value;
        }
        
        public override bool Evaluate(T name, ParameterState<T> parameterState)
        {
            if (!Name.Equals(name)) return false;
            return Condition switch
            {
                IntParameterCondition.Equals =>  Convert.ToInt32(parameterState.Value) == Value,
                IntParameterCondition.NotEquals => Convert.ToInt32(parameterState.Value) != Value,
                IntParameterCondition.Greater => parameterState.Value > Value,
                IntParameterCondition.Less => parameterState.Value < Value,
                _ => false
            };
        }
    }
}