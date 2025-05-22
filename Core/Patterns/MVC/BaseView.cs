using UnityEngine;

namespace LegendaryTools.Patterns
{
    public abstract class BaseView<TModel, TController> : MonoBehaviour, IView
        where TModel : IModel
        where TController : IController
    {
        public event System.Action<IView> OnViewChanged;
        public event System.Action<IView, IController> OnControllerSet;
        public event System.Action<IView, ViewState> OnStateChanged;
        public event System.Action<IView, bool> OnIsActiveChanged;

        protected TController controller;

        private bool isActive;
        public bool IsActive
        {
            get => isActive;
            set
            {
                if (isActive != value)
                {
                    isActive = value;
                    gameObject.SetActive(isActive);
                    OnIsActiveChanged?.Invoke(this, isActive);
                    OnViewChanged?.Invoke(this);
                }
            }
        }

        private ViewState state;

        public ViewState State
        {
            get => state;
            private set
            {
                if (state == value) return;
                state = value;
                OnStateChanged?.Invoke(this, State);
                OnViewChanged?.Invoke(this);
            }
        }

        protected virtual void Awake()
        {
            MVCManager.Instance.RegisterView<TController, TModel>(this);
        }

        protected virtual void Start()
        {
            IsActive = true;
        }

        protected virtual void OnDestroy()
        {
            MVCManager.Instance.UnregisterView(this);
        }

        protected virtual void OnEnable()
        {
            IsActive = true;
        }

        protected virtual void OnDisable()
        {
            IsActive = false;
        }

        public virtual void SetController(IController controller)
        {
            this.controller = (TController)controller;
            OnControllerSet?.Invoke(this, this.controller);
            OnViewChanged?.Invoke(this);
        }

        public virtual void UpdateView(IModel model)
        {
        }
    }
}