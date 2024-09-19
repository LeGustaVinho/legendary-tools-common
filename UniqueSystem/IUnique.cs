namespace LegendaryTools
{
    public interface IUnique
    {
        string Name { get; }
        string Guid { get; }

        void AssignNewGuid();
    }
}