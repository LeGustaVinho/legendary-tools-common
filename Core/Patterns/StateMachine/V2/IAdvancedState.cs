using System;
using LegendaryTools.GraphV2;

namespace LegendaryTools.StateMachineV2
{
    public interface IAdvancedState<T> : INode, IState where T : IEquatable<T>
    {
        IAdvancedStateConnection<T> ConnectTo(INode to, int priority, NodeConnectionDirection newDirection, 
            ConditionOperation conditionOperation = ConditionOperation.WhenAll);
    }
}