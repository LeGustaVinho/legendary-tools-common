using System;
using System.Collections.Generic;
using LegendaryTools.Graph;
using UnityEngine;

namespace LegendaryTools.StateMachine
{
    public enum StateEventType
    {
        Enter,
        Update,
        Exit,
    }
    
    [Serializable]
    public class State<TState,TTrigger> : LinkedNode<StateMachine<TState, TTrigger>, 
        State<TState,TTrigger>, 
        StateConnection<TState,TTrigger>, 
        StateConnectionContext<TTrigger>>
    {
        public readonly TState Name;

        private readonly Dictionary<TTrigger, StateConnection<TState,TTrigger>> outboundConnectionsLookup =
            new Dictionary<TTrigger, StateConnection<TState,TTrigger>>();

        public State(TState name, StateMachine<TState, TTrigger> owner = null) : base(owner)
        {
            Name = name;
            OnConnectionAdd += OnAddStateConnection;
            OnConnectionRemove += OnRemoveStateConnection;
        }

        internal State(TState name, StateMachine<TState, TTrigger> owner, bool isAnyState) : this(name, owner)
        {
            IsAnyState = isAnyState;
        }

        public bool IsAnyState { get; protected set; }

        public event Action<object> OnStateEnterEvent;
        public event Action<object> OnStateUpdateEvent;
        public event Action<object> OnStateExitEvent;

        public StateConnection<TState,TTrigger> ConnectTo(State<TState,TTrigger> targetState, 
            TTrigger triggerName,
            NodeConnectionDirection direction = NodeConnectionDirection.Bidirectional)
        {
            if (targetState.HasSubGraph)
            {
                Debug.Log("[State:ConnectTo] -> You cant connect to a state with sub graph");
                return null;
            }

            if (targetState.IsAnyState)
            {
                Debug.Log("[State:ConnectTo] -> You cant connect to a Any State");
                return null;
            }

            StateMachine<TState, TTrigger>[] targetStateMachineHierarchy = targetState.Owner.GraphHierarchy;
            StateMachine<TState, TTrigger>[] selfStateMachineHierarchy = Owner.GraphHierarchy;
            if (targetStateMachineHierarchy != null &&
                selfStateMachineHierarchy != null &&
                targetStateMachineHierarchy.Length > 0
                && selfStateMachineHierarchy.Length > 0)
            {
                if (targetStateMachineHierarchy[0] != selfStateMachineHierarchy[0])
                {
                    Debug.Log(
                        "[State:ConnectTo] -> A state cannot connect to another state that is not in the same state machine family.");
                    return null;
                }
            }
            else
            {
                Debug.Log("[State:ConnectTo] -> Unable to verify that states are from the same family.");
                return null;
            }

            if (outboundConnectionsLookup.ContainsKey(triggerName))
            {
                Debug.Log(
                    "[State:ConnectTo] -> A state cannot have multiple outbound transitions with the same trigger.");
                return null;
            }

            if (targetState.outboundConnectionsLookup.ContainsKey(triggerName))
            {
                if (targetState.outboundConnectionsLookup[triggerName].To == this)
                {
                    if (targetState.outboundConnectionsLookup[triggerName].Direction ==
                        NodeConnectionDirection.Bidirectional)
                    {
                        Debug.Log(
                            "[State:ConnectTo] -> Trigger is already registered as outbound transition and as inbound transition.");
                        return null;
                    }

                    targetState.outboundConnectionsLookup[triggerName].Direction =
                        NodeConnectionDirection.Bidirectional;
                    return targetState.outboundConnectionsLookup[triggerName];
                }
            }

            StateConnectionContext<TTrigger> context;
            context.Trigger = triggerName;

            return base.ConnectTo(targetState, context, direction);
        }

        public StateMachine<TState, TTrigger> CreateSubStateMachine()
        {
            
            StateMachine<TState, TTrigger> newStateMachine = new StateMachine<TState, TTrigger>(Owner.Name, Owner.AnyState.Name,this);
            AddSubGraph(newStateMachine);

            return newStateMachine;
        }

        public StateConnection<TState, TTrigger> GetOutboundConnection(TTrigger trigger)
        {
            return outboundConnectionsLookup.ContainsKey(trigger) ? outboundConnectionsLookup[trigger] : null;
        }

        public State<TState, TTrigger> FindNearestAncestor(State<TState, TTrigger> relativeTo)
        {
            State<TState, TTrigger>[] relativeHierarchy = relativeTo.NodeHierarchy;
            State<TState, TTrigger>[] currentStateHierarchy = NodeHierarchy;

            State<TState, TTrigger>[] shortestHierarchy = relativeHierarchy.Length > currentStateHierarchy.Length
                ? currentStateHierarchy
                : relativeHierarchy;
            HashSet<State<TState, TTrigger>> longestHierarchy = new HashSet<State<TState, TTrigger>>(relativeHierarchy.Length > currentStateHierarchy.Length
                ? relativeHierarchy
                : currentStateHierarchy);

            for (int i = shortestHierarchy.Length - 1; i >= 0; i--)
            {
                if (longestHierarchy.Contains(shortestHierarchy[i]))
                {
                    return shortestHierarchy[i];
                }
            }

            return null;
        }

        public override string ToString()
        {
            return base.ToString() + ", Name " + Name;
        }

        protected virtual void OnStateEnter(object arg)
        {
        }

        protected virtual void OnStateUpdate(object arg)
        {
        }

        protected virtual void OnStateExit(object arg)
        {
        }

        internal void InvokeOnStateEnter(object arg)
        {
            OnStateEnter(arg);
            OnStateEnterEvent?.Invoke(arg);
        }

        internal void InvokeOnStateUpdate(object arg)
        {
            OnStateUpdate(arg);
            OnStateUpdateEvent?.Invoke(arg);
        }

        internal void InvokeOnStateExit(object arg)
        {
            OnStateExit(arg);
            OnStateExitEvent?.Invoke(arg);
        }

        private void OnAddStateConnection(StateConnection<TState, TTrigger> stateConnection)
        {
            if (stateConnection.From == this)
            {
                outboundConnectionsLookup.Add(stateConnection.Trigger, stateConnection);
            }
        }

        private void OnRemoveStateConnection(StateConnection<TState, TTrigger> stateConnection)
        {
            if (stateConnection.From == this)
            {
                outboundConnectionsLookup.Remove(stateConnection.Trigger);
            }
        }
    }
}