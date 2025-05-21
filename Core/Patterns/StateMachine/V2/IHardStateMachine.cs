using System;
using System.Collections.Generic;

namespace LegendaryTools.StateMachineV2
{
    public interface IHardStateMachine<T> : IStateMachine<T> where T : struct, Enum, IConvertible
    {
        new IHardState<T> CurrentState { get; }
        Dictionary<T, IHardState<T>> States { get; }
        void Start(IHardState<T> startState);
    }
}