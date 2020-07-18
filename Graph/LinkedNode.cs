using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace LegendaryTools.Graph
{
    public interface INode<N>
    {
        N[] Neighbours { get; }

        int Count { get; }
    }

    public abstract class LinkedNode<G, N, NC, C> : HierarchicalNode<G, N>, INode<N>, IEnumerable<NC>
        where G : LinkedGraph<G, N, NC, C>
        where N : LinkedNode<G, N, NC, C>
        where NC : NodeConnection<G, N, NC, C>
    {
        protected readonly List<NC> Connections = new List<NC>();

        public LinkedNode(G owner = null) : base(owner)
        {
            owner?.Add(this as N);
        }

        public NC[] AllConnections => Connections.ToArray();

        public NC[] OutboundConnections
        {
            get
            {
                return Connections.FindAll(item =>
                    item.From == this || item.Direction == NodeConnectionDirection.Bidirectional).ToArray();
            }
        }

        public NC[] InboundConnections
        {
            get
            {
                return Connections
                    .FindAll(item => item.To == this || item.Direction == NodeConnectionDirection.Bidirectional)
                    .ToArray();
            }
        }

        public IEnumerator<NC> GetEnumerator()
        {
            return Connections.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public virtual N[] Neighbours
        {
            get
            {
                List<N> neighbours = new List<N>();

                for (int i = 0; i < Connections.Count; i++)
                {
                    neighbours.Add(Connections[i].From == this ? Connections[i].To : Connections[i].From);
                }

                return neighbours.ToArray();
            }
        }

        public int Count => Connections.Count;

        public event Action<NC> OnConnectionAdd;
        public event Action<NC> OnConnectionRemove;

        public NC ConnectTo(N to, C context, NodeConnectionDirection direction = NodeConnectionDirection.Bidirectional,
            float weight = 0)
        {
            if (to == null)
            {
                Debug.LogError("[LinkedNode:ConnectTo()] -> Target node cannot be null.");
                return null;
            }

            if (to == this)
            {
                Debug.LogError("[LinkedNode:ConnectTo()] -> You cant connect to yourself.");
                return null;
            }

            NC newConnection = Owner.CreateConnection(this as N, to, context, direction, weight);
            Connections.Add(newConnection);
            to.Connections.Add(newConnection);
            OnConnectionAdd?.Invoke(newConnection);
            return newConnection;
        }

        public bool RemoveConnection(NC nodeConnection)
        {
            OnConnectionRemove?.Invoke(nodeConnection);
            return Connections.Remove(nodeConnection);
        }

        public NC[] GetConnections(N node)
        {
            return Connections.FindAll(item => item.From == node || item.To == node).ToArray();
        }

        public NC GetConnectionTo(N node)
        {
            return Connections.Find(item => item.To == node);
        }

        public NC FindConnection(Predicate<NC> predicate)
        {
            return Connections.Find(predicate);
        }

        public override string ToString()
        {
            return "Node ID: " + ID + ", Connections: " + Connections.Count;
        }
    }
}