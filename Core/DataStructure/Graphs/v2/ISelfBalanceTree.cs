using System;
using System.Collections.Generic;

namespace LegendaryTools.GraphV2
{
    public interface ISelfBalanceTree<T> : ITree
        where T : IComparable<T>
    {
        public IComparer<T> OverrideComparer { get; set; }
        void AddSelfBalanceTreeNode(ISelfBalanceTreeNode<T> newNode); // Insert a new node in the B-tree
        bool RemoveSelfBalanceTreeNode(ISelfBalanceTreeNode<T> node, out ISelfBalanceTreeNode<T>[] removedNodes); // Remove a node from the B-tree
    }
}