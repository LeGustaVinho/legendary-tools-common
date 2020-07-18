using System;
using System.Collections.Generic;
using UnityEngine;

namespace LegendaryTools.Graph
{
    public abstract class HierarchicalGraph<G, N>
        where G : HierarchicalGraph<G, N>
        where N : HierarchicalNode<G, N>
    {
        protected N startOrRootNode;

        public HierarchicalGraph()
        {
            ID = Guid.NewGuid();
        }

        public HierarchicalGraph(N parentNode) : this()
        {
            ParentNode = parentNode;

            if (parentNode != null)
            {
                parentNode.SubGraph = this as G;
            }
        }

        public Guid ID { get; protected set; }
        public N ParentNode { get; protected internal set; }

        public G[] GraphHierarchy => GetHierarchyFromGraph();

        public N StartOrRootNode
        {
            get => startOrRootNode;
            set
            {
                if (value == null)
                {
                    Debug.LogError("[HierarchicalGraph:StartOrRootNode] -> StartOrRootNode cannot be null.");
                }
                else
                {
                    startOrRootNode = value;
                }
            }
        }

        public G[] GetHierarchyFromGraph(G highLevelGraph = null)
        {
            List<G> path = new List<G>();
            path.Add(this as G);
            for (N parentNode = ParentNode; parentNode != null; parentNode = parentNode.Owner?.ParentNode)
            {
                if (parentNode.Owner != null)
                {
                    path.Add(parentNode.Owner);
                }

                if (highLevelGraph != null && parentNode.Owner == highLevelGraph)
                {
                    break;
                }
            }

            path.Reverse();
            return path.ToArray();
        }

        public override int GetHashCode()
        {
            return ID.GetHashCode();
        }

        public override bool Equals(object other)
        {
            if (!other.GetType().IsSameOrSubclass(typeof(HierarchicalGraph<G, N>)))
            {
                return false;
            }

            HierarchicalGraph<G, N> node = (HierarchicalGraph<G, N>) other;
            return ID == node.ID;
        }
    }
}