using System;
using System.Collections.Generic;

namespace LegendaryTools.Graph
{
    public class HierarchicalNode<G, N>
        where G : HierarchicalGraph<G, N>
        where N : HierarchicalNode<G, N>
    {
        protected HierarchicalNode()
        {
            ID = Guid.NewGuid();
        }

        public HierarchicalNode(G owner) : this()
        {
            Owner = owner;
        }

        public Guid ID { get; protected set; }
        public G SubGraph { get; protected internal set; }

        public G Owner { get; protected internal set; }

        public bool HasSubGraph => SubGraph != null;

        public N[] NodeHierarchy => GetHierarchyFromNode();

        public void AddSubGraph(G subGraph)
        {
            SubGraph = subGraph;
            SubGraph.ParentNode = this as N;
        }

        public void RemoveSubGraph()
        {
            if (SubGraph == null)
            {
                return;
            }

            SubGraph.ParentNode = null;
            SubGraph = null;
        }

        public N[] GetHierarchyFromNode(N highLevelNode = null)
        {
            List<N> path = new List<N>();
            path.Add(this as N);
            for (N parentNode = Owner.ParentNode; parentNode != null; parentNode = parentNode.Owner?.ParentNode)
            {
                if (highLevelNode != null && parentNode == highLevelNode)
                {
                    break;
                }

                if (parentNode != null)
                {
                    path.Add(parentNode);
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
            if (!other.GetType().IsSameOrSubclass(typeof(HierarchicalNode<G, N>)))
            {
                return false;
            }

            HierarchicalNode<G, N> node = (HierarchicalNode<G, N>) other;
            return ID == node.ID;
        }
    }
}