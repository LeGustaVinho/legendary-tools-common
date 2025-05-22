namespace LegendaryTools.Patterns
{
    public interface IController
    {
        event System.Action<IController> OnControllerInitialized;
        event System.Action<IController> OnControllerRebound;
        event System.Action<IController> OnControllerUnbound;
        void Initialize(IModel model, IView view);
        void Unbind();
    }
}