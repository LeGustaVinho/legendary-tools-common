using System.Collections.Generic;

namespace LegendaryTools.GraphV2
{
    public interface ITreeNode : INode
    {
        public ITreeNode ParentNode { get; internal set; }
        public List<ITreeNode> ChildNodes { get; }
        
        INodeConnection ConnectToParent(ITreeNode parent); //Connects this node to a parent node in a tree structure. Ensures that the connection is directed from parent to child.
        void DisconnectFromParent(); //Disconnects this node from its parent.
    }
}