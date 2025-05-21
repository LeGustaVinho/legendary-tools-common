using System;

namespace LegendaryTools.StateMachineV2
{
    public class TriggerCondition<T> : Condition<T> where T : IEquatable<T>
    {
        public TriggerCondition(T name)
        {
            Name = name;
            Type = ParameterType.Trigger;
        }

        public override bool Evaluate(T name, ParameterState<T> parameterState)
        {
            return Name.Equals(name) && Convert.ToBoolean(parameterState.Value);
        }
    }
}