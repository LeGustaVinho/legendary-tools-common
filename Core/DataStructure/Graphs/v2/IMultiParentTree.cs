using System;
using System.Collections.Generic;

namespace LegendaryTools.GraphV2
{
    public interface IMultiParentTree : IGraph
    {
        IMultiParentTreeNode RootNode { get; }
        public int Height { get; }
        public int Width { get; }
        void AddTreeNode(IMultiParentTreeNode newNode, IMultiParentTreeNode parentNode);
        bool RemoveTreeNode(IMultiParentTreeNode node, out IMultiParentTreeNode[] removedNodes);
        public IMultiParentTreeNode DepthFirstSearch(Predicate<INode> predicate);
        public IMultiParentTreeNode HeightFirstSearch(Predicate<INode> predicate);
        List<IMultiParentTreeNode> DepthFirstTraverse();
        List<IMultiParentTreeNode> HeightFirstTraverse();
    }
}