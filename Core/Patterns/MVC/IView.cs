namespace LegendaryTools.Patterns
{
    public interface IView
    {
        event System.Action<IView> OnViewChanged;
        event System.Action<IView, IController> OnControllerSet;
        event System.Action<IView, ViewState> OnStateChanged;
        event System.Action<IView, bool> OnIsActiveChanged;
        void SetController(IController controller);
        void UpdateView(IModel model);
        bool IsActive { get; set; }
        ViewState State { get; }
    }
}