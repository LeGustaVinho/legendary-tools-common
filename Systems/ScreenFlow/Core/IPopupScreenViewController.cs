using LegendaryTools.Systems.ScreenFlow;

public interface IPopupScreenViewController : IScreenViewController
{
    void OnGoingToBackground(object view);
}

public interface IPopupScreenViewController<T> : IScreenViewController<T> where T : IPopupBase
{
    void OnGoingToBackground(T view);
}