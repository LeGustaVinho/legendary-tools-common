using System;

namespace LegendaryTools.StateMachineV2
{
    public interface IState
    {
        public string Name { get; }
        public event Action<IState> OnStateEnter;
        public event Action<IState> OnStateUpdate;
        public event Action<IState> OnStateExit;

        internal void InvokeOnStateEnter();
        internal void InvokeOnStateUpdate();
        internal void InvokeOnStateExit();
    }
}