using System;
using System.Collections.Generic;

namespace LegendaryTools.StateMachineV2
{
    public class HardStateMachine<T> : IHardStateMachine<T> where T : struct, Enum, IConvertible
    {
        public string Name => typeof(T).Name;
        
        public bool IsRunning => CurrentState != null;
        string IStateMachine<T>.Name { get; set; }
        public IHardState<T> CurrentState { get; protected set; }
        IState IStateMachine<T>.CurrentState => CurrentState;
        public Dictionary<T, IHardState<T>> States { get; }

        protected readonly Func<IHardState<T>, IHardState<T>, bool> allowTransition;
        
        public event Action<IStateMachine<T>> OnStart;
        public event Action<IStateMachine<T>> OnStop;
        public event Action<IState, IState> OnTransit;
        
        public HardStateMachine(Func<IHardState<T>, IHardState<T>, bool> allowTransition = null)
        {
            this.allowTransition = allowTransition;
            States = new Dictionary<T, IHardState<T>>();
            string[] enumNames = typeof(T).GetEnumNames();
            foreach (string enumName in enumNames)
            {
                T enumValue = enumName.GetEnumValue<T>();
                States.Add(enumValue, new HardState<T>(enumValue));
            }
        }
        
        public void Start(IHardState<T> startState)
        {
            if(IsRunning) return;
            if (!States.TryGetValue(startState.Type, out IHardState<T> state)) return;
            CurrentState = startState;
            Transit(null, startState);
            OnStart?.Invoke(this);
        }

        public void Start(IState startState)
        {
            if (startState is not IHardState<T> startHardState) return;
            Start(startHardState);
        }

        public void Stop()
        {
            if(!IsRunning) return;
            Transit(CurrentState, null);
            CurrentState = null;
            OnStop?.Invoke(this);
        }

        public void Update()
        {
            if(!IsRunning) return;
            CurrentState.InvokeOnStateUpdate();
        }

        public void SetTrigger(T trigger)
        {
            if(!IsRunning) return;
            if (!States.TryGetValue(trigger, out IHardState<T> toState)) return;
            Transit(CurrentState, toState);
        }

        protected void Transit(IHardState<T> fromState, IHardState<T> toState)
        {
            if (allowTransition != null && !allowTransition.Invoke(fromState, toState)) return;
            fromState?.InvokeOnStateExit();
            CurrentState = toState;
            toState?.InvokeOnStateEnter();
            OnTransit?.Invoke(fromState, toState);
        }
    }
}