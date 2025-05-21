using System;

namespace LegendaryTools.StateMachineV2
{
    public interface IHardState<T> : IState where T : struct, Enum, IConvertible
    {
        T Type { get; }
    }
}