using System;
using System.Collections.Generic;
using LegendaryTools.Graph;
using UnityEngine;

namespace LegendaryTools
{
    public struct StateLog
    {
        public readonly State State;
        public readonly StateConnection Connection;
        public readonly object Arg;

        public StateLog(State state, StateConnection connection, object arg)
        {
            State = state;
            Connection = connection;
            Arg = arg;
        }
    }

    [Serializable]
    public class StateMachine : LinkedGraph<StateMachine, State, StateConnection, StateConnectionContext>
    {
        private static readonly string ANY_STATE_DEFAULT_NAME = "Any State";

        public readonly State AnyState;
        private readonly List<StateLog> history = new List<StateLog>();

        private int current = -1;
        public string Name;

        public StateMachine(string name, State state = null) : base(state)
        {
            Name = name;
            AnyState = new State(ANY_STATE_DEFAULT_NAME, this, true);
        }

        public bool IsStarted => current != -1;

        public void Trigger(string triggerName, object arg = null)
        {
            if (!IsStarted)
            {
                Debug.Log("[StateMachine:Trigger] -> Was not started");
                return;
            }

            StateConnection trigger = history[current].State.GetOutboundConnection(triggerName);

            Trigger(trigger, arg);
        }

        public void Trigger(StateConnection trigger, object arg = null)
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

            State targetState = GetDestination(trigger, history[current].State);

            Transit(trigger, targetState, arg);
        }

        public void Transit(StateConnection trigger, State targetState, object arg)
        {
            transit(trigger, targetState, arg, true, true, true);
        }

        public void TransitTo(State targetState, object arg = null)
        {
            transit(null, targetState, arg, true, true, true);
        }

        public void DestroyState(State state)
        {
            StateConnection[] allConnections = state.AllConnections;
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

            State[] graphStateHierarchy = history[current].State.NodeHierarchy;
            Array.Reverse(graphStateHierarchy);
            for (int i = 0; i < graphStateHierarchy.Length; i++)
            {
                graphStateHierarchy[i].invokeOnStateEnter(param);
            }

            history.Clear();
            current = -1;
        }

        public void MoveBack()
        {
            tryMoveInHistory(current - 1);
        }

        public void MoveForward()
        {
            tryMoveInHistory(current + 1);
        }

        public override void Add(State newNode)
        {
            if (startOrRootNode != null && startOrRootNode.IsAnyState)
            {
                startOrRootNode = null;
            }

            base.Add(newNode);
        }

        private void tryMoveInHistory(int targetIndex)
        {
            if (targetIndex < 0 || targetIndex > history.Count - 1)
            {
                return;
            }

            transit(null, history[targetIndex].State, history[targetIndex].Arg, true, false, true);
            current = targetIndex;
        }

        public void Update(object arg = null)
        {
            if (!IsStarted)
            {
                return;
            }

            history[current].State.invokeOnStateUpdate(arg);
        }

        public override StateConnection CreateConnection(State from, State to, StateConnectionContext context,
            NodeConnectionDirection direction = NodeConnectionDirection.Bidirectional, float weight = 0)
        {
            return new StateConnection(from, to, context, direction, weight);
        }

        public override string ToString()
        {
            return base.ToString() + ", Name: " + Name;
        }

        private State GetDestination(string trigger, State currentState, out StateConnection stateConnection)
        {
            stateConnection = currentState.GetOutboundConnection(trigger);

            if (stateConnection != null)
            {
                return GetDestination(stateConnection, currentState);
            }

            Debug.Log("[StateMachine:GetDestination] -> Trigger name " + trigger + " not found.");
            return null;
        }

        private State GetDestination(StateConnection connection, State currentState)
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

        private void transit(StateConnection trigger, State targetState, object arg, bool callExit, bool modifyHistory,
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

            State nearestAncestor = current >= 0 && history.Count > 0
                ? history[current].State.FindNearestAncestor(targetState)
                : null;

            State[] graphStateHierarchy;
            if (callExit)
            {
                if (current >= 0 && history.Count > 0)
                {
                    graphStateHierarchy = history[current].State.GetHierarchyFromNode(nearestAncestor);
                    Array.Reverse(graphStateHierarchy);
                    for (int i = 0; i < graphStateHierarchy.Length; i++)
                    {
                        graphStateHierarchy[i].invokeOnStateExit(arg);
                    }
                }
            }

            if (modifyHistory)
            {
                if (current != history.Count - 1)
                {
                    history.RemoveRange(current + 1, history.Count - (current + 1));
                }

                history.Add(new StateLog(targetState, trigger, arg));
                current = history.Count - 1;
            }

            if (callEnter)
            {
                graphStateHierarchy = targetState.GetHierarchyFromNode(nearestAncestor);

                for (int i = 0; i < graphStateHierarchy.Length; i++)
                {
                    graphStateHierarchy[i].invokeOnStateEnter(arg);
                }
            }
        }
    }
}