namespace LegendaryTools.Patterns
{
    using System;

    public interface IModel
    {
        event Action<IModel> OnModelChanged;
    }
}