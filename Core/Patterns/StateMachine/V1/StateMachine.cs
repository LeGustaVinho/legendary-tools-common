using System;
using System.Collections.Generic;
using LegendaryTools.Graph;
using UnityEngine;

namespace LegendaryTools.StateMachine
{
    public struct StateLog<TState, TTrigger>
    {
        public readonly State<TState, TTrigger> State;
        public readonly StateConnection<TState, TTrigger> Connection;
        public readonly object Arg;

        public StateLog(State<TState, TTrigger> state, StateConnection<TState, TTrigger> connection, object arg)
        {
            State = state;
            Connection = connection;
            Arg = arg;
        }
    }

    [Serializable]
    public class StateMachine<TState, TTrigger> : LinkedGraph<StateMachine<TState, TTrigger>, 
        State<TState, TTrigger>, 
        StateConnection<TState, TTrigger>, 
        StateConnectionContext<TTrigger>>
    {
        public readonly State<TState, TTrigger> AnyState;
        private readonly List<StateLog<TState, TTrigger>> history = new List<StateLog<TState, TTrigger>>();

        private int current = -1;
        public string Name;

        public event Action<State<TState, TTrigger>, StateEventType, object> OnStateMachineTransit; 

        public StateMachine(string name, TState anyStateName, State<TState, TTrigger> state = null) : base(state)
        {
            Name = name;
            AnyState = new State<TState, TTrigger>(anyStateName, this, true);
        }

        public bool IsStarted => current != -1;

        public void Trigger(TTrigger triggerName, object arg = null)
        {
            if (!IsStarted)
            {
                Debug.Log("[StateMachine:Trigger] -> Was not started");
                return;
            }

            StateConnection<TState, TTrigger> trigger = history[current].State.GetOutboundConnection(triggerName);

            Trigger(trigger, arg);
        }

        public void Trigger(StateConnection<TState, TTrigger> trigger, object arg = null)
        {
            if (!IsStarted)
            {
                Debug.Log("[StateMachine:Trigger] -> Was not started");
                return;
            }

            if (trigger == null)
            {
                Debug.LogError("[StateMachine:Trigger()] -> Does not contain " + trigger.Trigger + " trigger.");
                return;
            }

            State<TState, TTrigger> targetState = GetDestination(trigger, history[current].State);

            Transit(trigger, targetState, arg);
        }

        public void Transit(StateConnection<TState, TTrigger> trigger, State<TState, TTrigger> targetState, object arg)
        {
            Transit(trigger, targetState, arg, true, true, true);
        }

        public void TransitTo(State<TState, TTrigger> targetState, object arg = null)
        {
            Transit(null, targetState, arg, true, true, true);
        }

        public void DestroyState(State<TState, TTrigger> state)
        {
            StateConnection<TState, TTrigger>[] allConnections = state.AllConnections;
            for (int i = 0; i < allConnections.Length; i++)
            {
                allConnections[i].Disconnect();
            }

            Remove(state);
        }

        public void Start(object param = null)
        {
            if (IsStarted)
            {
                Debug.LogError("[StateMachine:Start] -> State machine is already running.");
                return;
            }

            if (allNodes.Count == 0)
            {
                Debug.LogError("[StateMachine:Start] -> Unable to start because there are no states.");
                return;
            }

            Transit(null, StartOrRootNode, param);
        }

        public void Stop(object param = null)
        {
            if (!IsStarted)
            {
                Debug.LogError("[StateMachine:Start] -> State machine is not running.");
                return;
            }

            State<TState, TTrigger>[] graphStateHierarchy = history[current].State.NodeHierarchy;
            Array.Reverse(graphStateHierarchy);
            for (int i = 0; i < graphStateHierarchy.Length; i++)
            {
                graphStateHierarchy[i].InvokeOnStateEnter(param);
            }

            history.Clear();
            current = -1;
        }

        public void MoveBack()
        {
            TryMoveInHistory(current - 1);
        }

        public void MoveForward()
        {
            TryMoveInHistory(current + 1);
        }

        public override void Add(State<TState, TTrigger> newNode)
        {
            if (startOrRootNode != null && startOrRootNode.IsAnyState)
            {
                startOrRootNode = null;
            }

            base.Add(newNode);
        }

        private void TryMoveInHistory(int targetIndex)
        {
            if (targetIndex < 0 || targetIndex > history.Count - 1)
            {
                return;
            }

            Transit(null, history[targetIndex].State, history[targetIndex].Arg, true, false, true);
            current = targetIndex;
        }

        public void Update(object arg = null)
        {
            if (!IsStarted)
            {
                return;
            }

            history[current].State.InvokeOnStateUpdate(arg);
            OnStateMachineTransit?.Invoke(history[current].State, StateEventType.Enter, arg);
        }

        public override StateConnection<TState, TTrigger> CreateConnection(State<TState, TTrigger> from, 
            State<TState, TTrigger> to, 
            StateConnectionContext<TTrigger> context,
            NodeConnectionDirection direction = NodeConnectionDirection.Bidirectional, float weight = 0)
        {
            return new StateConnection<TState, TTrigger>(from, to, context, direction, weight);
        }

        public override string ToString()
        {
            return base.ToString() + ", Name: " + Name;
        }

        private State<TState, TTrigger> GetDestination(TTrigger trigger, 
            State<TState, TTrigger> currentState, 
            out StateConnection<TState, TTrigger> stateConnection)
        {
            stateConnection = currentState.GetOutboundConnection(trigger);

            if (stateConnection != null)
            {
                return GetDestination(stateConnection, currentState);
            }

            Debug.Log("[StateMachine:GetDestination] -> Trigger name " + trigger + " not found.");
            return null;
        }

        private State<TState, TTrigger> GetDestination(StateConnection<TState, TTrigger> connection, 
            State<TState, TTrigger> currentState)
        {
            switch (connection.Direction)
            {
                case NodeConnectionDirection.Unidirectional when connection.From == currentState:
                    return connection.To;
                case NodeConnectionDirection.Bidirectional
                    when connection.From == currentState || connection.To == currentState:
                    return connection.From == currentState ? connection.To : connection.From;
                default:
                    return null;
            }
        }

        private void Transit(StateConnection<TState, TTrigger> trigger, 
            State<TState, TTrigger> targetState, 
            object arg, 
            bool callExit, 
            bool modifyHistory,
            bool callEnter)
        {
            if (trigger != null)
            {
                if (history[current].State != trigger.From)
                {
                    Debug.LogError("[StateMachine:Transit()] -> Trigger from state is not current state.");
                    return;
                }
            }

            State<TState, TTrigger> nearestAncestor = current >= 0 && history.Count > 0
                ? history[current].State.FindNearestAncestor(targetState)
                : null;

            State<TState, TTrigger>[] graphStateHierarchy;
            if (callExit)
            {
                if (current >= 0 && history.Count > 0)
                {
                    graphStateHierarchy = history[current].State.GetHierarchyFromNode(nearestAncestor);
                    Array.Reverse(graphStateHierarchy);
                    for (int i = 0; i < graphStateHierarchy.Length; i++)
                    {
                        graphStateHierarchy[i].InvokeOnStateExit(arg);
                        OnStateMachineTransit?.Invoke(graphStateHierarchy[i], StateEventType.Exit, arg);
                    }
                }
            }

            if (modifyHistory)
            {
                if (current != history.Count - 1)
                {
                    history.RemoveRange(current + 1, history.Count - (current + 1));
                }

                history.Add(new StateLog<TState, TTrigger>(targetState, trigger, arg));
                current = history.Count - 1;
            }

            if (callEnter)
            {
                graphStateHierarchy = targetState.GetHierarchyFromNode(nearestAncestor);

                for (int i = 0; i < graphStateHierarchy.Length; i++)
                {
                    graphStateHierarchy[i].InvokeOnStateEnter(arg);
                    OnStateMachineTransit?.Invoke(graphStateHierarchy[i], StateEventType.Enter, arg);
                }
            }
        }
    }
}