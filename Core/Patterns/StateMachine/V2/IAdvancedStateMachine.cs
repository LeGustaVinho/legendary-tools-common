using System;
using System.Collections.Generic;
using LegendaryTools.GraphV2;

namespace LegendaryTools.StateMachineV2
{
    public interface IAdvancedStateMachine<T> : IGraph, IStateMachine<T> where T : IEquatable<T>
    {
        IAdvancedState<T> AnyState { get; }
        
        Dictionary<T, ParameterState<T>> ParameterValues { get; }

        void Start(IAdvancedState<T> startState);
        
        void AddParameter(T parameterName, ParameterType parameterType);
        bool RemoveParameter(T parameterName, ParameterType parameterType);
        
        void SetBool(T name, bool value);
        void SetInt(T name, int value);
        void SetFloat(T name, float value);
    }
}