using System;
using LegendaryTools.Graph;

namespace LegendaryTools
{
    [Serializable]
    public struct StateConnectionContext
    {
        public string Trigger;
    }

    [Serializable]
    public class StateConnection : NodeConnection<StateMachine, State, StateConnection, StateConnectionContext>
    {
        public StateConnection(string trigger,
            State from,
            State to,
            NodeConnectionDirection direction = NodeConnectionDirection.Bidirectional,
            float weight = 0) : base(from, to, direction, weight)
        {
            Context.Trigger = trigger;
        }

        public StateConnection(State from,
            State to,
            StateConnectionContext context,
            NodeConnectionDirection direction = NodeConnectionDirection.Bidirectional,
            float weight = 0) : base(from, to, direction, weight)
        {
            Context = context;
        }

        public string Trigger => Context.Trigger;

        public override string ToString()
        {
            return base.ToString() + ", Trigger " + Trigger;
        }
    }
}