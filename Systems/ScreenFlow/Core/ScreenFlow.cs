using System;
using System.Collections;
using System.Collections.Generic;
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

        private Coroutine preloadRoutine;
        private Coroutine screenTransitionRoutine;
        private Coroutine popupTransitionRoutine;
        private Coroutine nextScreenLoading;
        private Coroutine newPopupLoading;
        private Coroutine hideScreenRoutine;
        private Coroutine showScreenRoutine;
        private Coroutine hidePopupRoutine;
        private Coroutine showPopupRoutine;
        private Coroutine transitionRoutine;

        private RectTransform rectTransform;
        private Canvas canvas;
        private CanvasScaler canvasScaler;
        private GraphicRaycaster graphicRaycaster;

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
                transitionRoutine ??= StartCoroutine(ProcessCommandQueue());
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
                transitionRoutine ??= StartCoroutine(ProcessCommandQueue());
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
                transitionRoutine ??= StartCoroutine(ProcessCommandQueue());
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
            canvas = GetComponent<Canvas>();
            canvasScaler = GetComponent<CanvasScaler>();
            graphicRaycaster = GetComponent<GraphicRaycaster>();
        }
#else
        protected void Awake()
        {
            rectTransform = GetComponent<RectTransform>();
            canvas = GetComponent<Canvas>();
            canvasScaler = GetComponent<CanvasScaler>();
            graphicRaycaster = GetComponent<GraphicRaycaster>();
        }
#endif

#if SCREEN_FLOW_SINGLETON
        protected override void Start()
        {
            base.Start();
            if(AutoInitializeOnStart)
                Initialize();
        }
#else
        protected void Start()
        {
            if(AutoInitializeOnStart)
                Initialize();
        }
