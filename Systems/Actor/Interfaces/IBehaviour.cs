namespace LegendaryTools.Actor
{
    public interface IBehaviour : IComponent
    {
        bool Enabled { get; set; }
        bool IsActiveAndEnabled { get; }
    }
}