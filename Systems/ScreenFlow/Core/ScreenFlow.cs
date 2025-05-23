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

        [Header("UI Entities In Scene")] public List<ScreenInScene> ScreensInScene = new();
        public List<PopupInScene> PopupsInScene = new();

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

        public List<IPopupBase> CurrentPopupInstancesStack => new(PopupInstancesStack);

        public int PopupStackCount => PopupInstancesStack.Count;

        public event Action<ScreenConfig, IScreenBase> OnStart;
        public event Action<(ScreenConfig, IScreenBase), (ScreenConfig, IScreenBase)> OnScreenChange;
        public event Action<(PopupConfig, IPopupBase), (PopupConfig, IPopupBase)> OnPopupOpen;

        protected readonly List<UIEntityBaseConfig> PreloadQueue = new();
        protected readonly List<EntityArgPair<ScreenConfig>> ScreensHistory = new();
        protected readonly List<PopupConfig> PopupConfigsStack = new();
        protected readonly List<IPopupBase> PopupInstancesStack = new();

        protected readonly Dictionary<Type, List<IScreenViewController>> viewControllers = new();

        private PopupCanvasManager popupCanvasManager;
        private readonly List<ScreenFlowCommand> commandQueue = new();
        private Dictionary<ScreenFlowCommandType, Func<ScreenFlowCommand, IEnumerator>> commandExecutors;
        private readonly Dictionary<string, UIEntityBaseConfig> uiEntitiesLookup = new();

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

        public void BindController<T>(IScreenViewController<T> controller)
            where T : IScreenBase
        {
            Type viewType = typeof(T);

            if (!viewControllers.ContainsKey(viewType))
                viewControllers.Add(viewType, new List<IScreenViewController>());

            if (!viewControllers[viewType].Contains(controller))
                viewControllers[viewType].Add(controller);
        }

        public void UnBindController<T>(IScreenViewController<T> controller)
            where T : IScreenBase
        {
            Type viewType = typeof(T);

            if (viewControllers.TryGetValue(viewType, out List<IScreenViewController> controllers)) return;
            if (controllers.Contains(controller))
                controllers.Remove(controller);
        }

        public void SendTrigger(string name, object args = null, bool enqueue = true,
            Action<IScreenBase> requestedScreenOnShow = null, Action<IScreenBase> previousScreenOnHide = null)
        {
            if (uiEntitiesLookup.TryGetValue(name, out UIEntityBaseConfig uiEntityBaseConfig))
                SendTrigger(uiEntityBaseConfig, args, enqueue, requestedScreenOnShow, previousScreenOnHide);
        }

        public void SendTrigger(UIEntityBaseConfig uiEntity, object args = null, bool enqueue = true,
            Action<IScreenBase> requestedScreenOnShow = null, Action<IScreenBase> previousScreenOnHide = null)
        {
            ScreenFlowCommand command = new(ScreenFlowCommandType.Trigger, uiEntity, args, requestedScreenOnShow,
                previousScreenOnHide);
            if (!IsTransiting)
            {
                commandQueue.Add(command);
                transitionRoutine ??= StartCoroutine(ProcessCommandQueue());
            }
            else
            {
                if (enqueue) commandQueue.Add(command);
            }
        }

        public void SendTriggerT<TConfig, TShow>(TConfig uiEntity, TShow args = null, bool enqueue = true,
            Action<IScreenBase> requestedScreenOnShow = null, Action<IScreenBase> previousScreenOnHide = null)
            where TConfig : UIEntityBaseConfig
            where TShow : class
        {
            SendTrigger(uiEntity, args, enqueue, requestedScreenOnShow, previousScreenOnHide);
        }

        public void SendTriggerT<TConfig, TShow, THide>(TConfig uiEntity, TShow args = null, bool enqueue = true,
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

            SendTrigger(uiEntity, args, enqueue, RequestedScreenTOnShow, previousScreenOnHide);
        }

        public void MoveBack(object args = null, bool enqueue = true, Action<IScreenBase> onShow = null,
            Action<IScreenBase> onHide = null)
        {
            ScreenFlowCommand command = new(ScreenFlowCommandType.MoveBack, null, args, onShow, onHide);
            if (!IsTransiting)
            {
                commandQueue.Add(command);
                transitionRoutine ??= StartCoroutine(ProcessCommandQueue());
            }
            else
            {
                if (enqueue) commandQueue.Add(command);
            }
        }

        public void CloseForegroundPopup(object args = null, bool enqueue = true, Action<IScreenBase> onShow = null,
            Action<IScreenBase> onHide = null)
        {
            if (CurrentPopupInstance != null) ClosePopup(CurrentPopupInstance, args, enqueue, onShow, onHide);
        }

        public void ClosePopup(IPopupBase popupBase, object args = null, bool enqueue = true,
            Action<IScreenBase> onShow = null, Action<IScreenBase> onHide = null)
        {
            ScreenFlowCommand command = new(ScreenFlowCommandType.ClosePopup, popupBase, args, onShow, onHide);
            if (!IsTransiting)
            {
                commandQueue.Add(command);
                transitionRoutine ??= StartCoroutine(ProcessCommandQueue());
            }
            else
            {
                if (enqueue) commandQueue.Add(command);
            }
        }

        private void BuildCommandExecutors()
        {
            commandExecutors = new Dictionary<ScreenFlowCommandType, Func<ScreenFlowCommand, IEnumerator>>
            {
                {
                    ScreenFlowCommandType.Trigger, command =>
                        command.Object is ScreenConfig screenConfig
                            ? ScreenTransitTo(screenConfig, false, command.Args, command.OnShow, command.OnHide)
                            : command.Object is PopupConfig popupConfig && CurrentScreenConfig != null &&
                              CurrentScreenConfig.AllowPopups
                                ? PopupTransitTo(popupConfig, command.Args, command.OnShow, command.OnHide)
                                : null
                },
                {
                    ScreenFlowCommandType.MoveBack, command =>
                        MoveBackOp(command.Args, command.OnShow, command.OnHide)
                },
                {
                    ScreenFlowCommandType.ClosePopup, command =>
                        ClosePopupOp(command.Object as IPopupBase, command.Args, command.OnShow, command.OnHide)
                }
            };
        }

