using System;
using System.Collections.Generic;
using LegendaryTools.Graph;
using UnityEngine;

namespace LegendaryTools
{
    [Serializable]
    public class State : LinkedNode<StateMachine, State, StateConnection, StateConnectionContext>
    {
        public readonly string Name;

        private readonly Dictionary<string, StateConnection> outboundConnectionsLookup =
            new Dictionary<string, StateConnection>();

        public State(string name, StateMachine owner = null) : base(owner)
        {
            Name = name;
            OnConnectionAdd += onConnectionAdd;
            OnConnectionRemove += onConnectionRemove;
        }

        internal State(string name, StateMachine owner, bool isAnyState) : this(name, owner)
        {
            IsAnyState = isAnyState;
        }

        public bool IsAnyState { get; protected set; }

        public event Action<object> OnStateEnterEvent;
        public event Action<object> OnStateUpdateEvent;
        public event Action<object> OnStateExitEvent;

        public StateConnection ConnectTo(State targetState, string triggerName,
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

            StateMachine[] targetStateMachineHierarchy = targetState.Owner.GraphHierarchy;
            StateMachine[] selfStateMachineHierarchy = Owner.GraphHierarchy;
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

            StateConnectionContext context;
            context.Trigger = triggerName;

            return base.ConnectTo(targetState, context, direction);
        }

        public StateMachine CreateSubStateMachine()
        {
            StateMachine newStateMachine = new StateMachine(Name, this);
            AddSubGraph(newStateMachine);

            return newStateMachine;
        }

        public StateConnection GetOutboundConnection(string trigger)
        {
            return outboundConnectionsLookup.ContainsKey(trigger) ? outboundConnectionsLookup[trigger] : null;
        }

        public State FindNearestAncestor(State relativeTo)
        {
            State[] relativeHierarchy = relativeTo.NodeHierarchy;
            State[] currentStateHierarchy = NodeHierarchy;

            State[] shortestHierarchy = relativeHierarchy.Length > currentStateHierarchy.Length
                ? currentStateHierarchy
                : relativeHierarchy;
            HashSet<State> longestHierarchy = new HashSet<State>(relativeHierarchy.Length > currentStateHierarchy.Length
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

        internal void invokeOnStateEnter(object arg)
        {
            OnStateEnter(arg);
        }

        internal void invokeOnStateUpdate(object arg)
        {
            OnStateUpdate(arg);
        }

        internal void invokeOnStateExit(object arg)
        {
            OnStateExit(arg);
        }

        private void onConnectionAdd(StateConnection stateConnection)
        {
            if (stateConnection.From == this)
            {
                outboundConnectionsLookup.Add(stateConnection.Trigger, stateConnection);
            }
        }

        private void onConnectionRemove(StateConnection stateConnection)
        {
            if (stateConnection.From == this)
            {
                outboundConnectionsLookup.Remove(stateConnection.Trigger);
            }
        }
    }
}