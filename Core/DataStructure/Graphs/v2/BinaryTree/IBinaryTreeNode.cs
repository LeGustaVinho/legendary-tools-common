namespace LegendaryTools.GraphV2
{
    public interface IBinaryTreeNode : ITreeNode
    {
        public ITreeNode Left { get;  }
        public ITreeNode Right { get; }
    }
}