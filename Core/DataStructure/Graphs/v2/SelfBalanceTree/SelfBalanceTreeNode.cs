using System;
using System.Collections.Generic;

namespace LegendaryTools.GraphV2
{
    public class SelfBalanceTreeNode<T> : TreeNode, ISelfBalanceTreeNode<T> where T : IComparable<T>
    {
        // Implementing Key property from ISelfBalanceTreeNode<T>
        // For compliance, return the first key
        public T Key => Keys.Count > 0 ? Keys[0] : default(T);
        public List<T> Keys { get; set; }
        public bool IsLeaf => ChildNodes == null || ChildNodes.Count == 0;

        public SelfBalanceTreeNode()
        {
            Keys = new List<T>();
            ChildNodes = new List<ITreeNode>();
        }
        
        public SelfBalanceTreeNode(T key)
        {
            Keys = new List<T>();
            ChildNodes = new List<ITreeNode>();
            Keys.Add(key);
        }
        
        internal INodeConnection SetParent(ITreeNode newParent)
        {
            if (newParent == this) throw new InvalidOperationException("A node cannot be its own parent.");
            
            if (ParentNode != null)
            {
                INodeConnection existingConnection = FindConnectionBetweenNodes(ParentNode, this);
                if (existingConnection != null)
                {
                    RemoveConnection(existingConnection);
                }

                ParentNode.RemoveConnection(existingConnection);
                ParentNode.ChildNodes.Remove(this);
            }
            
            ParentNode = newParent;
            return newParent?.ConnectTo(this, NodeConnectionDirection.Unidirectional);
        }
        
        internal virtual INodeConnection AddChild(ITreeNode newNode)
        {
            ChildNodes.Add(newNode);
            return this.ConnectTo(newNode, NodeConnectionDirection.Unidirectional);
        }

        internal virtual INodeConnection InsertChild(int index, ITreeNode newNode)
        {
            ChildNodes.Insert(index, newNode);
            return this.ConnectTo(newNode, NodeConnectionDirection.Unidirectional);
        }
        
        internal List<INodeConnection> AddChildRange(IEnumerable<ITreeNode> collection)
        {
            List<INodeConnection> connections = new List<INodeConnection>();
            foreach (ITreeNode treeNode in collection)
            {
                connections.Add(AddChild(treeNode));
            }
            return connections;
        }
        
        internal void RemoveRange(int index, int count)
        {
            for(int i = index; i < count; i++)
            {
                RemoveChild(ChildNodes[i]);
            }
        }
        
        internal virtual bool RemoveChild(ITreeNode nodeToRemove)
        {
            INodeConnection existingConnection = FindConnectionBetweenNodes(this, nodeToRemove);
            if (existingConnection != null)
            {
                RemoveConnection(existingConnection);
            }
            return ChildNodes.Remove(nodeToRemove);
        }

        internal virtual void ChildRemoveAt(int index)
        {
            INodeConnection existingConnection = FindConnectionBetweenNodes(this, ChildNodes[index]);
            if (existingConnection != null)
            {
                RemoveConnection(existingConnection);
            }
            ChildNodes.RemoveAt(index);
        }
        
        internal bool Contains(ITreeNode nodeToRemove)
        {
            return ChildNodes.Contains(nodeToRemove);
        }
    }
}