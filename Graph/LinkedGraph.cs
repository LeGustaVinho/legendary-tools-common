using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace LegendaryTools.Graph
{
    public interface IGraph<N>
    {
        N StartOrRootNode { get; set; }

        void Add(N newNode);

        bool Remove(N node);

        bool Contains(N node);

        N[] Neighbours(N node);
    }

    public abstract class LinkedGraph<G, N, NC, C> : HierarchicalGraph<G, N>, IGraph<N>, ICollection<N>
        where G : LinkedGraph<G, N, NC, C>
        where N : LinkedNode<G, N, NC, C>
        where NC : NodeConnection<G, N, NC, C>
    {
        protected readonly List<N> allNodes = new List<N>();

        public LinkedGraph()
        {
        }

        public LinkedGraph(N parentNode) : base(parentNode)
        {
        }

        public int Count => allNodes.Count;
        public bool IsReadOnly => false;

        public void Clear()
        {
            allNodes.Clear();
        }

        public void CopyTo(N[] array, int arrayIndex)
        {
            allNodes.CopyTo(array, arrayIndex);
        }

        public IEnumerator<N> GetEnumerator()
        {
            return allNodes.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public virtual void Add(N newNode)
        {
            if (!allNodes.Contains(newNode))
            {
                newNode.Owner = this as G;
                allNodes.Add(newNode);
                OnNodeAdd?.Invoke(newNode);

                if (StartOrRootNode == null)
                {
                    StartOrRootNode = newNode;
                }
            }
            else
            {
                Debug.LogError("[HierarchicalGraph:Add()] -> Already contains this node.");
            }
        }

        public virtual bool Remove(N node)
        {
            node.Owner = null;
            OnNodeRemove?.Invoke(node);
            return allNodes.Remove(node);
        }

        public bool Contains(N node)
        {
            return allNodes.Contains(node);
        }

        public N[] Neighbours(N node)
        {
            return node.Neighbours;
        }

        public event Action<N> OnNodeAdd;
        public event Action<N> OnNodeRemove;

        public abstract NC CreateConnection(N from, N to, C context,
            NodeConnectionDirection direction = NodeConnectionDirection.Bidirectional, float weight = 0);

        public N Find(Predicate<N> predicate)
        {
            return allNodes.Find(predicate);
        }

        public List<N> FindAll(Predicate<N> predicate)
        {
            return allNodes.FindAll(predicate);
        }

        public override string ToString()
        {
            return "LinkedGraph ID " + ID + ", node count " + allNodes.Count;
        }

        protected void invokeOnNodeAddEvent(N node)
        {
            OnNodeAdd?.Invoke(node);
        }

        protected void invokeOnNodeRemoveEvent(N node)
        {
            OnNodeRemove?.Invoke(node);
        }
    }
}