#if SCREEN_FLOW_SINGLETON
        protected override void Awake()
        {
            base.Awake();

            rectTransform = GetComponent<RectTransform>();
            popupCanvasManager = new PopupCanvasManager(Config, GetComponent<Canvas>(), GetComponent<CanvasScaler>(),
                GetComponent<GraphicRaycaster>());
            BuildCommandExecutors();
        }
        
        protected override void Start()
        {
            base.Start();
            if (AutoInitializeOnStart)
                Initialize();
        }
#else
        protected void Awake()
        {
            rectTransform = GetComponent<RectTransform>();
            popupCanvasManager = new PopupCanvasManager(Config, GetComponent<Canvas>(), GetComponent<CanvasScaler>(),
                GetComponent<GraphicRaycaster>());
            BuildCommandExecutors();
        }
        
        protected void Start()
        {
            if(AutoInitializeOnStart)
                Initialize();
        }
#endif
        
        private IEnumerator PreLoadRoutine()
        {
            yield return Preload();
            preloadRoutine = null;
        }

        protected virtual void Update()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKeyDown(KeyCode.Escape)) ProcessBackKey();
#endif
        }

        public void Initialize()
        {
            if (Config == null)
            {
                Debug.LogError("[ScreenFlow:Start] -> Config is null");
                return;
            }

            PopulateUIEntitiesLookup();
            ProcessInSceneEntities();

            if (StartScreen != null) SendTrigger(StartScreen);

            preloadRoutine = StartCoroutine(PreLoadRoutine());
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
                if (!uiEntitiesLookup.TryAdd(entity.name, entity))
                    Debug.LogError(string.Format(errorMessage, entity.name));
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
                    Debug.LogError(
                        $"[ScreenFlow:Start()] -> UI Entity {screenInScene.Config.name} already exists in ScreenFlow");
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
                    Debug.LogError(
                        $"[ScreenFlow:Start()] -> UI Entity {popupInScene.Config.name} already exists in ScreenFlow");
                }
            }
        }

        private IEnumerator ExecuteCommand(ScreenFlowCommand command)
        {
            if (!commandExecutors.TryGetValue(command.Type, out Func<ScreenFlowCommand, IEnumerator> executor))
                yield break;
            IEnumerator routine = executor(command);
            if (routine != null) yield return routine;
        }

        private IEnumerator ProcessCommandQueue()
        {
            while (commandQueue.Count > 0)
            {
                ScreenFlowCommand next = commandQueue[0];
                commandQueue.RemoveAt(0);
                yield return ExecuteCommand(next);
            }

            transitionRoutine = null;
        }

        private IEnumerator MoveBackOp(object args, Action<IScreenBase> onShow = null,
            Action<IScreenBase> onHide = null)
        {
            EntityArgPair<ScreenConfig> previousScreenConfig =
                ScreensHistory.Count > 1 ? ScreensHistory[ScreensHistory.Count - 2] : null;
            if (previousScreenConfig == null) yield break;
            if (CurrentScreenConfig.CanMoveBackFromHere && previousScreenConfig.Entity.CanMoveBackToHere)
                yield return ScreenTransitTo(previousScreenConfig.Entity,
                    true, args ?? previousScreenConfig.Args, onShow, onHide);
        }

        private void RemovePopupFromStack(PopupConfig popupConfig, IPopupBase popupInstance)
        {
            PopupConfigsStack.Remove(popupConfig);
            PopupInstancesStack.Remove(popupInstance);
            popupCanvasManager.RecyclePopupCanvas(popupInstance);
            popupInstance.OnClosePopupRequest -= OnClosePopupRequest;
        }

        private IEnumerator ClosePopupOp(IPopupBase popupBase, object args, Action<IScreenBase> onShow = null,
            Action<IScreenBase> onHide = null)
        {
            int stackIndex = PopupInstancesStack.FindIndex(item => item == popupBase);
            if (stackIndex < 0) yield break;

            bool isTopOfStack = stackIndex == PopupInstancesStack.Count - 1;
            PopupConfig popupConfig = PopupConfigsStack[stackIndex];

            PopupConfig behindPopupConfig = stackIndex > 0 ? PopupConfigsStack[stackIndex - 1] : null;
            IPopupBase behindPopupInstance = stackIndex > 0 ? PopupInstancesStack[stackIndex - 1] : null;

            if (isTopOfStack)
            {
                yield return ExecuteHideAnimation(popupBase, popupConfig, args, onHide,
                    instance => DisposePopupFromHide(popupConfig, popupBase));

                if (behindPopupInstance != null &&
                    (behindPopupConfig.GoingBackgroundBehaviour == PopupGoingBackgroundBehaviour.JustHide ||
                     behindPopupConfig.GoingBackgroundBehaviour == PopupGoingBackgroundBehaviour.HideAndDestroy))
                    yield return ExecuteShowAnimation(behindPopupInstance, behindPopupConfig, args);

                onShow?.Invoke(behindPopupInstance);
            }
            else
            {
                onHide?.Invoke(popupBase);
                RemovePopupFromStack(popupConfig, popupBase);
                onShow?.Invoke(behindPopupInstance);
            }

            popupTransitionRoutine = null;
        }

        private IEnumerator Preload()
        {
            PreloadQueue.Clear();

            ScreenConfig[] screens = Array.FindAll(Config.Screens, item => item.AssetLoaderConfig.PreLoad);

            if (screens.Length > 0)
                PreloadQueue.AddRange(screens);

            PopupConfig[] popups = Array.FindAll(Config.Popups, item => item.AssetLoaderConfig.PreLoad);

            if (popups.Length > 0)
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
            object args = null, Action<IScreenBase> onShow = null, Action<IScreenBase> onHide = null)
        {
            yield return StartAssetLoading(screenConfig, nextScreenLoading, PreloadQueue);
            yield return HandlePopupsOnScreenTransit(args);

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

        private IEnumerator StartAssetLoading<T>(T config, Coroutine loadingCoroutine, List<T> preloadQueue)
            where T : UIEntityBaseConfig
        {
            if (!config.AssetLoaderConfig.IsLoaded && !config.AssetLoaderConfig.IsLoading)
            {
                if (preloadQueue.Contains(config)) preloadQueue.Remove(config);
                config.AssetLoaderConfig.PrepareLoadRoutine<GameObject>();
                loadingCoroutine = StartCoroutine(config.AssetLoaderConfig.WaitLoadRoutine());
                yield return loadingCoroutine;
                loadingCoroutine = null;
            }
        }

        private IEnumerator HideCurrentScreen(ScreenConfig oldScreenConfig, IScreenBase oldScreenInstance,
            object args, Action<IScreenBase> onHide)
        {
            if (oldScreenConfig == null || oldScreenInstance == null) yield break;

            yield return ExecuteHideAnimation(oldScreenInstance, oldScreenConfig, args, onHide,
                instance => HandleScreenHideCompletion(oldScreenConfig, oldScreenInstance, null));
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
                if (!screenConfig.AssetLoaderConfig.DontUnloadAfterLoad) screenConfig.AssetLoaderConfig.Unload();
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
                Debug.LogError(
                    $"[ScreenFlow:ScreenTransitTo()] -> Failed to load {screenConfig.AssetLoaderConfig.name}",
                    screenConfig);
                yield break;
            }

            ScreenBase newScreenPrefab = GetUIElementInstance<ScreenBase, ScreenConfig>(screenConfig);
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

        private T GetUIElementInstance<T, TConfig>(TConfig config)
            where T : class, IScreenBase
            where TConfig : UIEntityBaseConfig
        {
            switch (config.AssetLoaderConfig.LoadedAsset)
            {
                case GameObject uiGameObject:
                    return uiGameObject.GetComponent<ScreenBase>() as T;
                case T popupBase:
                    return popupBase;
                default:
                    return null;
            }
        }

        private IEnumerator ExecuteAnimation<T>(T instance, UIEntityBaseConfig config, object args, bool isShow, 
            Action<T> onComplete = null, Action<T> postAction = null)
            where T : IScreenBase
        {
            switch (config.AnimationType)
            {
                case AnimationType.NoAnimation:
                case AnimationType.Wait:
                    if (isShow)
                        yield return instance.Show(args);
                    else
                        yield return instance.RequestHide(args);
                    onComplete?.Invoke(instance);
                    if (!isShow)
                        CallOnHideForController(instance);
                    postAction?.Invoke(instance);
                    break;
                case AnimationType.Intersection:
                    Coroutine routine = isShow 
                        ? StartCoroutine(instance.Show(args))
                        : StartCoroutine(instance.RequestHide(args));
                    yield return routine;
                    onComplete?.Invoke(instance);
                    if (!isShow)
                        CallOnHideForController(instance);
                    postAction?.Invoke(instance);
                    if (isShow)
                        showScreenRoutine = null;
                    else
                        hideScreenRoutine = null;
                    break;
            }
        }

        private IEnumerator ExecuteShowAnimation(IScreenBase instance, UIEntityBaseConfig config, object args)
        {
            return ExecuteAnimation(instance, config, args, true);
        }

        private IEnumerator ExecuteHideAnimation(IScreenBase instance, UIEntityBaseConfig config, object args,
            Action<IScreenBase> onHide = null, Action<IScreenBase> postHideAction = null)
        {
            return ExecuteAnimation(instance, config, args, false, onHide, postHideAction);
        }

        private IEnumerator ShowNewScreen(ScreenConfig screenConfig, ScreenBase newScreenInstance, object args)
        {
            yield return ExecuteShowAnimation(newScreenInstance, screenConfig, args);

            if (hideScreenRoutine != null)
            {
                yield return hideScreenRoutine;
                HandleScreenHideCompletion(CurrentScreenConfig, CurrentScreenInstance, null);
                hideScreenRoutine = null;
            }
        }

        private void UpdateScreenState(ScreenConfig screenConfig, ScreenBase newScreenInstance,
            bool isMoveBack, object args)
        {
            CurrentScreenInstance = newScreenInstance;
            UpdateScreenHistory(screenConfig, args, isMoveBack);
            foreach (IPopupBase popup in PopupInstancesStack)
            {
                popup.ParentScreen = CurrentScreenInstance;
            }

            screenTransitionRoutine = null;
        }

        private void UpdateScreenHistory(ScreenConfig screenConfig, object args, bool isMoveBack)
        {
            if (isMoveBack)
                ScreensHistory.RemoveAt(ScreensHistory.Count - 1);
            else
                ScreensHistory.Add(new EntityArgPair<ScreenConfig>(screenConfig, args));
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
            if (!viewControllers.TryGetValue(viewType, out List<IScreenViewController> controllers)) return;
            foreach (IScreenViewController controller in controllers)
            {
                controller.OnShow(view);
            }
        }

        private void CallOnHideForController(IScreenBase view)
        {
            Type viewType = view.GetType();
            if (!viewControllers.TryGetValue(viewType, out List<IScreenViewController> controllers)) return;
            foreach (IScreenViewController controller in controllers)
            {
                controller.OnHide(view);
            }
        }

        private void CallOnGoingToBackgroundForController(IPopupBase view)
        {
            Type viewType = view.GetType();
            if (!viewControllers.TryGetValue(viewType, out List<IScreenViewController> controllers)) return;
            foreach (IScreenViewController controller in controllers)
            {
                if (controller is not IPopupScreenViewController popupViewController) continue;
                popupViewController.OnGoingToBackground(view);
            }
        }

        private IEnumerator PopupTransitTo(PopupConfig popupConfig, object args = null,
            Action<IScreenBase> onShow = null, Action<IScreenBase> onHide = null)
        {
            yield return StartAssetLoading(popupConfig, newPopupLoading, PreloadQueue);

            PopupConfig oldPopupConfig = CurrentPopupConfig;
            IPopupBase oldPopupInstance = CurrentPopupInstance;

            yield return HandleCurrentPopup(oldPopupConfig, oldPopupInstance, args, popupConfig);

            IPopupBase newPopupInstance = null;
            Canvas canvasPopup = null;
            yield return LoadAndInstantiatePopup(popupConfig, instance => newPopupInstance = instance,
                canvas => canvasPopup = canvas);
            if (newPopupInstance == null) yield break;

            yield return ShowNewPopup(popupConfig, newPopupInstance, canvasPopup, args);

            UpdatePopupState(popupConfig, newPopupInstance);
            NotifyPopupChange(oldPopupConfig, oldPopupInstance, popupConfig, newPopupInstance, onShow);
        }

        private IEnumerator HandleCurrentPopup(PopupConfig oldPopupConfig, IPopupBase oldPopupInstance,
            object args, PopupConfig newPopupConfig)
        {
            if (oldPopupConfig == null || oldPopupInstance == null) yield break;

            bool allowStackablePopups = CurrentScreenConfig.AllowStackablePopups;

            if (allowStackablePopups)
            {
                oldPopupInstance.GoToBackground(args);
                CallOnGoingToBackgroundForController(oldPopupInstance);
                if (oldPopupConfig.GoingBackgroundBehaviour != PopupGoingBackgroundBehaviour.DontHide)
                    yield return ExecuteHideAnimation(oldPopupInstance, oldPopupConfig, args, null,
                        instance =>
                        {
                            if (oldPopupConfig.GoingBackgroundBehaviour == PopupGoingBackgroundBehaviour.HideAndDestroy)
                                DisposePopupFromHide(oldPopupConfig, oldPopupInstance,
                                    oldPopupConfig == newPopupConfig);
                        });
            }
            else
            {
                yield return ExecuteHideAnimation(oldPopupInstance, oldPopupConfig, args, null,
                    instance => DisposePopupFromHide(oldPopupConfig, oldPopupInstance,
                        oldPopupConfig == newPopupConfig));
            }
        }

        private IEnumerator LoadAndInstantiatePopup(PopupConfig popupConfig, Action<IPopupBase> onInstanceCreated,
            Action<Canvas> onCanvasAssigned)
        {
            if (popupConfig.AssetLoaderConfig.LoadedAsset == null)
            {
                Debug.LogError($"[ScreenFlow:PopupTransitTo()] -> Failed to load {popupConfig.AssetLoaderConfig.name}",
                    popupConfig);
                yield break;
            }

            IPopupBase newPopupInstance = GetUIElementInstance<IPopupBase, PopupConfig>(popupConfig);
            if (newPopupInstance == null)
            {
                Debug.LogError(
                    $"[ScreenFlow:PopupTransitTo()] -> {popupConfig.AssetLoaderConfig.LoadedAsset.GetType()} doesn't have any component that inherits from {nameof(IPopupBase)} interface",
                    popupConfig);
                yield break;
            }

            Canvas canvasPopup;
            if (popupConfig.AssetLoaderConfig.IsInScene)
            {
                canvasPopup = newPopupInstance.GetComponentInParent<Canvas>() ??
                              newPopupInstance.GetComponentInChildren<Canvas>();
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

        private IEnumerator ShowNewPopup(PopupConfig popupConfig, IPopupBase newPopupInstance,
            Canvas canvasPopup, object args)
        {
            newPopupInstance.ParentScreen = CurrentScreenInstance;
            popupCanvasManager.CalculatePopupCanvasSortOrder(canvasPopup, CurrentPopupInstance);

            yield return ExecuteShowAnimation(newPopupInstance, popupConfig, args);

            if (hidePopupRoutine == null) yield break;
            yield return hidePopupRoutine;
            CallOnHideForController(CurrentPopupInstance);
            if (CurrentPopupConfig.GoingBackgroundBehaviour == PopupGoingBackgroundBehaviour.HideAndDestroy)
                DisposePopupFromHide(CurrentPopupConfig, CurrentPopupInstance);
            hidePopupRoutine = null;
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

        private IEnumerator HandlePopupsOnScreenTransit(object args = null)
        {
            if (CurrentPopupInstance == null) yield break;
            switch (CurrentScreenConfig.PopupBehaviourOnScreenTransition)
            {
                case PopupsBehaviourOnScreenTransition.PreserveAllOnHide: break;
                case PopupsBehaviourOnScreenTransition.HideFirstThenTransit:
                {
                    yield return ClosePopupOp(CurrentPopupInstance, args);
                    for (int i = PopupConfigsStack.Count - 1; i >= 0; i--)
                        DisposePopupFromHide(PopupConfigsStack[i], PopupInstancesStack[i]);
                    break;
                }
                case PopupsBehaviourOnScreenTransition.DestroyAllThenTransit:
                {
                    for (int i = PopupConfigsStack.Count - 1; i >= 0; i--)
                        DisposePopupFromHide(PopupConfigsStack[i], PopupInstancesStack[i]);
                    break;
                }
            }
        }

        private void DisposePopupFromHide(PopupConfig popupConfig, IPopupBase popupInstance,
            bool forceDontUnload = false)
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

                if (popupConfig.AssetLoaderConfig.DontUnloadAfterLoad) return;
                if (forceDontUnload == false)
                    popupConfig.AssetLoaderConfig.Unload();
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