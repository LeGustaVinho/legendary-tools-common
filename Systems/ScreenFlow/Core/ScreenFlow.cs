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
        public List<ScreenInScene> ScreensInScene = new();
        public List<PopupInScene> PopupsInScene = new();

        public bool IsTransiting => commandQueueProcessor?.IsTransiting ?? false;
        public bool IsPreloading => uiEntityLoader?.IsPreloading ?? false;

        public ScreenConfig CurrentScreenConfig =>
            ScreensHistory.Count > 0 ? ScreensHistory[ScreensHistory.Count - 1].Entity : null;

        public IScreenBase CurrentScreenInstance { get; private set; }
        public PopupConfig CurrentPopupConfig => popupManager?.CurrentPopupConfig;
        public IPopupBase CurrentPopupInstance => popupManager?.CurrentPopupInstance;

        public List<IPopupBase> CurrentPopupInstancesStack =>
            popupManager?.CurrentPopupInstancesStack2 ?? new List<IPopupBase>();

        public int PopupStackCount => popupManager?.PopupStackCount ?? 0;

        public event Action<ScreenConfig, IScreenBase> OnStart;
        public event Action<(ScreenConfig, IScreenBase), (ScreenConfig, IScreenBase)> OnScreenChange;

        public event Action<(PopupConfig, IPopupBase), (PopupConfig, IPopupBase)> OnPopupOpen
        {
            add => popupManager.OnPopupOpen += value;
            remove => popupManager.OnPopupOpen -= value;
        }

        public bool Verbose;

        internal readonly List<EntityArgPair<ScreenConfig>> ScreensHistory = new();
        private readonly Dictionary<Type, List<IScreenViewController>> viewControllers = new();
        private readonly Dictionary<string, UIEntityBaseConfig> uiEntitiesLookup = new();

        private PopupManager popupManager;
        private CommandQueueProcessor commandQueueProcessor;
        private UIEntityLoader uiEntityLoader;
        private RectTransform rectTransform;
        private KeyCode backKey = KeyCode.None;

#if SCREEN_FLOW_SINGLETON
        protected override void Awake()
#else
        protected void Awake()
#endif
        {
            if (!ValidateComponents()) return;

            rectTransform = GetComponent<RectTransform>();
            popupManager = new PopupManager(this, Config, GetComponent<Canvas>(), GetComponent<CanvasScaler>(),
                GetComponent<GraphicRaycaster>());
            if (popupManager == null) Debug.LogError("[ScreenFlow:Awake] -> Failed to initialize PopupManager");

            uiEntityLoader = new UIEntityLoader(this, popupManager);
            if (uiEntityLoader == null) Debug.LogError("[ScreenFlow:Awake] -> Failed to initialize UIEntityLoader");

            commandQueueProcessor = new CommandQueueProcessor(this, popupManager, uiEntityLoader);
            if (commandQueueProcessor == null)
                Debug.LogError("[ScreenFlow:Awake] -> Failed to initialize CommandQueueProcessor");
        }

#if SCREEN_FLOW_SINGLETON
        protected override async void Start()
#else
        protected async void Start()
