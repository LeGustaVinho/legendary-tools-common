using System;

namespace LegendaryTools.GraphV2
{
    public interface IBinaryTree : ITree
    {
        void AddTreeNode(IBinaryTreeNode newNode, IBinaryTreeNode parentNode, float weight = 1); //Adds a node to the Binary Tree, validating the graph as Binary Tree
        bool RemoveBinaryTreeNode(IBinaryTreeNode node, out ITreeNode[] removedNodes);
        IBinaryTreeNode BinarySearch(Predicate<INode> predicate);
    }
}