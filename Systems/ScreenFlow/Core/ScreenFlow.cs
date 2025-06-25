using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using Debug = UnityEngine.Debug;
#if ENABLE_INPUT_SYSTEM

#endif

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

        [Header("UI Entities In Scene")] 
        public List<ScreenInScene> ScreensInScene = new List<ScreenInScene>();
        public List<PopupInScene> PopupsInScene = new List<PopupInScene>();

        public bool IsTransiting => screenTransitionRoutine != null || 
                                    popupTransitionRoutine != null || 
                                    transitionRoutine != null;

        public bool IsPreloading => preloadRoutine != null;

        public ScreenConfig CurrentScreenConfig =>
            ScreensHistory.Count > 0 ? ScreensHistory[ScreensHistory.Count - 1].Entity : null;

        public IScreenBase CurrentScreenInstance { private set; get; }

        public PopupConfig CurrentPopupConfig =>
            PopupConfigsStack.Count > 0 ? PopupConfigsStack[PopupConfigsStack.Count - 1] : null;

        public IPopupBase CurrentPopupInstance =>
            PopupInstancesStack.Count > 0 ? PopupInstancesStack[PopupInstancesStack.Count - 1] : null;

        public List<IPopupBase> CurrentPopupInstancesStack => new List<IPopupBase>(PopupInstancesStack);

        public int PopupStackCount => PopupInstancesStack.Count;

        public event Action<ScreenConfig, IScreenBase> OnStart;
        public event Action<(ScreenConfig, IScreenBase), (ScreenConfig, IScreenBase)> OnScreenChange;
        public event Action<(PopupConfig, IPopupBase), (PopupConfig, IPopupBase)> OnPopupOpen;

        protected readonly List<UIEntityBaseConfig> PreloadQueue = new List<UIEntityBaseConfig>();
        protected readonly List<EntityArgPair<ScreenConfig>> ScreensHistory = new List<EntityArgPair<ScreenConfig>>();
        protected readonly List<PopupConfig> PopupConfigsStack = new List<PopupConfig>();
        protected readonly List<IPopupBase> PopupInstancesStack = new List<IPopupBase>();
        protected readonly Dictionary<IPopupBase, Canvas> AllocatedPopupCanvas = new Dictionary<IPopupBase, Canvas>();
        protected readonly List<Canvas> AvailablePopupCanvas = new List<Canvas>();

        protected readonly Dictionary<System.Type, List<IScreenViewController>> viewControllers =
            new Dictionary<System.Type, List<IScreenViewController>>();
        
        private readonly List<ScreenFlowCommand> commandQueue = new List<ScreenFlowCommand>();
        private readonly Dictionary<string, UIEntityBaseConfig> uiEntitiesLookup =
            new Dictionary<string, UIEntityBaseConfig>();

        private Task preloadRoutine;
        private Task screenTransitionRoutine;
        private Task popupTransitionRoutine;
        private Task nextScreenLoading;
        private Task newPopupLoading;
        private Task hideScreenRoutine;
        private Task showScreenRoutine;
        private Task hidePopupRoutine;
        private Task showPopupRoutine;
        private Task transitionRoutine;

        private RectTransform rectTransform;
        private PopupCanvasManager popupCanvasManager;

        public void BindController<T>(IScreenViewController<T> controller)
            where T : IScreenBase
        {
            Type viewType = typeof(T);
            
            if (!viewControllers.ContainsKey(viewType))
                viewControllers.Add(viewType, new List<IScreenViewController>());
            
            if(!viewControllers[viewType].Contains(controller))
                viewControllers[viewType].Add(controller);
        }
        
        public void UnBindController<T>(IScreenViewController<T> controller)
            where T : IScreenBase
        {
            Type viewType = typeof(T);
            
            if (!viewControllers.TryGetValue(viewType, out List<IScreenViewController> controllers))
            {
                if (controllers.Contains(controller))
                    controllers.Remove(controller);
            }
        }
        
        public void SendTrigger(string name, System.Object args = null, bool enqueue = true, 
            Action<IScreenBase> requestedScreenOnShow = null, Action<IScreenBase> previousScreenOnHide = null)
        {
            if (uiEntitiesLookup.TryGetValue(name, out UIEntityBaseConfig uiEntityBaseConfig))
            {
                SendTrigger(uiEntityBaseConfig, args, enqueue, requestedScreenOnShow, previousScreenOnHide);
            }
        }

        public void SendTrigger(UIEntityBaseConfig uiEntity, System.Object args = null, bool enqueue = true, 
            Action<IScreenBase> requestedScreenOnShow = null, Action<IScreenBase> previousScreenOnHide = null)
        {
            ScreenFlowCommand command = new ScreenFlowCommand(ScreenFlowCommandType.Trigger, uiEntity, args, requestedScreenOnShow, previousScreenOnHide);
            if (!IsTransiting)
            {
                commandQueue.Add(command);
                transitionRoutine ??= ProcessCommandQueue();
            }
            else
            {
                if (enqueue)
                {
                    commandQueue.Add(command);
                }
            }
        }

        public void SendTriggerT<TConfig, TShow>(TConfig uiEntity, TShow args = null, bool enqueue = true,
            Action<IScreenBase> requestedScreenOnShow = null, Action<IScreenBase> previousScreenOnHide = null)
            where TConfig : UIEntityBaseConfig
            where TShow : class
        {
            SendTrigger(uiEntity, args , enqueue, requestedScreenOnShow, previousScreenOnHide);
        }
        
        public void SendTriggerT<TConfig, TShow, THide>(TConfig uiEntity, TShow args = null, bool enqueue = true,
            Action<ScreenBaseT<TConfig, TShow, THide>> requestedScreenOnShow = null, Action<IScreenBase> previousScreenOnHide = null)
            where TConfig : UIEntityBaseConfig
            where TShow : class
            where THide : class
        {
            void RequestedScreenTOnShow(IScreenBase screenBase)
            {
                if (screenBase is ScreenBaseT<TConfig, TShow, THide> screenBaseT)
                {
                    requestedScreenOnShow?.Invoke(screenBaseT);
                }
            }
            
            SendTrigger(uiEntity, args , enqueue, RequestedScreenTOnShow, previousScreenOnHide);
        }

        public void MoveBack(System.Object args = null, bool enqueue = true, Action<IScreenBase> onShow = null, Action<IScreenBase> onHide = null)
        {
            ScreenFlowCommand command = new ScreenFlowCommand(ScreenFlowCommandType.MoveBack, null, args, onShow, onHide);
            if (!IsTransiting)
            {
                commandQueue.Add(command);
                transitionRoutine ??= ProcessCommandQueue();
            }
            else
            {
                if (enqueue)
                {
                    commandQueue.Add(command);
                }
            }
        }

        public void CloseForegroundPopup(System.Object args = null, bool enqueue = true, Action<IScreenBase> onShow = null, Action<IScreenBase> onHide = null)
        {
            if (CurrentPopupInstance != null)
            {
                ClosePopup(CurrentPopupInstance, args, enqueue, onShow, onHide);
            }
        }

        public void ClosePopup(IPopupBase popupBase, System.Object args = null, bool enqueue = true, Action<IScreenBase> onShow = null, Action<IScreenBase> onHide = null)
        {
            var command = new ScreenFlowCommand(ScreenFlowCommandType.ClosePopup, popupBase, args, onShow, onHide);
            if (!IsTransiting)
            {
                commandQueue.Add(command);
                transitionRoutine ??= ProcessCommandQueue();
            }
            else
            {
                if (enqueue)
                {
                    commandQueue.Add(command);
                }
            }
        }
