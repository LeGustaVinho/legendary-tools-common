namespace LegendaryTools.Patterns
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    public class MVCManager
    {
        private static MVCManager instance;
        public static MVCManager Instance => instance ??= new MVCManager();

        private Dictionary<Type, IView> pendingViews = new Dictionary<Type, IView>();
        private Dictionary<Type, IController> pendingControllers = new Dictionary<Type, IController>();
        private Dictionary<Type, Func<IModel>> modelFactories = new Dictionary<Type, Func<IModel>>();
        private Dictionary<IView, IController> viewControllerPairs = new Dictionary<IView, IController>();

        public void RegisterModelFactory<TModel>(Func<TModel> factory) where TModel : IModel
        {
            modelFactories[typeof(TModel)] = () => factory() as IModel;
        }

        public void RegisterView<TController, TModel>(IView view)
            where TController : IController
            where TModel : IModel
        {
            Type controllerType = typeof(TController);
            if (pendingControllers.TryGetValue(controllerType, out IController controller))
            {
                Bind<TModel, IView>(controller, view);
                pendingControllers.Remove(controllerType);
            }
            else
            {
                pendingViews[controllerType] = view;
            }
        }

        public void RegisterController<TView, TModel>(IController controller)
            where TView : IView
            where TModel : IModel
        {
            Type viewType = typeof(TView);
            if (pendingViews.TryGetValue(viewType, out IView view))
            {
                Bind<TModel, TView>(controller, (TView)view);
                pendingViews.Remove(viewType);
            }
            else
            {
                pendingControllers[viewType] = controller;
            }
        }

        public void UnregisterView(IView view)
        {
            if (viewControllerPairs.TryGetValue(view, out IController controller))
            {
                controller.Unbind();
                viewControllerPairs.Remove(view);
            }

            KeyValuePair<Type, IView> pendingEntry = pendingViews.FirstOrDefault(pair => pair.Value == view);
            if (pendingEntry.Key != null)
            {
                pendingViews.Remove(pendingEntry.Key);
            }
        }

        public void RebindController<TModel, TView>(BaseController<TModel, TView> controller, TView newView)
            where TModel : IModel
            where TView : IView
        {
            if (viewControllerPairs.Values.Contains(controller))
            {
                IView oldView = viewControllerPairs.FirstOrDefault(pair => pair.Value == controller).Key;
                if (oldView != null)
                {
                    viewControllerPairs.Remove(oldView);
                }
            }

            controller.Rebind(newView);
            viewControllerPairs[newView] = controller;
        }

        private void Bind<TModel, TView>(IController controller, TView view)
            where TModel : IModel
            where TView : IView
        {
            TModel model = default(TModel);
            if (modelFactories.TryGetValue(typeof(TModel), out Func<IModel> factory))
                model = (TModel)factory();
            
            controller.Initialize(model, view);
            view.SetController(controller);
            viewControllerPairs[view] = controller;
        }
    }
}