namespace LegendaryTools.GraphV2
{
    public interface IGraph
    {
        public string Id { get; set; } //Guid
        public bool IsDirectedAcyclic { get; } //Also checks NodeConnectionDirection
        public bool IsDirectedCyclic { get; } //Also checks NodeConnectionDirection
        public bool IsAcyclic { get; } //Dont check NodeConnectionDirection
        public bool IsCyclic { get; } //Dont check NodeConnectionDirection
        bool IsDirected { get; }
        IGraph ParentGraph { get; }
        IGraph[] ChildGraphs { get; }
        IGraph[] GraphHierarchy { get; } //Returns the hierarchy of this graph based on ParentGraph looking recursive until ParentGraph is null, in order
        INode[] AllNodes { get; } //Return all nodes only in this graph
        INode[] AllNodesRecursive { get; } //Return all nodes in this graph recursive in ChildGraph nodes
        void Add(INode newNode);
        bool Remove(INode node);
        void AddGraph(IGraph child); //Add a graph children to this graph
        void RemoveGraph(IGraph child);
        bool Contains(INode node);
        INode[] Neighbours(INode node);
    }
}