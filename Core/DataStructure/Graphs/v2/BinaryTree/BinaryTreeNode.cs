using System;
using System.Collections.Generic;
using System.Linq;

namespace LegendaryTools.GraphV2
{
    public class BinaryTreeNode : TreeNode, IBinaryTreeNode
    {
        public ITreeNode Left { get; private set; }
        public ITreeNode Right { get; private set; }

        /// <summary>
        ///     Connects this node as the left child of the specified parent.
        /// </summary>
        /// <param name="parent">The parent node.</param>
        /// <param name="weight">Optional weight for the connection.</param>
        /// <returns>The established node connection.</returns>
        public INodeConnection ConnectAsLeftChild(IBinaryTreeNode parent, float weight = 1.0f)
        {
            if (parent == null) throw new ArgumentNullException(nameof(parent));

            if (parent.Left != null)
                throw new InvalidOperationException("Parent already has a left child.");

            INodeConnection connection = ConnectToParent(parent);
            if (connection != null)
            {
                // Ensure bidirectional reference in the parent
                if (!(parent is BinaryTreeNode binaryParent))
                    throw new InvalidOperationException("Parent node must be of type BinaryTreeNode.");

                binaryParent.Left = this;
            }

            return connection;
        }

        /// <summary>
        ///     Connects this node as the right child of the specified parent.
        /// </summary>
        /// <param name="parent">The parent node.</param>
        /// <param name="weight">Optional weight for the connection.</param>
        /// <returns>The established node connection.</returns>
        public INodeConnection ConnectAsRightChild(IBinaryTreeNode parent, float weight = 1.0f)
        {
            if (parent == null) throw new ArgumentNullException(nameof(parent));

            if (parent.Right != null)
                throw new InvalidOperationException("Parent already has a right child.");

            INodeConnection connection = ConnectToParent(parent);
            if (connection != null)
            {
                // Ensure bidirectional reference in the parent
                if (!(parent is BinaryTreeNode binaryParent))
                    throw new InvalidOperationException("Parent node must be of type BinaryTreeNode.");

                binaryParent.Right = this;
            }

            return connection;
        }

        /// <summary>
        ///     Overrides the Neighbours property to include only left and right children and the parent.
        /// </summary>
        public override INode[] Neighbours
        {
            get
            {
                List<INode> neighbors = new List<INode>();
                if (ParentNode != null)
                    neighbors.Add(ParentNode);
                if (Left != null)
                    neighbors.Add(Left);
                if (Right != null)
                    neighbors.Add(Right);
                return neighbors.Distinct().ToArray();
            }
        }

        /// <summary>
        ///     Overrides the DisconnectFromParent method to clear left/right references in the parent.
        /// </summary>
        public override void DisconnectFromParent()
        {
            if (ParentNode != null)
            {
                INodeConnection connection =
                    Connections.FirstOrDefault(conn => conn.ToNode == this && conn.FromNode == ParentNode);
                if (connection != null)
                {
                    connection.Disconnect();

                    if (ParentNode is BinaryTreeNode binaryParent)
                    {
                        if (binaryParent.Left == this)
                            binaryParent.Left = null;
                        if (binaryParent.Right == this)
                            binaryParent.Right = null;
                    }

                    ParentNode.ChildNodes.Remove(this);
                    ParentNode = null;
                }

                Owner = null;
            }
        }
    }
}