#if SCREEN_FLOW_SINGLETON
        protected override void Awake()
        {
            base.Awake();

            rectTransform = GetComponent<RectTransform>();
        }
#else
        protected void Awake()
        {
            rectTransform = GetComponent<RectTransform>();
        }
#endif

#if SCREEN_FLOW_SINGLETON
        protected override async void Start()
        {
            base.Start();
            if(AutoInitializeOnStart)
                await Initialize();
        }
#else
        protected async void Start()
        {
            if(AutoInitializeOnStart)
                await Initialize();
        }
#endif

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
            
            popupCanvasManager = new PopupCanvasManager(Config, GetComponent<Canvas>(), GetComponent<CanvasScaler>(),
                GetComponent<GraphicRaycaster>());
            
            PopulateUIEntitiesLookup();
            ProcessInSceneEntities();
            
            if (StartScreen != null)
            {
                SendTrigger(StartScreen);
            }
            
            await Preload();
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
            ProcessInSceneScreens();
            ProcessInScenePopups();
        }

        private void ProcessInSceneScreens()
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
        }

        private void ProcessInScenePopups()
        {
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

        private async Task ProcessCommandQueue()
        {
            while (commandQueue.Count > 0)
            {
                ScreenFlowCommand next = commandQueue[0];
                commandQueue.RemoveAt(0);

                switch (next.Type)
                {
                    case ScreenFlowCommandType.Trigger:
                    {
                        if (next.Object is ScreenConfig screenConfig)
                        {
                            await ScreenTransitTo(screenConfig, false, next.Args, next.OnShow, next.OnHide);
                        }
                        else if (next.Object is PopupConfig popupConfig)
                        {
                            if (CurrentScreenConfig != null)
                            {
                                if (CurrentScreenConfig.AllowPopups)
                                {
                                    await PopupTransitTo(popupConfig, next.Args, next.OnShow, next.OnHide);
                                }
                            }
                        }
                        break;
                    }
                    case ScreenFlowCommandType.MoveBack:
                    {
                        await MoveBackOp(next.Args, next.OnShow, next.OnHide);
                        break;
                    }
                    case ScreenFlowCommandType.ClosePopup:
                    {
                        await ClosePopupOp(next.Object as IPopupBase, next.Args, next.OnShow, next.OnHide);
                        break;
                    }
                }
            }

            transitionRoutine = null;
        }

        private async Task MoveBackOp(System.Object args, Action<IScreenBase> onShow = null, Action<IScreenBase> onHide = null)
        {
            EntityArgPair<ScreenConfig> previousScreenConfig = ScreensHistory.Count > 1 ? ScreensHistory[ScreensHistory.Count - 2] : null;
            if (previousScreenConfig != null)
            {
                if (CurrentScreenConfig.CanMoveBackFromHere && previousScreenConfig.Entity.CanMoveBackToHere)
                {
                    await ScreenTransitTo(previousScreenConfig.Entity, 
                        true, args ?? previousScreenConfig.Args, onShow, onHide);
                }
            }
        }
        
        private async Task ClosePopupOp(IPopupBase popupBase, System.Object args, Action<IScreenBase> onShow = null, 
            Action<IScreenBase> onHide = null)
        {
            int stackIndex = PopupInstancesStack.FindIndex(item => item == popupBase);

            if (stackIndex >= 0)
            {
                bool isTopOfStack = stackIndex == PopupInstancesStack.Count - 1;
                PopupConfig popupConfig = PopupConfigsStack[stackIndex];

                PopupConfig behindPopupConfig = null;
                IPopupBase behindPopupInstance = null;

                if (stackIndex - 1 >= 0)
                {
                    behindPopupConfig = PopupConfigsStack[stackIndex - 1];
                    behindPopupInstance = PopupInstancesStack[stackIndex - 1];
                }

                if (isTopOfStack)
                {
                    switch (popupConfig.AnimationType)
                    {
                        case AnimationType.NoAnimation:
                        case AnimationType.Wait:
                        {
                            //Wait for hide's animation to complete
                            await popupBase.RequestHide(args);
                            onHide?.Invoke(CurrentPopupInstance);
                            CallOnHideForController(popupBase);
                            DisposePopupFromHide(popupConfig, popupBase);
                            break;
                        }
                        case AnimationType.Intersection:
                        {
                            hidePopupRoutine = popupBase.RequestHide(args);
                            break;
                        }
                    }

                    if (behindPopupInstance != null && 
                        (behindPopupConfig.GoingBackgroundBehaviour == PopupGoingBackgroundBehaviour.JustHide ||
                         behindPopupConfig.GoingBackgroundBehaviour == PopupGoingBackgroundBehaviour.HideAndDestroy))
                    {
                        switch (popupConfig.AnimationType)
                        {
                            case AnimationType.NoAnimation:
                            case AnimationType.Wait:
                            {
                                //Wait for shows's animation to complete
                                await behindPopupInstance.Show(args);
                                break;
                            }
                            case AnimationType.Intersection:
                            {
                                //Show animation starts playing (may be playing in parallel with hide's animation)
                                showPopupRoutine = behindPopupInstance.Show(args);
                                break;
                            }
                        }
                    }

                    if (hidePopupRoutine != null) //If we were waiting for hide's animation
                    {
                        await hidePopupRoutine; //Wait for hide's animation to complete
                        onHide?.Invoke(CurrentPopupInstance);
                        CallOnHideForController(popupBase);
                        DisposePopupFromHide(CurrentPopupConfig, CurrentPopupInstance);
                        hidePopupRoutine = null;
                    }

                    if (showPopupRoutine != null) //If we were waiting for show's animation
                    {
                        await showPopupRoutine; //Wait for show's animation to complete
                        showPopupRoutine = null;
                    }
                }
                else
                {
                    onHide?.Invoke(popupBase);
                    DisposePopupFromHide(popupConfig, popupBase);
                }
                
                onShow?.Invoke(behindPopupInstance);
            }

            popupTransitionRoutine = null;
        }

        private async Task Preload()
        {
            PreloadQueue.Clear();
            
            ScreenConfig[] screens = Array.FindAll(Config.Screens, item => item.AssetLoaderConfig.PreLoad);
            
            if(screens.Length > 0)
                PreloadQueue.AddRange(screens);

            PopupConfig[] popups = Array.FindAll(Config.Popups, item => item.AssetLoaderConfig.PreLoad);
            
            if(popups.Length > 0)
                PreloadQueue.AddRange(Array.FindAll(Config.Popups, item => item.AssetLoaderConfig.PreLoad));

            await PreloadingAssets();
        }

        private async Task PreloadingAssets()
        {
            foreach (UIEntityBaseConfig uiEntityBaseConfig in PreloadQueue)
            {
                await uiEntityBaseConfig.AssetLoaderConfig.WaitForLoadingAsync<GameObject>();
            }
        }

        private async Task ScreenTransitTo(ScreenConfig screenConfig, bool isMoveBack = false,
            System.Object args = null, Action<IScreenBase> onShow = null, Action<IScreenBase> onHide = null)
        {
            await StartScreenTransition(screenConfig, args);
            
            ScreenConfig oldScreenConfig = CurrentScreenConfig;
            IScreenBase oldScreenInstance = CurrentScreenInstance;
            
            await HideCurrentScreen(oldScreenConfig, oldScreenInstance, args, onHide);
            
            ScreenBase newScreenInstance = null;
            await LoadAndInstantiateScreen(screenConfig, instance => newScreenInstance = instance);
            if (newScreenInstance == null) return;
            
            await ShowNewScreen(screenConfig, newScreenInstance, args);
            
            UpdateScreenState(screenConfig, newScreenInstance, isMoveBack, args);
            NotifyScreenChange(oldScreenConfig, oldScreenInstance, screenConfig, newScreenInstance, onShow);
        }

        private async Task StartScreenTransition(ScreenConfig screenConfig, System.Object args)
        {
            if (!screenConfig.AssetLoaderConfig.IsLoaded && !screenConfig.AssetLoaderConfig.IsLoading)
            {
                if (PreloadQueue.Contains(screenConfig))
                {
                    PreloadQueue.Remove(screenConfig);
                }
                nextScreenLoading = screenConfig.AssetLoaderConfig.WaitForLoadingAsync<GameObject>();
            }
            
            await HandlePopupsOnScreenTransit(args);
        }

        private async Task HideCurrentScreen(ScreenConfig oldScreenConfig, IScreenBase oldScreenInstance, 
            System.Object args, Action<IScreenBase> onHide)
        {
            if (oldScreenConfig == null || oldScreenInstance == null) return;

            switch (oldScreenConfig.AnimationType)
            {
                case AnimationType.NoAnimation:
                case AnimationType.Wait:
                    await oldScreenInstance.RequestHide(args);
                    HandleScreenHideCompletion(oldScreenConfig, oldScreenInstance, onHide);
                    break;
                case AnimationType.Intersection:
                    hideScreenRoutine = oldScreenInstance.RequestHide(args);
                    break;
            }
        }

        private void HandleScreenHideCompletion(ScreenConfig screenConfig, IScreenBase screenInstance, 
            Action<IScreenBase> onHide)
        {
            onHide?.Invoke(screenInstance);
            CallOnHideForController(screenInstance);

            if (screenConfig.AssetLoaderConfig.IsInScene)
            {
                screenInstance.GameObject.SetActive(false);
            }
            else
            {
                Destroy(screenInstance.GameObject);
                if (!screenConfig.AssetLoaderConfig.DontUnloadAfterLoad)
                {
                    screenConfig.AssetLoaderConfig.Unload();
                }
            }
        }

        private async Task LoadAndInstantiateScreen(ScreenConfig screenConfig, Action<ScreenBase> onInstanceCreated)
        {
            if (nextScreenLoading != null)
            {
                await nextScreenLoading;
                nextScreenLoading = null;
            }
            else if (!screenConfig.AssetLoaderConfig.IsLoaded)
            {
                while (!screenConfig.AssetLoaderConfig.IsLoaded)
                {
                    await Task.Yield();
                }
            }

            if (screenConfig.AssetLoaderConfig.LoadedAsset == null)
            {
                Debug.LogError($"[ScreenFlow:ScreenTransitTo()] -> Failed to load {screenConfig.AssetLoaderConfig.name}", 
                    screenConfig);
                return;
            }

            ScreenBase newScreenPrefab = GetScreenPrefab(screenConfig);
            if (newScreenPrefab == null)
            {
                Debug.LogError(
                    $"[ScreenFlow:ScreenTransitTo()] -> {screenConfig.AssetLoaderConfig.LoadedAsset.GetType()} doesn't have any component that inherits from ScreenBase class", 
                    screenConfig);
                return;
            }

            ScreenBase newScreenInstance;
            if (screenConfig.AssetLoaderConfig.IsInScene)
            {
                newScreenInstance = newScreenPrefab;
                newScreenInstance.gameObject.SetActive(true);
            }
            else
            {
                newScreenInstance = InstantiateUIElement<ScreenBase>(newScreenPrefab, rectTransform, 
                    out RectTransform instanceRT, out RectTransform prefabRT);
            }

            onInstanceCreated?.Invoke(newScreenInstance);
        }

        private ScreenBase GetScreenPrefab(ScreenConfig screenConfig)
        {
            switch (screenConfig.AssetLoaderConfig.LoadedAsset)
            {
                case GameObject screenGameObject:
                    return screenGameObject.GetComponent<ScreenBase>();
                case ScreenBase screenBase:
                    return screenBase;
                default:
                    return null;
            }
        }

        private async Task ShowNewScreen(ScreenConfig screenConfig, ScreenBase newScreenInstance, System.Object args)
        {
            switch (screenConfig.AnimationType)
            {
                case AnimationType.NoAnimation:
                case AnimationType.Wait:
                    await newScreenInstance.Show(args);
                    break;
                case AnimationType.Intersection:
                    showScreenRoutine = newScreenInstance.Show(args);
                    break;
            }

            if (hideScreenRoutine != null)
            {
                await hideScreenRoutine;
                HandleScreenHideCompletion(CurrentScreenConfig, CurrentScreenInstance, null);
                hideScreenRoutine = null;
            }

            if (showScreenRoutine != null)
            {
                await showScreenRoutine;
                showScreenRoutine = null;
            }
        }

        private void UpdateScreenState(ScreenConfig screenConfig, ScreenBase newScreenInstance, 
            bool isMoveBack, System.Object args)
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

            foreach (IPopupBase popup in PopupInstancesStack)
            {
                popup.ParentScreen = CurrentScreenInstance;
            }

            screenTransitionRoutine = null;
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

        private void CallOnShowForController(IScreenBase view)
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
        
        private void CallOnHideForController(IScreenBase view)
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
        
        private void CallOnGoingToBackgroundForController(IPopupBase view)
        {
            Type viewType = view.GetType();
            if (viewControllers.TryGetValue(viewType, out List<IScreenViewController> controllers))
            {
                foreach (IScreenViewController controller in controllers)
                {
                    if(controller is not IPopupScreenViewController popupViewController) continue;
                    popupViewController.OnGoingToBackground(view);
                }
            }
        }

        private async Task PopupTransitTo(PopupConfig popupConfig, System.Object args = null, 
            Action<IScreenBase> onShow = null, Action<IScreenBase> onHide = null)
        {
            await StartPopupTransition(popupConfig, args);
            
            PopupConfig oldPopupConfig = CurrentPopupConfig;
            IPopupBase oldPopupInstance = CurrentPopupInstance;
            
            await HandleCurrentPopup(oldPopupConfig, oldPopupInstance, args, popupConfig);
            
            IPopupBase newPopupInstance = null;
            Canvas canvasPopup = null;
            await LoadAndInstantiatePopup(popupConfig, instance => newPopupInstance = instance, canvas => canvasPopup = canvas);
            if (newPopupInstance == null) return;
            
            await ShowNewPopup(popupConfig, newPopupInstance, canvasPopup, args);
            
            UpdatePopupState(popupConfig, newPopupInstance);
            NotifyPopupChange(oldPopupConfig, oldPopupInstance, popupConfig, newPopupInstance, onShow);
        }

        private async Task StartPopupTransition(PopupConfig popupConfig, System.Object args)
        {
            if (!popupConfig.AssetLoaderConfig.IsLoaded)
            {
                await popupConfig.AssetLoaderConfig.WaitForLoadingAsync<GameObject>();
            }
        }

        private async Task HandleCurrentPopup(PopupConfig oldPopupConfig, IPopupBase oldPopupInstance, 
            System.Object args, PopupConfig newPopupConfig)
        {
            if (oldPopupConfig == null || oldPopupInstance == null) return;

            bool allowStackablePopups = CurrentScreenConfig.AllowStackablePopups;
            
            switch (oldPopupConfig.AnimationType)
            {
                case AnimationType.NoAnimation:
                case AnimationType.Wait:
                    if (allowStackablePopups)
                    {
                        oldPopupInstance.GoToBackground(args);
                        CallOnGoingToBackgroundForController(oldPopupInstance);
                        if (oldPopupConfig.GoingBackgroundBehaviour != PopupGoingBackgroundBehaviour.DontHide)
                        {
                            await oldPopupInstance.RequestHide(args);
                            CallOnHideForController(oldPopupInstance);
                            if (oldPopupConfig.GoingBackgroundBehaviour == PopupGoingBackgroundBehaviour.HideAndDestroy)
                            {
                                DisposePopupFromHide(oldPopupConfig, oldPopupInstance, oldPopupConfig == newPopupConfig);
                            }
                        }
                    }
                    else
                    {
                        await oldPopupInstance.RequestHide(args);
                        CallOnHideForController(oldPopupInstance);
                        DisposePopupFromHide(oldPopupConfig, oldPopupInstance, oldPopupConfig == newPopupConfig);
                    }
                    break;
                case AnimationType.Intersection:
                    if (allowStackablePopups)
                    {
                        oldPopupInstance.GoToBackground(args);
                        CallOnGoingToBackgroundForController(oldPopupInstance);
                        if (oldPopupConfig.GoingBackgroundBehaviour != PopupGoingBackgroundBehaviour.DontHide)
                        {
                            hidePopupRoutine =oldPopupInstance.RequestHide(args);
                        }
                    }
                    else
                    {
                        hidePopupRoutine = oldPopupInstance.RequestHide(args);
                    }
                    break;
            }
        }

        private async Task LoadAndInstantiatePopup(PopupConfig popupConfig, Action<IPopupBase> onInstanceCreated, 
            Action<Canvas> onCanvasAssigned)
        {
            if (popupConfig.AssetLoaderConfig.LoadedAsset == null)
            {
                Debug.LogError($"[ScreenFlow:PopupTransitTo()] -> Failed to load {popupConfig.AssetLoaderConfig.name}", popupConfig);
                return;
            }

            IPopupBase newPopupInstance = GetPopupInstance(popupConfig);
            if (newPopupInstance == null)
            {
                Debug.LogError($"[ScreenFlow:PopupTransitTo()] -> {popupConfig.AssetLoaderConfig.LoadedAsset.GetType()} doesn't have any component that inherits from {nameof(IPopupBase)} interface", popupConfig);
                return;
            }

            Canvas canvasPopup;
            if (popupConfig.AssetLoaderConfig.IsInScene)
            {
                canvasPopup = GetComponentInParent<Canvas>() ?? GetComponentInChildren<Canvas>();
                newPopupInstance.GameObject.SetActive(true);
            }
            else
            {
                canvasPopup = popupCanvasManager.GetCanvas(newPopupInstance);
                if (canvasPopup != null)
                {
                    newPopupInstance = InstantiateUIElement(newPopupInstance as ScreenBase, null, 
                        out RectTransform instanceRT, out RectTransform prefabRT) as IPopupBase;
                }
                else
                {
                    newPopupInstance = InstantiateUIElement(newPopupInstance as ScreenBase, null, 
                        out RectTransform instanceRT, out RectTransform prefabRT) as IPopupBase;
                    canvasPopup = popupCanvasManager.AllocatePopupCanvas(newPopupInstance);
                    ReparentUIElement(instanceRT, prefabRT, canvasPopup.transform);
                }
            }

            onInstanceCreated?.Invoke(newPopupInstance);
            onCanvasAssigned?.Invoke(canvasPopup);
        }

        private IPopupBase GetPopupInstance(PopupConfig popupConfig)
        {
            switch (popupConfig.AssetLoaderConfig.LoadedAsset)
            {
                case GameObject screenGameObject:
                    return screenGameObject.GetComponent<ScreenBase>() as IPopupBase;
                case PopupBase popupBase:
                    return popupBase;
                case ScreenBase screenBase:
                    return screenBase as IPopupBase;
                default:
                    return null;
            }
        }

        private async Task ShowNewPopup(PopupConfig popupConfig, IPopupBase newPopupInstance, 
            Canvas canvasPopup, System.Object args)
        {
            newPopupInstance.ParentScreen = CurrentScreenInstance;
            popupCanvasManager.CalculatePopupCanvasSortOrder(canvasPopup, CurrentPopupInstance);

            switch (popupConfig.AnimationType)
            {
                case AnimationType.NoAnimation:
                case AnimationType.Wait:
                    await newPopupInstance.Show(args);
                    break;
                case AnimationType.Intersection:
                    showPopupRoutine = newPopupInstance.Show(args);
                    break;
            }

            if (hidePopupRoutine != null)
            {
                await hidePopupRoutine;
                CallOnHideForController(CurrentPopupInstance);
                if (CurrentPopupConfig.GoingBackgroundBehaviour == PopupGoingBackgroundBehaviour.HideAndDestroy)
                {
                    DisposePopupFromHide(CurrentPopupConfig, CurrentPopupInstance);
                }
                hidePopupRoutine = null;
            }

            if (showPopupRoutine != null)
            {
                await showPopupRoutine;
                showPopupRoutine = null;
            }
        }

        private void UpdatePopupState(PopupConfig popupConfig, IPopupBase newPopupInstance)
        {
            newPopupInstance.OnClosePopupRequest += OnClosePopupRequest;
            PopupConfigsStack.Add(popupConfig);
            PopupInstancesStack.Add(newPopupInstance);
            popupTransitionRoutine = null;
        }

        private void NotifyPopupChange(PopupConfig oldPopupConfig, IPopupBase oldPopupInstance, 
            PopupConfig newPopupConfig, IPopupBase newPopupInstance, Action<IScreenBase> onShow)
        {
            onShow?.Invoke(newPopupInstance);
            OnPopupOpen?.Invoke((oldPopupConfig, oldPopupInstance), (newPopupConfig, newPopupInstance));
            CallOnShowForController(newPopupInstance);
        }

        private void OnClosePopupRequest(IPopupBase popupToClose)
        {
            ClosePopup(popupToClose);
        }

        private async Task HandlePopupsOnScreenTransit(System.Object args = null)
        {
            if (CurrentPopupInstance != null)
            {
                switch (CurrentScreenConfig.PopupBehaviourOnScreenTransition)
                {
                    case PopupsBehaviourOnScreenTransition.PreserveAllOnHide:
                    {
                        //Just keep going
                        break;
                    }
                    case PopupsBehaviourOnScreenTransition.HideFirstThenTransit:
                    {
                        await ClosePopupOp(CurrentPopupInstance, args);
                        for (int i = PopupConfigsStack.Count - 1; i >= 0; i--)
                        {
                            DisposePopupFromHide(PopupConfigsStack[i], PopupInstancesStack[i]);
                        }

                        break;
                    }
                    case PopupsBehaviourOnScreenTransition.DestroyAllThenTransit:
                    {
                        for (int i = PopupConfigsStack.Count - 1; i >= 0; i--)
                        {
                            DisposePopupFromHide(PopupConfigsStack[i], PopupInstancesStack[i]);
                        }

                        break;
                    }
                }
            }
        }

        private void DisposePopupFromHide(PopupConfig popupConfig, IPopupBase popupInstance, bool forceDontUnload = false)
        {
            //Remove current popup from stack
            PopupConfigsStack.Remove(popupConfig);
            PopupInstancesStack.Remove(popupInstance);
            popupCanvasManager.RecyclePopupCanvas(popupInstance);

            popupInstance.OnClosePopupRequest -= OnClosePopupRequest;

            if (popupConfig.AssetLoaderConfig.IsInScene)
            {
                popupInstance.GameObject.SetActive(false);
            }
            else
            {
                Destroy(popupInstance.GameObject);

                if (!popupConfig.AssetLoaderConfig.DontUnloadAfterLoad)
                {
                    if (forceDontUnload == false)
                    {
                        popupConfig.AssetLoaderConfig.Unload();
                    }
                }
            }
        }

        private T InstantiateUIElement<T>(T screenPrefab, Transform parent, out RectTransform elementInstanceRT,
            out RectTransform elementPrefabRT)
            where T : ScreenBase
        {
            T elementInstance = Instantiate(screenPrefab);
            elementInstanceRT = elementInstance.GetComponent<RectTransform>();
            elementPrefabRT = screenPrefab.GetComponent<RectTransform>();

            ReparentUIElement(elementInstanceRT, elementPrefabRT, parent);

            return elementInstance;
        }

        private void ReparentUIElement(RectTransform elementInstanceRT, RectTransform elementPrefabRT, Transform parent)
        {
            elementInstanceRT.SetParent(parent);
            elementInstanceRT.SetAsLastSibling();
            elementInstanceRT.localPosition = elementPrefabRT.position;
            elementInstanceRT.localRotation = elementPrefabRT.rotation;
            elementInstanceRT.localScale = elementPrefabRT.localScale;

            elementInstanceRT.anchoredPosition3D = elementPrefabRT.anchoredPosition3D;
            elementInstanceRT.anchorMin = elementPrefabRT.anchorMin;
            elementInstanceRT.anchorMax = elementPrefabRT.anchorMax;
            elementInstanceRT.sizeDelta = elementPrefabRT.sizeDelta;
            elementInstanceRT.offsetMin = elementPrefabRT.offsetMin;
            elementInstanceRT.offsetMax = elementPrefabRT.offsetMax;
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
                case BackKeyBehaviour.ScreenMoveBack: MoveBack(); break;
                case BackKeyBehaviour.CloseFirstPopup:
                    if (CurrentPopupInstance != null)
                        CloseForegroundPopup();
                    else MoveBack();
                    break;
            }
        }

        public void Dispose()
        {
        }
    }
}