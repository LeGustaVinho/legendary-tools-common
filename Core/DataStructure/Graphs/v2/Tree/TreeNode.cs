using System;
using System.Collections.Generic;
using System.Linq;

namespace LegendaryTools.GraphV2
{
    public class TreeNode : Node, ITreeNode
    {
        public  ITreeNode ParentNode { get; protected set; }
        ITreeNode ITreeNode.ParentNode
        {
            get => ParentNode;
            set => ParentNode = value;
        }

        public List<ITreeNode> ChildNodes { get; protected set; }

        public TreeNode()
        {
            ChildNodes = new List<ITreeNode>();
        }

        public INodeConnection ConnectToParent(ITreeNode parent)
        {
            if (parent == null) throw new ArgumentNullException(nameof(parent));
            if (parent == this) throw new InvalidOperationException("A node cannot be its own parent.");
            if (IsAncestor(parent))
                throw new InvalidOperationException("Connecting to this parent would create a cycle.");

            // Disconnect from current parent if exists
            DisconnectFromParent();

            // Establish connection from parent to this node (Directed)
            INodeConnection connection = parent.ConnectTo(this, NodeConnectionDirection.Unidirectional);
            if (connection != null)
            {
                ParentNode = parent;
                parent.ChildNodes.Add(this);
            }

            return connection;
        }

        public virtual void DisconnectFromParent()
        {
            if (ParentNode != null)
            {
                INodeConnection connection =
                    Connections.FirstOrDefault(conn => conn.ToNode == this && conn.FromNode == ParentNode);
                if (connection != null)
                {
                    connection.Disconnect();
                    ParentNode.ChildNodes.Remove(this);
                    ParentNode = null;
                }
                Owner = null;
            }
        }

        private bool IsAncestor(ITreeNode potentialAncestor)
        {
            ITreeNode current = ParentNode;
            while (current != null)
            {
                if (current == potentialAncestor)
                    return true;
                current = current.ParentNode;
            }

            return false;
        }

        public override INode[] Neighbours
        {
            get
            {
                List<INode> neighbors = base.Neighbours.ToList();
                // For tree, only consider parent and children
                return neighbors.Where(n => ChildNodes.Contains(n as ITreeNode) || n == ParentNode).Distinct()
                    .ToArray();
            }
        }
    }
}