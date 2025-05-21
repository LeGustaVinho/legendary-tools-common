using System;
using System.Collections.Generic;

namespace LegendaryTools.GraphV2
{
    public interface ITree : IGraph
    {
        ITreeNode RootNode { get; }
        public int Height { get; }
        public int Width { get; }
        void AddTreeNode(ITreeNode newNode, ITreeNode parentNode); //Adds a node to the tree, validating the graph remains directed acyclic tree
        bool RemoveTreeNode(ITreeNode node, out ITreeNode[] removedNodes);
        public ITreeNode DepthFirstSearch(Predicate<INode> predicate);
        public ITreeNode HeightFirstSearch(Predicate<INode> predicate);
        List<ITreeNode> DepthFirstTraverse();
        List<ITreeNode> HeightFirstTraverse();
    }
}