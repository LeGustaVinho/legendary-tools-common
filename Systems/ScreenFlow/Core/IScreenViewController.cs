using LegendaryTools.Systems.ScreenFlow;

public interface IScreenViewController
{
    void OnShow(object view);
    void OnHide(object view);
}

public interface IScreenViewController<T> : IScreenViewController where T : IScreenBase
{
    void OnShow(T view);
    void OnHide(T view);
}