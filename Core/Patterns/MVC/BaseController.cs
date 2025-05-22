namespace LegendaryTools.Patterns
{
    public abstract class BaseController<TModel, TView> : IController
        where TModel : IModel
        where TView : IView
    {
        public event System.Action<IController> OnControllerInitialized;
        public event System.Action<IController> OnControllerRebound;
        public event System.Action<IController> OnControllerUnbound;

        protected TModel model;
        protected TView view;

        public BaseController()
        {
            MVCManager.Instance.RegisterController<TView, TModel>(this);
        }

        public void Initialize(IModel model, IView view)
        {
            this.model = (TModel)model;
            this.view = (TView)view;
            this.model.OnModelChanged += OnModelChanged;
            OnInitialize();
            OnControllerInitialized?.Invoke(this);
        }

        protected virtual void OnInitialize() { }

        public void Unbind()
        {
            if (model != null)
            {
                model.OnModelChanged -= OnModelChanged;
            }
            OnUnbind();
            OnControllerUnbound?.Invoke(this);
        }

        protected virtual void OnUnbind() { }

        public void Rebind(TView newView)
        {
            Unbind();
            Initialize(model, newView);
            newView.SetController(this);
            OnControllerRebound?.Invoke(this);
        }

        private void OnModelChanged(IModel changedModel)
        {
            if (view != null)
            {
                view.UpdateView(changedModel);
            }
        }
    }
}