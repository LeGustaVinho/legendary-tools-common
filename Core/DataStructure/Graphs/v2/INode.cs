using System.Collections.Generic;

namespace LegendaryTools.GraphV2
{
    public interface INode
    {
        public string Id { get; set; } //Guid
        bool ShouldMergeOppositeConnections { get; }
        INode[] Neighbours { get; }
        IGraph Owner { get; }
        List<INodeConnection> Connections { get; }
        INodeConnection[] OutboundConnections { get; }
        INodeConnection[] InboundConnections { get; }
        int Count { get; }
        INodeConnection ConnectTo(INode to, NodeConnectionDirection newDirection);
        bool RemoveConnection(INodeConnection nodeConnection);
        INodeConnection FindConnectionBetweenNodes(INode from, INode to);
        internal void SetOwner(IGraph owner);
    }
}