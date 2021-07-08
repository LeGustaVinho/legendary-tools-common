using System;
using LegendaryTools.Graph;

namespace LegendaryTools
{
    [Serializable]
    public struct StateConnectionContext<TTrigger>
    {
        public TTrigger Trigger;
    }

    [Serializable]
    public class StateConnection<TState, TTrigger> : 
        NodeConnection<StateMachine<TState, TTrigger>, 
        State<TState, TTrigger>, 
        StateConnection<TState, TTrigger>, 
        StateConnectionContext<TTrigger>>
    {
        public TTrigger Trigger => Context.Trigger;
        
        public StateConnection(TTrigger trigger,
            State<TState, TTrigger> from,
            State<TState, TTrigger> to,
            NodeConnectionDirection direction = NodeConnectionDirection.Bidirectional,
            float weight = 0) : base(from, to, direction, weight)
        {
            Context.Trigger = trigger;
        }

        public StateConnection(State<TState, TTrigger> from,
            State<TState, TTrigger> to,
            StateConnectionContext<TTrigger> context,
            NodeConnectionDirection direction = NodeConnectionDirection.Bidirectional,
            float weight = 0) : base(from, to, direction, weight)
        {
            Context = context;
        }

        public override string ToString()
        {
            return base.ToString() + ", Trigger " + Trigger;
        }
    }
}