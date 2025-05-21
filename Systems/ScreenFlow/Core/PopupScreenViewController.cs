using LegendaryTools.Systems.ScreenFlow;

public abstract class PopupScreenViewController<T> : ScreenViewController<T>, IPopupScreenViewController<T> 
    where T : class, IPopupBase
{
    public void OnGoingToBackground(object view)
    {
        OnGoingToBackground((T)view);
    }
    
    public abstract void OnGoingToBackground(T view);
}