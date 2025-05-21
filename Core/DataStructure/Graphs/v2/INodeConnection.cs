namespace LegendaryTools.GraphV2
{
    public interface INodeConnection
    {
        public string Id { get; set; } //Guid
        public INode FromNode { get; set; }
        public INode ToNode { get; set; }
        NodeConnectionDirection Direction { get; set; }
        void Disconnect();
        INode GetOut(INode from);
        INode GetIn(INode to);
    }
}