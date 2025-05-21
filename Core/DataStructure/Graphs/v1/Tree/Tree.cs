using System;
using System.Collections.Generic;

namespace LegendaryTools.Graph
{
    public class Tree<G, N> : HierarchicalGraph<G, N>
        where G : Tree<G, N>
        where N : Branch<G, N>
    {
        public Tree(N rootNode)
        {
            rootNode.Owner = this as G;
            StartOrRootNode = rootNode;
            rootNode.SetParent(null);
        }

        public Tree(N rootNode, N parentNode) : base(parentNode)
        {
            rootNode.Owner = this as G;
            StartOrRootNode = rootNode;
            rootNode.SetParent(null);
        }

        public N Find(Predicate<N> match)
        {
            return StartOrRootNode.Find(match);
        }
        
        public List<N> FindAll(Predicate<N> match)
        {
            return StartOrRootNode.FindAll(match);
        }

        public IEnumerator<N> GetAllChildrenEnumerator()
        {
            return StartOrRootNode.GetAllChildrenEnumerator();
        }
    }
}