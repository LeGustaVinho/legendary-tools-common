using System;

namespace LegendaryTools.GraphV2
{
    public interface ISelfBalanceTreeNode<T> : ITreeNode
        where T : IComparable<T>
    {
        public T Key { get; }
    }
}