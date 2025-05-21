using System.Collections.Generic;

namespace LegendaryTools.GraphV2
{
    public interface IMultiParentTreeNode : INode
    {
        public List<IMultiParentTreeNode> ParentNodes { get; internal set; }
        public List<IMultiParentTreeNode> ChildNodes { get; }
        
        INodeConnection ConnectToParent(IMultiParentTreeNode parent); //Connects this node to a parent node in a tree structure. Ensures that the connection is directed from parent to child.
        void DisconnectFromParents(); //Disconnects this node from all parents.
        void DisconnectFromParent(IMultiParentTreeNode parentNode); //Disconnects this node from a specific parent.
    }
}