#endif
        IEnumerator PreLoadRoutine()
        {
            yield return Preload();
            preloadRoutine = null;
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
        
        public void Initialize()
        {
            if (!ValidateConfig()) return;
            
            PopulateUIEntitiesLookup();
            ProcessInSceneEntities();
            
            if (StartScreen != null)
            {
                SendTrigger(StartScreen);
            }
            
            preloadRoutine = StartCoroutine(PreLoadRoutine());
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

        private IEnumerator ProcessCommandQueue()
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
                            yield return ScreenTransitTo(screenConfig, false, next.Args, next.OnShow, next.OnHide);
                        }
                        else if (next.Object is PopupConfig popupConfig)
                        {
                            if (CurrentScreenConfig != null)
                            {
                                if (CurrentScreenConfig.AllowPopups)
                                {
                                    yield return PopupTransitTo(popupConfig, next.Args, next.OnShow, next.OnHide);
                                }
                            }
                        }
                        break;
                    }
                    case ScreenFlowCommandType.MoveBack:
                    {
                        yield return MoveBackOp(next.Args, next.OnShow, next.OnHide);
                        break;
                    }
                    case ScreenFlowCommandType.ClosePopup:
                    {
                        yield return ClosePopupOp(next.Object as IPopupBase, next.Args, next.OnShow, next.OnHide);
                        break;
                    }
                }
            }

            transitionRoutine = null;
        }

        private IEnumerator MoveBackOp(System.Object args, Action<IScreenBase> onShow = null, Action<IScreenBase> onHide = null)
        {
            EntityArgPair<ScreenConfig> previousScreenConfig = ScreensHistory.Count > 1 ? ScreensHistory[ScreensHistory.Count - 2] : null;
            if (previousScreenConfig != null)
            {
                if (CurrentScreenConfig.CanMoveBackFromHere && previousScreenConfig.Entity.CanMoveBackToHere)
                {
                    yield return ScreenTransitTo(previousScreenConfig.Entity, 
                        true, args ?? previousScreenConfig.Args, onShow, onHide);
                }
            }
        }
        
        private IEnumerator ClosePopupOp(IPopupBase popupBase, System.Object args, Action<IScreenBase> onShow = null, 
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
                            yield return popupBase.RequestHide(args);
                            onHide?.Invoke(CurrentPopupInstance);
                            CallOnHideForController(popupBase);
                            DisposePopupFromHide(popupConfig, popupBase);
                            break;
                        }
                        case AnimationType.Intersection:
                        {
                            hidePopupRoutine = StartCoroutine(popupBase.RequestHide(args));
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
                                yield return behindPopupInstance.Show(args);
                                break;
                            }
                            case AnimationType.Intersection:
                            {
                                //Show animation starts playing (may be playing in parallel with hide's animation)
                                showPopupRoutine = StartCoroutine(behindPopupInstance.Show(args));
                                break;
                            }
                        }
                    }

                    if (hidePopupRoutine != null) //If we were waiting for hide's animation
                    {
                        yield return hidePopupRoutine; //Wait for hide's animation to complete
                        onHide?.Invoke(CurrentPopupInstance);
                        CallOnHideForController(popupBase);
                        DisposePopupFromHide(CurrentPopupConfig, CurrentPopupInstance);
                        hidePopupRoutine = null;
                    }

                    if (showPopupRoutine != null) //If we were waiting for show's animation
                    {
                        yield return showPopupRoutine; //Wait for show's animation to complete
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

        private IEnumerator Preload()
        {
            PreloadQueue.Clear();
            
            ScreenConfig[] screens = Array.FindAll(Config.Screens, item => item.AssetLoaderConfig.PreLoad);
            
            if(screens.Length > 0)
                PreloadQueue.AddRange(screens);

            PopupConfig[] popups = Array.FindAll(Config.Popups, item => item.AssetLoaderConfig.PreLoad);
            
            if(popups.Length > 0)
                PreloadQueue.AddRange(Array.FindAll(Config.Popups, item => item.AssetLoaderConfig.PreLoad));

            yield return PreloadingAssets();
        }

        private IEnumerator PreloadingAssets()
        {
            foreach (UIEntityBaseConfig uiEntityBaseConfig in PreloadQueue)
            {
                uiEntityBaseConfig.AssetLoaderConfig.PrepareLoadRoutine<GameObject>();
                yield return uiEntityBaseConfig.AssetLoaderConfig.WaitLoadRoutine();
            }
        }

        private IEnumerator ScreenTransitTo(ScreenConfig screenConfig, bool isMoveBack = false,
            System.Object args = null, Action<IScreenBase> onShow = null, Action<IScreenBase> onHide = null)
        {
            yield return StartScreenTransition(screenConfig, args);
            
            ScreenConfig oldScreenConfig = CurrentScreenConfig;
            IScreenBase oldScreenInstance = CurrentScreenInstance;
            
            yield return HideCurrentScreen(oldScreenConfig, oldScreenInstance, args, onHide);
            
            ScreenBase newScreenInstance = null;
            yield return LoadAndInstantiateScreen(screenConfig, instance => newScreenInstance = instance);
            if (newScreenInstance == null) yield break;
            
            yield return ShowNewScreen(screenConfig, newScreenInstance, args);
            
            UpdateScreenState(screenConfig, newScreenInstance, isMoveBack, args);
            NotifyScreenChange(oldScreenConfig, oldScreenInstance, screenConfig, newScreenInstance, onShow);
        }

        private IEnumerator StartScreenTransition(ScreenConfig screenConfig, System.Object args)
        {
            if (!screenConfig.AssetLoaderConfig.IsLoaded && !screenConfig.AssetLoaderConfig.IsLoading)
            {
                if (PreloadQueue.Contains(screenConfig))
                {
                    PreloadQueue.Remove(screenConfig);
                }
                screenConfig.AssetLoaderConfig.PrepareLoadRoutine<GameObject>();
                nextScreenLoading = StartCoroutine(screenConfig.AssetLoaderConfig.WaitLoadRoutine());
            }
            
            yield return HandlePopupsOnScreenTransit(args);
        }

        private IEnumerator HideCurrentScreen(ScreenConfig oldScreenConfig, IScreenBase oldScreenInstance, 
            System.Object args, Action<IScreenBase> onHide)
        {
            if (oldScreenConfig == null || oldScreenInstance == null) yield break;

            switch (oldScreenConfig.AnimationType)
            {
                case AnimationType.NoAnimation:
                case AnimationType.Wait:
                    yield return oldScreenInstance.RequestHide(args);
                    HandleScreenHideCompletion(oldScreenConfig, oldScreenInstance, onHide);
                    break;
                case AnimationType.Intersection:
                    hideScreenRoutine = StartCoroutine(oldScreenInstance.RequestHide(args));
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

        private IEnumerator LoadAndInstantiateScreen(ScreenConfig screenConfig, Action<ScreenBase> onInstanceCreated)
        {
            if (nextScreenLoading != null)
            {
                yield return nextScreenLoading;
                nextScreenLoading = null;
            }
            else if (!screenConfig.AssetLoaderConfig.IsLoaded)
            {
                while (!screenConfig.AssetLoaderConfig.IsLoaded)
                {
                    yield return null;
                }
            }

            if (screenConfig.AssetLoaderConfig.LoadedAsset == null)
            {
                Debug.LogError($"[ScreenFlow:ScreenTransitTo()] -> Failed to load {screenConfig.AssetLoaderConfig.name}", 
                    screenConfig);
                yield break;
            }

            ScreenBase newScreenPrefab = GetScreenPrefab(screenConfig);
            if (newScreenPrefab == null)
            {
                Debug.LogError(
                    $"[ScreenFlow:ScreenTransitTo()] -> {screenConfig.AssetLoaderConfig.LoadedAsset.GetType()} doesn't have any component that inherits from ScreenBase class", 
                    screenConfig);
                yield break;
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

        private IEnumerator ShowNewScreen(ScreenConfig screenConfig, ScreenBase newScreenInstance, System.Object args)
        {
            switch (screenConfig.AnimationType)
            {
                case AnimationType.NoAnimation:
                case AnimationType.Wait:
                    yield return newScreenInstance.Show(args);
                    break;
                case AnimationType.Intersection:
                    showScreenRoutine = StartCoroutine(newScreenInstance.Show(args));
                    break;
            }

            if (hideScreenRoutine != null)
            {
                yield return hideScreenRoutine;
                HandleScreenHideCompletion(CurrentScreenConfig, CurrentScreenInstance, null);
                hideScreenRoutine = null;
            }

            if (showScreenRoutine != null)
            {
                yield return showScreenRoutine;
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

        private IEnumerator PopupTransitTo(PopupConfig popupConfig, System.Object args = null, 
            Action<IScreenBase> onShow = null, Action<IScreenBase> onHide = null)
        {
            yield return StartPopupTransition(popupConfig, args);
            
            PopupConfig oldPopupConfig = CurrentPopupConfig;
            IPopupBase oldPopupInstance = CurrentPopupInstance;
            
            yield return HandleCurrentPopup(oldPopupConfig, oldPopupInstance, args, popupConfig);
            
            IPopupBase newPopupInstance = null;
            Canvas canvasPopup = null;
            yield return LoadAndInstantiatePopup(popupConfig, instance => newPopupInstance = instance, canvas => canvasPopup = canvas);
            if (newPopupInstance == null) yield break;
            
            yield return ShowNewPopup(popupConfig, newPopupInstance, canvasPopup, args);
            
            UpdatePopupState(popupConfig, newPopupInstance);
            NotifyPopupChange(oldPopupConfig, oldPopupInstance, popupConfig, newPopupInstance, onShow);
        }

        private IEnumerator StartPopupTransition(PopupConfig popupConfig, System.Object args)
        {
            if (!popupConfig.AssetLoaderConfig.IsLoaded)
            {
                popupConfig.AssetLoaderConfig.PrepareLoadRoutine<GameObject>();
                newPopupLoading = StartCoroutine(popupConfig.AssetLoaderConfig.WaitLoadRoutine());
            }
            yield return newPopupLoading;
            newPopupLoading = null;
        }

        private IEnumerator HandleCurrentPopup(PopupConfig oldPopupConfig, IPopupBase oldPopupInstance, 
            System.Object args, PopupConfig newPopupConfig)
        {
            if (oldPopupConfig == null || oldPopupInstance == null) yield break;

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
                            yield return oldPopupInstance.RequestHide(args);
                            CallOnHideForController(oldPopupInstance);
                            if (oldPopupConfig.GoingBackgroundBehaviour == PopupGoingBackgroundBehaviour.HideAndDestroy)
                            {
                                DisposePopupFromHide(oldPopupConfig, oldPopupInstance, oldPopupConfig == newPopupConfig);
                            }
                        }
                    }
                    else
                    {
                        yield return oldPopupInstance.RequestHide(args);
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
                            hidePopupRoutine = StartCoroutine(oldPopupInstance.RequestHide(args));
                        }
                    }
                    else
                    {
                        hidePopupRoutine = StartCoroutine(oldPopupInstance.RequestHide(args));
                    }
                    break;
            }
        }

        private IEnumerator LoadAndInstantiatePopup(PopupConfig popupConfig, Action<IPopupBase> onInstanceCreated, 
            Action<Canvas> onCanvasAssigned)
        {
            if (popupConfig.AssetLoaderConfig.LoadedAsset == null)
            {
                Debug.LogError($"[ScreenFlow:PopupTransitTo()] -> Failed to load {popupConfig.AssetLoaderConfig.name}", popupConfig);
                yield break;
            }

            IPopupBase newPopupInstance = GetPopupInstance(popupConfig);
            if (newPopupInstance == null)
            {
                Debug.LogError($"[ScreenFlow:PopupTransitTo()] -> {popupConfig.AssetLoaderConfig.LoadedAsset.GetType()} doesn't have any component that inherits from {nameof(IPopupBase)} interface", popupConfig);
                yield break;
            }

            Canvas canvasPopup;
            if (popupConfig.AssetLoaderConfig.IsInScene)
            {
                canvasPopup = GetComponentInParent<Canvas>() ?? GetComponentInChildren<Canvas>();
                newPopupInstance.GameObject.SetActive(true);
            }
            else
            {
                canvasPopup = GetCanvas(newPopupInstance);
                if (canvasPopup != null)
                {
                    newPopupInstance = InstantiateUIElement(newPopupInstance as ScreenBase, null, 
                        out RectTransform instanceRT, out RectTransform prefabRT) as IPopupBase;
                }
                else
                {
                    newPopupInstance = InstantiateUIElement(newPopupInstance as ScreenBase, null, 
                        out RectTransform instanceRT, out RectTransform prefabRT) as IPopupBase;
                    canvasPopup = AllocatePopupCanvas(newPopupInstance);
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

        private IEnumerator ShowNewPopup(PopupConfig popupConfig, IPopupBase newPopupInstance, 
            Canvas canvasPopup, System.Object args)
        {
            newPopupInstance.ParentScreen = CurrentScreenInstance;
            CalculatePopupCanvasSortOrder(canvasPopup, CurrentPopupInstance);

            switch (popupConfig.AnimationType)
            {
                case AnimationType.NoAnimation:
                case AnimationType.Wait:
                    yield return newPopupInstance.Show(args);
                    break;
                case AnimationType.Intersection:
                    showPopupRoutine = StartCoroutine(newPopupInstance.Show(args));
                    break;
            }

            if (hidePopupRoutine != null)
            {
                yield return hidePopupRoutine;
                CallOnHideForController(CurrentPopupInstance);
                if (CurrentPopupConfig.GoingBackgroundBehaviour == PopupGoingBackgroundBehaviour.HideAndDestroy)
                {
                    DisposePopupFromHide(CurrentPopupConfig, CurrentPopupInstance);
                }
                hidePopupRoutine = null;
            }

            if (showPopupRoutine != null)
            {
                yield return showPopupRoutine;
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

        private IEnumerator HandlePopupsOnScreenTransit(System.Object args = null)
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
                        yield return ClosePopupOp(CurrentPopupInstance, args);
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
            RecyclePopupCanvas(popupInstance);

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

        private Canvas GetCanvas(IScreenBase popupBase)
        {
            Canvas canvasPopup = popupBase.GetComponentInParent<Canvas>();
            return canvasPopup;
        }

        private Canvas AllocatePopupCanvas(IPopupBase popupInstance)
        {
            Canvas availableCanvas = null;
            if (AvailablePopupCanvas.Count > 0)
            {
                availableCanvas = AvailablePopupCanvas[AvailablePopupCanvas.Count - 1];
                AvailablePopupCanvas.RemoveAt(AvailablePopupCanvas.Count - 1);
            }
            else
            {
                availableCanvas = CreatePopupCanvas();
            }

            AllocatedPopupCanvas.Add(popupInstance, availableCanvas);
            availableCanvas.gameObject.SetActive(true);
            return availableCanvas;
        }

        private void RecyclePopupCanvas(IPopupBase popupInstance)
        {
            if (AllocatedPopupCanvas.TryGetValue(popupInstance, out Canvas popupCanvas))
            {
                AllocatedPopupCanvas.Remove(popupInstance);
                AvailablePopupCanvas.Add(popupCanvas);
                popupCanvas.gameObject.SetActive(false);
            }
        }

        private Canvas CreatePopupCanvas()
        {
            if (Config.OverridePopupCanvasPrefab != null)
            {
                Canvas canvasPopupOverride = Instantiate(Config.OverridePopupCanvasPrefab);
                DontDestroyOnLoad(canvasPopupOverride); //Canvas are persistent because they can be reused 
                return canvasPopupOverride;
            }

            GameObject canvasPopupGo = new GameObject("[Canvas] - Popup");
            DontDestroyOnLoad(canvasPopupGo); //Canvas are persistent because they can be reused 

            Canvas canvasPopup = canvasPopupGo.AddComponent<Canvas>();
            CanvasScaler canvasScalerPopup = canvasPopupGo.AddComponent<CanvasScaler>();
            GraphicRaycaster graphicRaycasterPopup = canvasPopupGo.AddComponent<GraphicRaycaster>();

            //Copies the settings from the screen's canvas to the popup's canvas
            canvasPopup.renderMode = canvas.renderMode;
            canvasPopup.pixelPerfect = canvas.pixelPerfect;
            canvasPopup.sortingLayerID = canvas.sortingLayerID;
            canvasPopup.sortingOrder = canvas.sortingOrder;
            canvasPopup.targetDisplay = canvas.targetDisplay;
            canvasPopup.renderMode = canvas.renderMode;
            canvasPopup.additionalShaderChannels = canvas.additionalShaderChannels;
            canvasPopup.worldCamera = canvas.worldCamera;

            //Copies the settings from the screen's canvasScaler to the popup's canvasScaler
            canvasScalerPopup.uiScaleMode = canvasScaler.uiScaleMode;
            canvasScalerPopup.referenceResolution = canvasScaler.referenceResolution;
            canvasScalerPopup.screenMatchMode = canvasScaler.screenMatchMode;
            canvasScalerPopup.matchWidthOrHeight = canvasScaler.matchWidthOrHeight;
            canvasScalerPopup.referencePixelsPerUnit = canvasScaler.referencePixelsPerUnit;
            canvasScalerPopup.scaleFactor = canvasScaler.scaleFactor;
            canvasScalerPopup.physicalUnit = canvasScaler.physicalUnit;
            canvasScalerPopup.fallbackScreenDPI = canvasScaler.fallbackScreenDPI;
            canvasScalerPopup.defaultSpriteDPI = canvasScaler.defaultSpriteDPI;

            //Copies the settings from the screen's graphicRaycaster to the popup's graphicRaycaster
            graphicRaycasterPopup.ignoreReversedGraphics = graphicRaycaster.ignoreReversedGraphics;
            graphicRaycasterPopup.blockingObjects = graphicRaycaster.blockingObjects;

            return canvasPopup;
        }

        private void CalculatePopupCanvasSortOrder(Canvas canvasPopup, IPopupBase topOfStackPopupInstance)
        {
            if (canvasPopup != null)
            {
                if (topOfStackPopupInstance == null)
                {
                    canvasPopup.sortingOrder = canvas.sortingOrder + 1;
                }
                else
                {
                    if (AllocatedPopupCanvas.TryGetValue(topOfStackPopupInstance, out Canvas currentPopupCanvas))
                    {
                        canvasPopup.sortingOrder = currentPopupCanvas.sortingOrder + 1;
                    }
                }

                canvasPopup.name = "[Canvas] - Popup #" + canvasPopup.sortingOrder;
            }
        }

        private void ProcessBackKey()
        {
            if (CurrentScreenConfig != null)
            {
                //check if the screen is overriding config back key behaviour
                if (CurrentScreenInstance.BackKeyBehaviourOverride == BackKeyBehaviourOverride.Inherit)
                {
                    switch (CurrentScreenConfig.BackKeyBehaviour)
                    {
                        case BackKeyBehaviour.NotAllowed: return;
                        case BackKeyBehaviour.ScreenMoveBack:
                            MoveBack();
                            break;
                        case BackKeyBehaviour.CloseFirstPopup:
                        {
                            if (CurrentPopupInstance != null)
                            {
                                CloseForegroundPopup();
                            }
                            else
                            {
                                MoveBack();
                            }

                            break;
                        }
                    }
                }
                else
                {
                    switch (CurrentScreenInstance.BackKeyBehaviourOverride)
                    {
                        case BackKeyBehaviourOverride.NotAllowed: return;
                        case BackKeyBehaviourOverride.ScreenMoveBack:
                            MoveBack();
                            break;
                        case BackKeyBehaviourOverride.CloseFirstPopup:
                        {
                            if (CurrentPopupInstance != null)
                            {
                                CloseForegroundPopup();
                            }
                            else
                            {
                                MoveBack();
                            }

                            break;
                        }
                    }
                }
            }
        }

        public void Dispose()
        {
        }
    }
}