#endif
        {
            if (AutoInitializeOnStart) await Initialize();
        }

        protected virtual void Update()
        {
            if (backKey != KeyCode.None && Input.GetKeyDown(backKey))
            {
                if (CurrentScreenConfig == null)
                {
                    Debug.LogWarning("[ScreenFlow:Update] -> Back key pressed but no current screen config available");
                    return;
                }

                ProcessBackKey();
            }
        }

        public async Task Initialize()
        {
            if (!ValidateConfig()) return;
            Verbose = Config.Verbose;
            PopulateUIEntitiesLookup();
            ProcessInSceneEntities();
            CacheBackKey();

            ScreenConfig startScreen = Config.StartScreen;
            if (startScreen == null && Config.Screens.Length > 0)
            {
                startScreen = Config.Screens[0];
                Debug.LogWarning("[ScreenFlow:Initialize] -> No start screen specified, using first screen in config");
            }

            if (startScreen != null)
            {
                await SendTrigger(startScreen);
                if (Verbose)
                    Debug.Log(
                        $"[ScreenFlow:Initialize] -> Successfully initialized with start screen: {startScreen.name}");
            }
            else
            {
                Debug.LogWarning("[ScreenFlow:Initialize] -> No start screen available");
            }

            await uiEntityLoader.Preload(Config);
        }

        public void BindController<T>(IScreenViewController<T> controller) where T : IScreenBase
        {
            if (controller == null)
            {
                Debug.LogError("[ScreenFlow:BindController] -> Attempted to bind null controller");
                return;
            }

            Type viewType = typeof(T);
            if (!viewControllers.ContainsKey(viewType))
                viewControllers.Add(viewType, new List<IScreenViewController>());

            if (!viewControllers[viewType].Contains(controller))
            {
                viewControllers[viewType].Add(controller);
                if (Verbose) Debug.Log($"[ScreenFlow:BindController] -> Bound controller for type {viewType.Name}");
            }
            else
            {
                Debug.LogWarning($"[ScreenFlow:BindController] -> Controller for type {viewType.Name} already bound");
            }
        }

        public void UnBindController<T>(IScreenViewController<T> controller) where T : IScreenBase
        {
            if (controller == null)
            {
                Debug.LogError("[ScreenFlow:UnBindController] -> Attempted to unbind null controller");
                return;
            }

            Type viewType = typeof(T);
            if (!viewControllers.TryGetValue(viewType, out List<IScreenViewController> controllers))
            {
                Debug.LogWarning($"[ScreenFlow:UnBindController] -> No controllers found for type {viewType.Name}");
                return;
            }

            if (controllers.Contains(controller))
            {
                controllers.Remove(controller);
                if (Verbose) Debug.Log($"[ScreenFlow:UnBindController] -> Unbound controller for type {viewType.Name}");
            }
            else
            {
                Debug.LogWarning($"[ScreenFlow:UnBindController] -> Controller not found for type {viewType.Name}");
            }
        }

        public async Task SendTrigger(string name, object args = null, Action<IScreenBase> requestedScreenOnShow = null,
            Action<IScreenBase> previousScreenOnHide = null)
        {
            if (string.IsNullOrEmpty(name))
            {
                Debug.LogError("[ScreenFlow:SendTrigger] -> Trigger name is null or empty");
                return;
            }

            if (uiEntitiesLookup.TryGetValue(name, out UIEntityBaseConfig uiEntityBaseConfig))
                await SendTrigger(uiEntityBaseConfig, args, requestedScreenOnShow, previousScreenOnHide);
            else
                Debug.LogError($"[ScreenFlow:SendTrigger] -> No UI entity found with name: {name}");
        }

        public async Task SendTrigger(UIEntityBaseConfig uiEntity, object args = null,
            Action<IScreenBase> requestedScreenOnShow = null, Action<IScreenBase> previousScreenOnHide = null)
        {
            if (uiEntity == null)
            {
                Debug.LogError("[ScreenFlow:SendTrigger] -> UI entity is null");
                return;
            }

            ScreenFlowCommand command = new(ScreenFlowCommandType.Trigger, uiEntity, args, requestedScreenOnShow,
                previousScreenOnHide);
            await commandQueueProcessor.ProcessCommand(command);
            if (Verbose) Debug.Log($"[ScreenFlow:SendTrigger] -> Processed trigger for {uiEntity.name}");
        }

        public async Task SendTriggerT<TConfig, TShow>(TConfig uiEntity, TShow args = null,
            Action<IScreenBase> requestedScreenOnShow = null, Action<IScreenBase> previousScreenOnHide = null)
            where TConfig : UIEntityBaseConfig
            where TShow : class
        {
            await SendTrigger(uiEntity, args, requestedScreenOnShow, previousScreenOnHide);
        }

        public async Task SendTriggerT<TConfig, TShow, THide>(TConfig uiEntity, TShow args = null,
            Action<ScreenBaseT<TConfig, TShow, THide>> requestedScreenOnShow = null,
            Action<IScreenBase> previousScreenOnHide = null)
            where TConfig : UIEntityBaseConfig
            where TShow : class
            where THide : class
        {
            if (uiEntity == null)
            {
                Debug.LogError("[ScreenFlow:SendTriggerT] -> UI entity is null");
                return;
            }

            void RequestedScreenTOnShow(IScreenBase screenBase)
            {
                if (screenBase is ScreenBaseT<TConfig, TShow, THide> screenBaseT)
                    requestedScreenOnShow?.Invoke(screenBaseT);
                else
                    Debug.LogError(
                        $"[ScreenFlow:SendTriggerT] -> ScreenBase is not of type ScreenBaseT<{typeof(TConfig).Name}, {typeof(TShow).Name}, {typeof(THide).Name}>");
            }

            await SendTrigger(uiEntity, args, RequestedScreenTOnShow, previousScreenOnHide);
        }

        public async Task MoveBack(object args = null, Action<IScreenBase> onShow = null,
            Action<IScreenBase> onHide = null)
        {
            if (ScreensHistory.Count < 2)
            {
                Debug.LogWarning("[ScreenFlow:MoveBack] -> Cannot move back, insufficient screen history");
                return;
            }

            ScreenFlowCommand command = new(ScreenFlowCommandType.MoveBack, null, args, onShow, onHide);
            await commandQueueProcessor.ProcessCommand(command);
            if (Verbose) Debug.Log("[ScreenFlow:MoveBack] -> Processed move back command");
        }

        public void CloseForegroundPopup(object args = null, Action<IScreenBase> onShow = null,
            Action<IScreenBase> onHide = null)
        {
            if (CurrentPopupInstance == null)
            {
                Debug.LogWarning("[ScreenFlow:CloseForegroundPopup] -> No popup instance to close");
                return;
            }

            ClosePopup(CurrentPopupInstance, args, onShow, onHide);
        }

        public void ClosePopup(IPopupBase popupBase, object args = null, Action<IScreenBase> onShow = null,
            Action<IScreenBase> onHide = null)
        {
            if (popupBase == null)
            {
                Debug.LogError("[ScreenFlow:ClosePopup] -> PopupBase is null");
                return;
            }

            ScreenFlowCommand command = new(ScreenFlowCommandType.ClosePopup, popupBase, args, onShow, onHide);
            commandQueueProcessor.ProcessCommand(command).FireAndForget();
            if (Verbose)
                Debug.Log($"[ScreenFlow:ClosePopup] -> Queued close popup command for {popupBase.GameObject.name}");
        }

        public void Dispose()
        {
            commandQueueProcessor?.Dispose();
            uiEntityLoader?.Dispose();
            popupManager?.Dispose();
            viewControllers.Clear();
            uiEntitiesLookup.Clear();
            ScreensHistory.Clear();
            if (Verbose) Debug.Log("[ScreenFlow:Dispose] -> ScreenFlow disposed");
        }

        protected internal T InstantiateUIElement<T>(T screenPrefab, Transform parent,
            out RectTransform elementInstanceRT,
            out RectTransform elementPrefabRT) where T : ScreenBase
        {
            return uiEntityLoader.InstantiateUIElement(screenPrefab, parent, out elementInstanceRT,
                out elementPrefabRT);
        }

        protected internal void ReparentUIElement(RectTransform elementInstanceRT, RectTransform elementPrefabRT,
            Transform parent)
        {
            uiEntityLoader.ReparentUIElement(elementInstanceRT, elementPrefabRT, parent);
        }

        private bool ValidateComponents()
        {
            if (GetComponent<Canvas>() == null)
            {
                Debug.LogError("[ScreenFlow:ValidateComponents] -> Canvas component is missing");
                return false;
            }

            if (GetComponent<CanvasScaler>() == null)
            {
                Debug.LogError("[ScreenFlow:ValidateComponents] -> CanvasScaler component is missing");
                return false;
            }

            if (GetComponent<GraphicRaycaster>() == null)
            {
                Debug.LogError("[ScreenFlow:ValidateComponents] -> GraphicRaycaster component is missing");
                return false;
            }

            return true;
        }

        private bool ValidateConfig()
        {
            if (Config == null)
            {
                Debug.LogError("[ScreenFlow:ValidateConfig] -> Config is null");
                return false;
            }

            if (Config.Screens == null)
            {
                Debug.LogError("[ScreenFlow:ValidateConfig] -> Config.Screens is null");
                return false;
            }

            if (Config.Popups == null)
            {
                Debug.LogError("[ScreenFlow:ValidateConfig] -> Config.Popups is null");
                return false;
            }

            return true;
        }

        private void CacheBackKey()
        {
            if (Config == null || Config.PlatformBackKeys == null)
            {
                Debug.LogWarning("[ScreenFlow:CacheBackKey] -> No platform back key configuration available");
                backKey = KeyCode.None;
                return;
            }

            foreach (PlatformBackKeyConfig config in Config.PlatformBackKeys)
            {
                if (config.Platform == Application.platform && config.Enabled)
                {
                    backKey = config.BackKey;
                    if (Verbose)
                        Debug.Log(
                            $"[ScreenFlow:CacheBackKey] -> Back key set to {backKey} for platform {config.Platform}");
                    return;
                }
            }

            backKey = KeyCode.None;
            if (Verbose) Debug.Log("[ScreenFlow:CacheBackKey] -> No back key configured for current platform");
        }

        private void PopulateUIEntitiesLookup()
        {
            uiEntitiesLookup.Clear();
            AddUIEntitiesToLookup(Config.Screens,
                "[ScreenFlow:PopulateUIEntitiesLookup] -> Screen {0} already exists in ScreenFlow");
            AddUIEntitiesToLookup(Config.Popups,
                "[ScreenFlow:PopulateUIEntitiesLookup] -> Popup {0} already exists in ScreenFlow");
            if (Verbose)
                Debug.Log(
                    $"[ScreenFlow:PopulateUIEntitiesLookup] -> Populated lookup with {uiEntitiesLookup.Count} entities");
        }

        private void AddUIEntitiesToLookup<T>(IEnumerable<T> entities, string errorMessage)
            where T : UIEntityBaseConfig
        {
            if (entities == null)
            {
                Debug.LogError("[ScreenFlow:AddUIEntitiesToLookup] -> Entities collection is null");
                return;
            }

            foreach (T entity in entities)
            {
                if (entity == null)
                {
                    Debug.LogError("[ScreenFlow:AddUIEntitiesToLookup] -> Found null entity in collection");
                    continue;
                }

                if (!uiEntitiesLookup.ContainsKey(entity.name))
                    uiEntitiesLookup.Add(entity.name, entity);
                else
                    Debug.LogError(string.Format(errorMessage, entity.name));
            }
        }

        private void ProcessInSceneEntities()
        {
            if (ScreensInScene == null)
            {
                Debug.LogError("[ScreenFlow:ProcessInSceneEntities] -> ScreensInScene list is null");
                return;
            }

            if (PopupsInScene == null)
            {
                Debug.LogError("[ScreenFlow:ProcessInSceneEntities] -> PopupsInScene list is null");
                return;
            }

            foreach (ScreenInScene screenInScene in ScreensInScene)
            {
                if (screenInScene == null || screenInScene.Config == null || screenInScene.ScreenInstance == null)
                {
                    Debug.LogError("[ScreenFlow:ProcessInSceneEntities] -> Invalid ScreenInScene entry");
                    continue;
                }

                if (!uiEntitiesLookup.ContainsKey(screenInScene.Config.name))
                {
                    uiEntitiesLookup.Add(screenInScene.Config.name, screenInScene.Config);
                    screenInScene.Config.AssetLoaderConfig.SetAsSceneAsset(screenInScene.ScreenInstance);
                    if (Verbose)
                        Debug.Log(
                            $"[ScreenFlow:ProcessInSceneEntities] -> Added in-scene screen: {screenInScene.Config.name}");
                }
                else
                {
                    Debug.LogError(
                        $"[ScreenFlow:ProcessInSceneEntities] -> UI Entity {screenInScene.Config.name} already exists in ScreenFlow");
                }
            }

            foreach (PopupInScene popupInScene in PopupsInScene)
            {
                if (popupInScene == null || popupInScene.Config == null || popupInScene.PopupInstance == null)
                {
                    Debug.LogError("[ScreenFlow:ProcessInSceneEntities] -> Invalid PopupInScene entry");
                    continue;
                }

                if (!uiEntitiesLookup.ContainsKey(popupInScene.Config.name))
                {
                    uiEntitiesLookup.Add(popupInScene.Config.name, popupInScene.Config);
                    popupInScene.Config.AssetLoaderConfig.SetAsSceneAsset(popupInScene.PopupInstance);
                    if (Verbose)
                        Debug.Log(
                            $"[ScreenFlow:ProcessInSceneEntities] -> Added in-scene popup: {popupInScene.Config.name}");
                }
                else
                {
                    Debug.LogError(
                        $"[ScreenFlow:ProcessInSceneEntities] -> UI Entity {popupInScene.Config.name} already exists in ScreenFlow");
                }
            }
        }

        private void ProcessBackKey()
        {
            if (CurrentScreenConfig == null)
            {
                Debug.LogWarning("[ScreenFlow:ProcessBackKey] -> No current screen config available");
                return;
            }

            if (CurrentScreenInstance == null)
            {
                Debug.LogError("[ScreenFlow:ProcessBackKey] -> Current screen instance is null");
                return;
            }

            BackKeyBehaviourOverride behaviour = CurrentScreenInstance.BackKeyBehaviourOverride;
            BackKeyBehaviour configBehaviour = CurrentScreenConfig.BackKeyBehaviour;
            BackKeyBehaviour effectiveBehaviour = behaviour == BackKeyBehaviourOverride.Inherit
                ? configBehaviour
                : (BackKeyBehaviour)behaviour;

            if (Verbose)
                Debug.Log($"[ScreenFlow:ProcessBackKey] -> Processing back key with behaviour: {effectiveBehaviour}");
            HandleBackKey(effectiveBehaviour);
        }

        private void HandleBackKey(BackKeyBehaviour behaviour)
        {
            switch (behaviour)
            {
                case BackKeyBehaviour.NotAllowed:
                    if (Verbose) Debug.Log("[ScreenFlow:HandleBackKey] -> Back key not allowed");
                    break;
                case BackKeyBehaviour.ScreenMoveBack:
                    if (Verbose) Debug.Log("[ScreenFlow:HandleBackKey] -> Initiating screen move back");
                    MoveBack().FireAndForget();
                    break;
                case BackKeyBehaviour.CloseFirstPopupOrMoveBack:
                    if (CurrentPopupInstance != null)
                    {
                        if (Verbose) Debug.Log("[ScreenFlow:HandleBackKey] -> Closing foreground popup");
                        CloseForegroundPopup();
                    }
                    else
                    {
                        if (Verbose) Debug.Log("[ScreenFlow:HandleBackKey] -> No popup, initiating screen move back");
                        MoveBack().FireAndForget();
                    }

                    break;
            }
        }

        internal async Task ScreenTransitTo(ScreenConfig screenConfig, bool isMoveBack, object args,
            Action<IScreenBase> onShow, Action<IScreenBase> onHide)
        {
            if (screenConfig == null)
            {
                Debug.LogError("[ScreenFlow:ScreenTransitTo] -> ScreenConfig is null");
                return;
            }

            ScreenConfig oldScreenConfig = CurrentScreenConfig;
            IScreenBase oldScreenInstance = CurrentScreenInstance;

            await uiEntityLoader.StartScreenTransition(screenConfig, args);
            await uiEntityLoader.HideCurrentScreen(oldScreenConfig, oldScreenInstance, args, onHide);

            ScreenBase newScreenInstance = await uiEntityLoader.LoadAndInstantiateScreen(screenConfig);
            if (newScreenInstance == null)
            {
                Debug.LogError($"[ScreenFlow:ScreenTransitTo] -> Failed to instantiate screen: {screenConfig.name}");
                return;
            }

            await uiEntityLoader.ShowNewScreen(screenConfig, newScreenInstance, args);
            UpdateScreenState(screenConfig, newScreenInstance, isMoveBack, args);
            NotifyScreenChange(oldScreenConfig, oldScreenInstance, screenConfig, newScreenInstance, onShow);
            if (Verbose) Debug.Log($"[ScreenFlow:ScreenTransitTo] -> Transitioned to screen: {screenConfig.name}");
        }

        private void UpdateScreenState(ScreenConfig screenConfig, ScreenBase newScreenInstance,
            bool isMoveBack, object args)
        {
            if (screenConfig == null || newScreenInstance == null)
            {
                Debug.LogError("[ScreenFlow:UpdateScreenState] -> Invalid screen config or instance");
                return;
            }

            CurrentScreenInstance = newScreenInstance;
            if (isMoveBack)
            {
                if (ScreensHistory.Count > 0)
                {
                    ScreensHistory.RemoveAt(ScreensHistory.Count - 1);
                    if (Verbose)
                        Debug.Log("[ScreenFlow:UpdateScreenState] -> Removed screen from history for move back");
                }
                else
                {
                    Debug.LogError("[ScreenFlow:UpdateScreenState] -> Attempted to remove screen from empty history");
                }
            }
            else
            {
                ScreensHistory.Add(new EntityArgPair<ScreenConfig>(screenConfig, args));
                if (Verbose)
                    Debug.Log($"[ScreenFlow:UpdateScreenState] -> Added screen to history: {screenConfig.name}");
            }

            foreach (IPopupBase popup in popupManager.CurrentPopupInstancesStack2)
            {
                popup.ParentScreen = CurrentScreenInstance;
                if (Verbose)
                    Debug.Log(
                        $"[ScreenFlow:UpdateScreenState] -> Updated parent screen for popup: {popup.GameObject.name}");
            }
        }

        private void NotifyScreenChange(ScreenConfig oldScreenConfig, IScreenBase oldScreenInstance,
            ScreenConfig newScreenConfig, IScreenBase newScreenInstance, Action<IScreenBase> onShow)
        {
            if (newScreenInstance == null)
            {
                Debug.LogError("[ScreenFlow:NotifyScreenChange] -> New screen instance is null");
                return;
            }

            onShow?.Invoke(newScreenInstance);
            if (oldScreenConfig == null)
            {
                OnStart?.Invoke(newScreenConfig, newScreenInstance);
                if (Verbose)
                    Debug.Log($"[ScreenFlow:NotifyScreenChange] -> Invoked OnStart for screen: {newScreenConfig.name}");
            }

            OnScreenChange?.Invoke((oldScreenConfig, oldScreenInstance), (newScreenConfig, newScreenInstance));
            CallOnShowForController(newScreenInstance);
            if (Verbose)
                Debug.Log($"[ScreenFlow:NotifyScreenChange] -> Notified screen change to: {newScreenConfig.name}");
        }

        internal void CallOnShowForController(IScreenBase view)
        {
            if (view == null)
            {
                Debug.LogError("[ScreenFlow:CallOnShowForController] -> View is null");
                return;
            }

            Type viewType = view.GetType();
            if (!viewControllers.TryGetValue(viewType, out List<IScreenViewController> controllers))
            {
                if(Verbose)
                    Debug.LogWarning($"[ScreenFlow:CallOnShowForController] -> No controllers found for type {viewType.Name}");
                return;
            }

            foreach (IScreenViewController controller in controllers)
            {
                if (controller == null)
                {
                    Debug.LogError("[ScreenFlow:CallOnShowForController] -> Found null controller in list");
                    continue;
                }

                controller.OnShow(view);
                if (Verbose)
                    Debug.Log(
                        $"[ScreenFlow:CallOnShowForController] -> Called OnShow for controller on view: {view.GameObject.name}");
            }
        }

        internal void CallOnHideForController(IScreenBase view)
        {
            if (view == null)
            {
                Debug.LogError("[ScreenFlow:CallOnHideForController] -> View is null");
                return;
            }

            Type viewType = view.GetType();
            if (!viewControllers.TryGetValue(viewType, out List<IScreenViewController> controllers))
            {
                Debug.LogWarning(
                    $"[ScreenFlow:CallOnHideForController] -> No controllers found for type {viewType.Name}");
                return;
            }

            foreach (IScreenViewController controller in controllers)
            {
                if (controller == null)
                {
                    Debug.LogError("[ScreenFlow:CallOnHideForController] -> Found null controller in list");
                    continue;
                }

                controller.OnHide(view);
                if (Verbose)
                    Debug.Log(
                        $"[ScreenFlow:CallOnHideForController] -> Called OnHide for controller on view: {view.GameObject.name}");
            }
        }

        internal void CallOnGoingToBackgroundForController(IPopupBase view)
        {
            if (view == null)
            {
                Debug.LogError("[ScreenFlow:CallOnGoingToBackgroundForController] -> View is null");
                return;
            }

            Type viewType = view.GetType();
            if (!viewControllers.TryGetValue(viewType, out List<IScreenViewController> controllers))
            {
                Debug.LogWarning(
                    $"[ScreenFlow:CallOnGoingToBackgroundForController] -> No controllers found for type {viewType.Name}");
                return;
            }

            foreach (IScreenViewController controller in controllers)
            {
                if (controller == null)
                {
                    Debug.LogError(
                        "[ScreenFlow:CallOnGoingToBackgroundForController] -> Found null controller in list");
                    continue;
                }

                if (controller is IPopupScreenViewController popupViewController)
                {
                    popupViewController.OnGoingToBackground(view);
                    if (Verbose)
                        Debug.Log(
                            $"[ScreenFlow:CallOnGoingToBackgroundForController] -> Called OnGoingToBackground for controller on view: {view.GameObject.name}");
                }
            }
        }
    }
}