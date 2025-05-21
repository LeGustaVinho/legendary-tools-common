using LegendaryTools.Systems.ScreenFlow;

public abstract class ScreenViewController<T> : IScreenViewController<T> 
    where T : class, IScreenBase
{
    public void OnShow(object view)
    {
        OnShow((T)view);
    }

    public void OnHide(object view)
    {
        OnHide((T)view);
    }

    public abstract void OnShow(T view);

    public abstract void OnHide(T view);
}