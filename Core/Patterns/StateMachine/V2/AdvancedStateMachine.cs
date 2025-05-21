using System;
using System.Collections.Generic;
using LegendaryTools.GraphV2;
using UnityEngine;

namespace LegendaryTools.StateMachineV2
{
    public class AdvancedStateMachine<T> : GraphV2.Graph, IAdvancedStateMachine<T> where T : IEquatable<T>
    {
        public string Name { get; set; }
        public IAdvancedState<T> AnyState { get; }
        
        public bool IsRunning => CurrentState != null;
        public IAdvancedState<T> CurrentState { get; protected set; }
        IState IStateMachine<T>.CurrentState => CurrentState;
        public Dictionary<T, ParameterState<T>> ParameterValues { get; protected set; } = new Dictionary<T, ParameterState<T>>();
        
        public event Action<IStateMachine<T>> OnStart;
        public event Action<IStateMachine<T>> OnStop;
        public event Action<IState, IState> OnTransit;
        
        public AdvancedStateMachine(IAdvancedState<T> anyState, string name = "")
        {
            Name = name;
            AnyState = anyState;
        }
        
        public void Start(IAdvancedState<T> startState)
        {
            if (IsRunning) return;
            if (!Contains(startState)) throw new InvalidOperationException($"{nameof(startState)} must be a state inside of {Name} {nameof(AdvancedStateMachine<T>)}");
            Transit(null, startState, null);
            OnStart?.Invoke(this);
        }

        public void Start(IState startState)
        {
            if (startState is not IAdvancedState<T> startAdvancedState) 
                throw new InvalidOperationException($"{nameof(startState)} does not implements {nameof(IAdvancedState<T>)}");
            Start(startAdvancedState);
        }

        public void Stop()
        {
            if (!IsRunning) return;
            Transit(CurrentState, null, null);
            CurrentState = null;
            OnStop?.Invoke(this);
        }

        public void Update()
        {
            if(IsRunning) CurrentState.InvokeOnStateUpdate();
        }

        protected void Transit(IAdvancedState<T> fromState, IAdvancedState<T> toState, IAdvancedStateConnection<T> transition)
        {
            fromState?.InvokeOnStateExit();
            transition?.InvokeOnTransit();
            CurrentState = toState;
            toState?.InvokeOnStateEnter();
            OnTransit?.Invoke(fromState, toState);
        }
        
        public void AddParameter(T parameterName, ParameterType parameterType)
        {
            if (ParameterValues.ContainsKey(parameterName))
            {
                throw new InvalidOperationException($"{Name} {nameof(AdvancedStateMachine<T>)} already has {parameterName} parameter");
            }
            
            ParameterValues.Add(parameterName, new ParameterState<T>(parameterName, parameterType, 0));
        }

        public bool RemoveParameter(T parameterName, ParameterType parameterType)
        {
            if (!ParameterValues.ContainsKey(parameterName))
            {
                return false;
            }
            
            ParameterValues.Remove(parameterName);
            return true;
        }

        public void SetTrigger(T name)
        {
            if (!IsRunning) throw new InvalidOperationException($"{Name} {nameof(AdvancedStateMachine<T>)} is not running.");
            ValidateParam(name, this, ParameterType.Trigger, out ParameterState<T> parameterState);
            ParameterValues[name].Value = Convert.ToSingle(true);
            if (!CheckTriggerForState(CurrentState))
                CheckTriggerForState(AnyState);
        }

        public void SetBool(T name, bool value)
        {
            if (!IsRunning) throw new InvalidOperationException($"{Name} {nameof(AdvancedStateMachine<T>)} is not running.");
            ValidateParam(name, this, ParameterType.Bool, out ParameterState<T> parameterState);
            ParameterValues[name].Value = Convert.ToSingle(value);
            if (!CheckTriggerForState(CurrentState))
                CheckTriggerForState(AnyState);
        }

        public void SetInt(T name, int value)
        {
            if (!IsRunning) throw new InvalidOperationException($"{Name} {nameof(AdvancedStateMachine<T>)} is not running.");
            ValidateParam(name, this, ParameterType.Int, out ParameterState<T> parameterState);
            ParameterValues[name].Value = Convert.ToSingle(value);
            if (!CheckTriggerForState(CurrentState))
                CheckTriggerForState(AnyState);
        }

        public void SetFloat(T name, float value)
        {
            if (!IsRunning) throw new InvalidOperationException($"{Name} {nameof(AdvancedStateMachine<T>)} is not running.");
            ValidateParam(name, this, ParameterType.Float, out ParameterState<T> parameterState);
            ParameterValues[name].Value = Convert.ToSingle(value);
            if (!CheckTriggerForState(CurrentState))
                CheckTriggerForState(AnyState);
        }
        
        protected bool CheckTriggerForState(IAdvancedState<T> state)
        {
            List<(IAdvancedState<T>, IAdvancedStateConnection<T>)> availableTransitions = new List<(IAdvancedState<T>, IAdvancedStateConnection<T>)>();
            foreach (INodeConnection nodeConnection in state.OutboundConnections)
            {
                if (nodeConnection is not IAdvancedStateConnection<T> stateConnection)
                {
                    Debug.LogWarning($"[{nameof(AdvancedStateMachine<T>)}:{nameof(SetTrigger)}] NodeConnection does not implements {nameof(IAdvancedStateConnection<T>)} on {Name}");
                    continue;
                }

                if (!stateConnection.Evaluate(ParameterValues)) continue;
                INode toNode = stateConnection.GetOut(CurrentState);
                if (toNode is not IAdvancedState<T> toState)
                {
                    Debug.LogWarning($"[{nameof(AdvancedStateMachine<T>)}:{nameof(CheckTriggerForState)}] toNode does not implements {nameof(IAdvancedState<T>)} on {Name}");
                    continue;
                }
                availableTransitions.Add((toState, stateConnection));
            }

            if (availableTransitions.Count == 0) return false;
            if (availableTransitions.Count > 1)
            {
                Debug.LogWarning($"[{nameof(AdvancedStateMachine<T>)}:{nameof(CheckTriggerForState)}] Multiple transitions can be taken from State {state.Name} with current params, total {availableTransitions.Count}.");
            }
            availableTransitions.Sort((x, y) => x.Item2.Priority.CompareTo(y.Item2.Priority)); //Sort by priority ascending
            Transit(CurrentState, availableTransitions[0].Item1, availableTransitions[0].Item2); //IStateConnection with priority takes precedence
            availableTransitions[0].Item2.ConsumeTriggers(ParameterValues);
            return true;
        }
        
        internal static void ValidateParam(T name, IAdvancedStateMachine<T> rootStateMachine, ParameterType expectedDefinition, out ParameterState<T> parameterState)
        {
            if (!rootStateMachine.ParameterValues.TryGetValue(name, out parameterState)) 
                throw new InvalidOperationException($"{name} parameter does not exists in {rootStateMachine.Name} {nameof(AdvancedStateMachine<T>)}");
            if(parameterState.Type != expectedDefinition) 
                throw new InvalidOperationException($"You are trying to set a value to a different type, {name} is type {parameterState.Type} in {rootStateMachine.Name} {nameof(AdvancedStateMachine<T>)}");
        }
    }
}