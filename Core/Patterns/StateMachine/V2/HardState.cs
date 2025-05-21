using System;

namespace LegendaryTools.StateMachineV2
{
    public class HardState<T> : IHardState<T> where T : struct, Enum, IConvertible
    {
        public string Name => Type.ToString();
        
        public T Type { get; }
        
        public event Action<IState> OnStateEnter;
        public event Action<IState> OnStateUpdate;
        public event Action<IState> OnStateExit;
        
        void IState.InvokeOnStateEnter()
        {
            OnStateEnter?.Invoke(this);
        }

        void IState.InvokeOnStateUpdate()
        {
            OnStateUpdate?.Invoke(this);
        }

        void IState.InvokeOnStateExit()
        {
            OnStateExit?.Invoke(this);
        }

        public HardState(T type)
        {
            Type = type;
        }
    }
}