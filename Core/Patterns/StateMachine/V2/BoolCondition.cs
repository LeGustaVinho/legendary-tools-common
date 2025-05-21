using System;

namespace LegendaryTools.StateMachineV2
{
    public class BoolCondition<T> : Condition<T> where T : IEquatable<T>
    {
        public BoolParameterCondition Condition;
        
        public BoolCondition(T name)
        {
            Name = name;
            Type = ParameterType.Bool;
        }
        
        public BoolCondition(T name, BoolParameterCondition condition) : this(name)
        {
            Condition = condition;
        }
        
        public override bool Evaluate(T name, ParameterState<T> parameterState)
        {
            if (!Name.Equals(name)) return false;
            return Condition switch
            {
                BoolParameterCondition.True => Convert.ToBoolean(parameterState.Value),
                BoolParameterCondition.False => !Convert.ToBoolean(parameterState.Value),
                _ => false
            };
        }
    }
}