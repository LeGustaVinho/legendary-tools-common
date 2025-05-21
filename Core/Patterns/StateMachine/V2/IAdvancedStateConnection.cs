using System;
using System.Collections.Generic;
using LegendaryTools.GraphV2;

namespace LegendaryTools.StateMachineV2
{
    public interface IAdvancedStateConnection<T> : INodeConnection, IComparable<IAdvancedStateConnection<T>> where T : IEquatable<T>
    {
        List<Condition<T>> Conditions { get; }
        ConditionOperation ConditionOperation { get; internal set; }
        string Name { get; set; }
        int Priority { get; internal set; }
        event Action OnTransit;
        void AddCondition(T name, FloatParameterCondition parameterCondition, float value);
        void AddCondition(T name, IntParameterCondition parameterCondition, int value);
        void AddCondition(T name, BoolParameterCondition parameterCondition);
        void AddCondition(T name);
        void RemoveCondition(Predicate<Condition<T>> predicate);
        bool Evaluate(Dictionary<T, ParameterState<T>> parametersState);
        void ConsumeTriggers(Dictionary<T, ParameterState<T>> parametersState);
        void OnTransited();
        internal void InvokeOnTransit();
    }
}