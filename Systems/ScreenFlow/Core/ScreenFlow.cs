using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace LegendaryTools.Systems.ScreenFlow
{
    [RequireComponent(typeof(Canvas))]
    [RequireComponent(typeof(CanvasScaler))]
    public class ScreenFlow : 
#if SCREEN_FLOW_SINGLETON
        SingletonBehaviour<ScreenFlow>
#else
        MonoBehaviour
#endif
        , IScreenFlow
    {
        public bool AutoInitializeOnStart;
        public ScreenFlowConfig Config;
        public ScreenConfig StartScreen;
        public List<ScreenInScene> ScreensInScene = new List<ScreenInScene>();
        public List<PopupInScene> PopupsInScene = new List<PopupInScene>();

        public bool IsTransiting => commandQueueProcessor.IsTransiting;
        public bool IsPreloading => uiEntityLoader.IsPreloading;
        public ScreenConfig CurrentScreenConfig => ScreensHistory.Count > 0 ? ScreensHistory[ScreensHistory.Count - 1].Entity : null;
        public IScreenBase CurrentScreenInstance { get; private set; }
        public PopupConfig CurrentPopupConfig => popupManager.CurrentPopupConfig;
        public IPopupBase CurrentPopupInstance => popupManager.CurrentPopupInstance;
        public List<IPopupBase> CurrentPopupInstancesStack => popupManager.CurrentPopupInstancesStack2;
        public int PopupStackCount => popupManager.PopupStackCount;

        public event Action<ScreenConfig, IScreenBase> OnStart;
        public event Action<(ScreenConfig, IScreenBase), (ScreenConfig, IScreenBase)> OnScreenChange;
        public event Action<(PopupConfig, IPopupBase), (PopupConfig, IPopupBase)> OnPopupOpen
        {
            add => popupManager.OnPopupOpen += value;
            remove => popupManager.OnPopupOpen -= value;
        }

        internal readonly List<EntityArgPair<ScreenConfig>> ScreensHistory = new List<EntityArgPair<ScreenConfig>>();
        internal readonly Dictionary<Type, List<IScreenViewController>> viewControllers = new Dictionary<Type, List<IScreenViewController>>();
        private readonly Dictionary<string, UIEntityBaseConfig> uiEntitiesLookup = new Dictionary<string, UIEntityBaseConfig>();

        private PopupManager popupManager;
        private CommandQueueProcessor commandQueueProcessor;
        private UIEntityLoader uiEntityLoader;
        private RectTransform rectTransform;

#if SCREEN_FLOW_SINGLETON
        protected override void Awake()
#else
        protected void Awake()
#endif
        {
            rectTransform = GetComponent<RectTransform>();
            popupManager = new PopupManager(Config, GetComponent<Canvas>(), GetComponent<CanvasScaler>(), GetComponent<GraphicRaycaster>());
            commandQueueProcessor = new CommandQueueProcessor(this, popupManager, uiEntityLoader);
            uiEntityLoader = new UIEntityLoader(this, popupManager);
        }

#if SCREEN_FLOW_SINGLETON
        protected override async void Start()
#else
        protected async void Start()
#endif
        {
            if (AutoInitializeOnStart)
                await Initialize();
        }

        protected virtual void Update()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                ProcessBackKey();
            }
#endif
        }

        public async Task Initialize()
        {
            if (!ValidateConfig()) return;
            PopulateUIEntitiesLookup();
            ProcessInSceneEntities();
            if (StartScreen != null)
            {
                await SendTrigger(StartScreen);
            }
            await uiEntityLoader.Preload(Config);
        }

        public void BindController<T>(IScreenViewController<T> controller) where T : IScreenBase
        {
            Type viewType = typeof(T);
            if (!viewControllers.ContainsKey(viewType))
                viewControllers.Add(viewType, new List<IScreenViewController>());
            if (!viewControllers[viewType].Contains(controller))
                viewControllers[viewType].Add(controller);
        }

        public void UnBindController<T>(IScreenViewController<T> controller) where T : IScreenBase
        {
            Type viewType = typeof(T);
            if (viewControllers.TryGetValue(viewType, out List<IScreenViewController> controllers))
            {
                if (controllers.Contains(controller))
                    controllers.Remove(controller);
            }
        }

        public async Task SendTrigger(string name, object args = null, bool enqueue = true,
            Action<IScreenBase> requestedScreenOnShow = null, Action<IScreenBase> previousScreenOnHide = null)
        {
            if (uiEntitiesLookup.TryGetValue(name, out UIEntityBaseConfig uiEntityBaseConfig))
            {
                await SendTrigger(uiEntityBaseConfig, args, enqueue, requestedScreenOnShow, previousScreenOnHide);
            }
        }

        public async Task SendTrigger(UIEntityBaseConfig uiEntity, object args = null, bool enqueue = true,
            Action<IScreenBase> requestedScreenOnShow = null, Action<IScreenBase> previousScreenOnHide = null)
        {
            ScreenFlowCommand command = new ScreenFlowCommand(ScreenFlowCommandType.Trigger, uiEntity, args, requestedScreenOnShow, previousScreenOnHide);
            await commandQueueProcessor.ProcessCommand(command, enqueue);
        }

        public async Task SendTriggerT<TConfig, TShow>(TConfig uiEntity, TShow args = null, bool enqueue = true,
            Action<IScreenBase> requestedScreenOnShow = null, Action<IScreenBase> previousScreenOnHide = null)
            where TConfig : UIEntityBaseConfig
            where TShow : class
        {
            await SendTrigger(uiEntity, args, enqueue, requestedScreenOnShow, previousScreenOnHide);
        }

        public async Task SendTriggerT<TConfig, TShow, THide>(TConfig uiEntity, TShow args = null, bool enqueue = true,
            Action<ScreenBaseT<TConfig, TShow, THide>> requestedScreenOnShow = null,
            Action<IScreenBase> previousScreenOnHide = null)
            where TConfig : UIEntityBaseConfig
            where TShow : class
            where THide : class
        {
            void RequestedScreenTOnShow(IScreenBase screenBase)
            {
                if (screenBase is ScreenBaseT<TConfig, TShow, THide> screenBaseT)
                    requestedScreenOnShow?.Invoke(screenBaseT);
            }
            await SendTrigger(uiEntity, args, enqueue, RequestedScreenTOnShow, previousScreenOnHide);
        }

        public async Task MoveBack(object args = null, bool enqueue = true, Action<IScreenBase> onShow = null, Action<IScreenBase> onHide = null)
        {
            ScreenFlowCommand command = new ScreenFlowCommand(ScreenFlowCommandType.MoveBack, null, args, onShow, onHide);
            await commandQueueProcessor.ProcessCommand(command, enqueue);
        }

        public void CloseForegroundPopup(object args = null, bool enqueue = true, Action<IScreenBase> onShow = null, Action<IScreenBase> onHide = null)
        {
            if (CurrentPopupInstance != null)
            {
                ClosePopup(CurrentPopupInstance, args, enqueue, onShow, onHide);
            }
        }

        public void ClosePopup(IPopupBase popupBase, object args = null, bool enqueue = true, Action<IScreenBase> onShow = null, Action<IScreenBase> onHide = null)
        {
            ScreenFlowCommand command = new ScreenFlowCommand(ScreenFlowCommandType.ClosePopup, popupBase, args, onShow, onHide);
            commandQueueProcessor.EnqueueOrProcessCommand(command, enqueue);
        }

        public void Dispose()
        {
            commandQueueProcessor.Dispose();
            uiEntityLoader.Dispose();
            popupManager.Dispose();
        }

        protected internal T InstantiateUIElement<T>(T screenPrefab, Transform parent, out RectTransform elementInstanceRT,
            out RectTransform elementPrefabRT) where T : ScreenBase
        {
            return uiEntityLoader.InstantiateUIElement(screenPrefab, parent, out elementInstanceRT, out elementPrefabRT);
        }

        protected internal void ReparentUIElement(RectTransform elementInstanceRT, RectTransform elementPrefabRT, Transform parent)
        {
            uiEntityLoader.ReparentUIElement(elementInstanceRT, elementPrefabRT, parent);
        }

        private bool ValidateConfig()
        {
            if (Config == null)
            {
                Debug.LogError("[ScreenFlow:Start] -> Config is null");
                return false;
            }
            return true;
        }

        private void PopulateUIEntitiesLookup()
        {
            uiEntitiesLookup.Clear();
            AddUIEntitiesToLookup(Config.Screens, "[ScreenFlow:Start()] -> UI Entity {0} already exists in ScreenFlow");
            AddUIEntitiesToLookup(Config.Popups, "[ScreenFlow:Start()] -> UI Entity {0} already exists in ScreenFlow");
        }

        private void AddUIEntitiesToLookup<T>(IEnumerable<T> entities, string errorMessage)
            where T : UIEntityBaseConfig
        {
            foreach (T entity in entities)
            {
                if (!uiEntitiesLookup.ContainsKey(entity.name))
                {
                    uiEntitiesLookup.Add(entity.name, entity);
                }
                else
                {
                    Debug.LogError(string.Format(errorMessage, entity.name));
                }
            }
        }

        private void ProcessInSceneEntities()
        {
            foreach (ScreenInScene screenInScene in ScreensInScene)
            {
                if (!uiEntitiesLookup.ContainsKey(screenInScene.Config.name))
                {
                    uiEntitiesLookup.Add(screenInScene.Config.name, screenInScene.Config);
                    screenInScene.Config.AssetLoaderConfig.SetAsSceneAsset(screenInScene.ScreenInstance);
                }
                else
                {
                    Debug.LogError($"[ScreenFlow:Start()] -> UI Entity {screenInScene.Config.name} already exists in ScreenFlow");
                }
            }

            foreach (PopupInScene popupInScene in PopupsInScene)
            {
                if (!uiEntitiesLookup.ContainsKey(popupInScene.Config.name))
                {
                    uiEntitiesLookup.Add(popupInScene.Config.name, popupInScene.Config);
                    popupInScene.Config.AssetLoaderConfig.SetAsSceneAsset(popupInScene.PopupInstance);
                }
                else
                {
                    Debug.LogError($"[ScreenFlow:Start()] -> UI Entity {popupInScene.Config.name} already exists in ScreenFlow");
                }
            }
        }

        private void ProcessBackKey()
        {
            if (CurrentScreenConfig == null) return;
            BackKeyBehaviourOverride behaviour = CurrentScreenInstance.BackKeyBehaviourOverride;
            BackKeyBehaviour configBehaviour = CurrentScreenConfig.BackKeyBehaviour;
            BackKeyBehaviour effectiveBehaviour = behaviour == BackKeyBehaviourOverride.Inherit
                ? configBehaviour
                : (BackKeyBehaviour)behaviour;
            HandleBackKey(effectiveBehaviour);
        }

        private void HandleBackKey(BackKeyBehaviour behaviour)
        {
            switch (behaviour)
            {
                case BackKeyBehaviour.NotAllowed: break;
                case BackKeyBehaviour.ScreenMoveBack: MoveBack().FireAndForget(); break;
                case BackKeyBehaviour.CloseFirstPopup:
                    if (CurrentPopupInstance != null)
                        CloseForegroundPopup();
                    else MoveBack().FireAndForget();
                    break;
            }
        }

        internal async Task ScreenTransitTo(ScreenConfig screenConfig, bool isMoveBack, object args,
            Action<IScreenBase> onShow, Action<IScreenBase> onHide)
        {
            ScreenConfig oldScreenConfig = CurrentScreenConfig;
            IScreenBase oldScreenInstance = CurrentScreenInstance;

            await uiEntityLoader.StartScreenTransition(screenConfig, args);
            await uiEntityLoader.HideCurrentScreen(oldScreenConfig, oldScreenInstance, args, onHide);

            ScreenBase newScreenInstance = null;
            await uiEntityLoader.LoadAndInstantiateScreen(screenConfig, instance => newScreenInstance = instance);
            if (newScreenInstance == null) return;

            await uiEntityLoader.ShowNewScreen(screenConfig, newScreenInstance, args);

            UpdateScreenState(screenConfig, newScreenInstance, isMoveBack, args);
            NotifyScreenChange(oldScreenConfig, oldScreenInstance, screenConfig, newScreenInstance, onShow);
        }

        private void UpdateScreenState(ScreenConfig screenConfig, ScreenBase newScreenInstance,
            bool isMoveBack, object args)
        {
            CurrentScreenInstance = newScreenInstance;
            if (isMoveBack)
            {
                ScreensHistory.RemoveAt(ScreensHistory.Count - 1);
            }
            else
            {
                ScreensHistory.Add(new EntityArgPair<ScreenConfig>(screenConfig, args));
            }
            foreach (IPopupBase popup in popupManager.CurrentPopupInstancesStack2)
            {
                popup.ParentScreen = CurrentScreenInstance;
            }
        }

        private void NotifyScreenChange(ScreenConfig oldScreenConfig, IScreenBase oldScreenInstance,
            ScreenConfig newScreenConfig, IScreenBase newScreenInstance, Action<IScreenBase> onShow)
        {
            onShow?.Invoke(newScreenInstance);
            if (oldScreenConfig == null)
                OnStart?.Invoke(newScreenConfig, newScreenInstance);
            OnScreenChange?.Invoke((oldScreenConfig, oldScreenInstance), (newScreenConfig, newScreenInstance));
            CallOnShowForController(newScreenInstance);
        }

        internal void CallOnShowForController(IScreenBase view)
        {
            Type viewType = view.GetType();
            if (viewControllers.TryGetValue(viewType, out List<IScreenViewController> controllers))
            {
                foreach (IScreenViewController controller in controllers)
                {
                    controller.OnShow(view);
                }
            }
        }

        internal void CallOnHideForController(IScreenBase view)
        {
            Type viewType = view.GetType();
            if (viewControllers.TryGetValue(viewType, out List<IScreenViewController> controllers))
            {
                foreach (IScreenViewController controller in controllers)
                {
                    controller.OnHide(view);
                }
            }
        }

        internal void CallOnGoingToBackgroundForController(IPopupBase view)
        {
            Type viewType = view.GetType();
            if (viewControllers.TryGetValue(viewType, out List<IScreenViewController> controllers))
            {
                foreach (IScreenViewController controller in controllers)
                {
                    if (controller is not IPopupScreenViewController popupViewController) continue;
                    popupViewController.OnGoingToBackground(view);
                }
            }
        }
    }
}