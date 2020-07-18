using System;

namespace LegendaryTools.Graph
{
    public enum NodeConnectionDirection
    {
        Unidirectional, //Graph can move From -> To
        Bidirectional //Graph can move From <-> To
    }

    public class NodeConnection<G, N, NC, C>
        where G : LinkedGraph<G, N, NC, C>
        where N : LinkedNode<G, N, NC, C>
        where NC : NodeConnection<G, N, NC, C>
    {
        public C Context;
        public NodeConnectionDirection Direction = NodeConnectionDirection.Bidirectional;
        public float Weight;

        public NodeConnection(N from, N to, NodeConnectionDirection direction = NodeConnectionDirection.Bidirectional,
            float weight = 0)
        {
            ID = Guid.NewGuid();
            From = from;
            To = to;
            Direction = direction;
            Weight = weight;
        }

        public NodeConnection(N from, N to, C context,
            NodeConnectionDirection direction = NodeConnectionDirection.Bidirectional, float weight = 0) : this(from,
            to, direction, weight)
        {
            Context = context;
        }

        public Guid ID { get; protected set; }
        public N From { get; protected set; }
        public N To { get; protected set; }

        public void Disconnect()
        {
            From.RemoveConnection(this as NC);
            To.RemoveConnection(this as NC);
        }

        public override string ToString()
        {
            return "NodeConnection, from " + From.ID + ", to " + To.ID + ", direction: " + Direction;
        }
    }
}