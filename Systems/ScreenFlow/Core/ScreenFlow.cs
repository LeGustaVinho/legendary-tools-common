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

        public bool IsTransiting => transitionRoutine != null;

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
        private Task nextScreenLoading;
        private Task newPopupLoading;
        private Task hideScreenRoutine;
        private Task showScreenRoutine;
        private Task hidePopupRoutine;
        private Task showPopupRoutine;
        private Task transitionRoutine;

        private RectTransform rectTransform;
        private PopupCanvasManager popupCanvasManager;

        // Binds a view controller to a specific screen type, allowing it to receive lifecycle events (e.g., OnShow, OnHide).
        public void BindController<T>(IScreenViewController<T> controller)
            where T : IScreenBase
        {
            // Get the type of the screen the controller is associated with.
            Type viewType = typeof(T);
            
            // Initialize the list of controllers for this screen type if it doesn't exist.
            if (!viewControllers.ContainsKey(viewType))
                viewControllers.Add(viewType, new List<IScreenViewController>());
            
            // Add the controller to the list if it's not already present.
            if(!viewControllers[viewType].Contains(controller))
                viewControllers[viewType].Add(controller);
        }
        
        // Unbinds a view controller from a specific screen type, stopping it from receiving lifecycle events.
        public void UnBindController<T>(IScreenViewController<T> controller)
            where T : IScreenBase
        {
            // Get the type of the screen the controller is associated with.
            Type viewType = typeof(T);
            
            // Remove the controller from the list if it exists.
            if (viewControllers.TryGetValue(viewType, out List<IScreenViewController> controllers))
            {
                if (controllers.Contains(controller))
                    controllers.Remove(controller);
            }
        }
        
        // Sends a trigger to transition to a UI entity (screen or popup) by its name.
        public async Task SendTrigger(string name, System.Object args = null, bool enqueue = true, 
            Action<IScreenBase> requestedScreenOnShow = null, Action<IScreenBase> previousScreenOnHide = null)
        {
            // Look up the UI entity by name in the lookup dictionary.
            if (uiEntitiesLookup.TryGetValue(name, out UIEntityBaseConfig uiEntityBaseConfig))
            {
                // Process the trigger for the found UI entity.
                await SendTrigger(uiEntityBaseConfig, args, enqueue, requestedScreenOnShow, previousScreenOnHide);
            }
        }

        // Sends a trigger to transition to a specific UI entity (screen or popup).
        public async Task SendTrigger(UIEntityBaseConfig uiEntity, System.Object args = null, bool enqueue = true, 
            Action<IScreenBase> requestedScreenOnShow = null, Action<IScreenBase> previousScreenOnHide = null)
        {
            // Create a command to trigger the transition to the specified UI entity.
            ScreenFlowCommand command = new ScreenFlowCommand(ScreenFlowCommandType.Trigger, uiEntity, args, requestedScreenOnShow, previousScreenOnHide);
            // Process the command, either immediately or by enqueuing it.
            await ProcessCommand(command, enqueue);
        }

        // Generic version of SendTrigger with type-safe arguments for the UI entity and show args.
        public async Task SendTriggerT<TConfig, TShow>(TConfig uiEntity, TShow args = null, bool enqueue = true,
            Action<IScreenBase> requestedScreenOnShow = null, Action<IScreenBase> previousScreenOnHide = null)
            where TConfig : UIEntityBaseConfig
            where TShow : class
        {
            // Delegate to the non-generic SendTrigger method.
            await SendTrigger(uiEntity, args, enqueue, requestedScreenOnShow, previousScreenOnHide);
        }
        
        // Generic version of SendTrigger with type-safe arguments for UI entity, show, and hide args.
        public async Task SendTriggerT<TConfig, TShow, THide>(TConfig uiEntity, TShow args = null, bool enqueue = true,
            Action<ScreenBaseT<TConfig, TShow, THide>> requestedScreenOnShow = null, Action<IScreenBase> previousScreenOnHide = null)
            where TConfig : UIEntityBaseConfig
            where TShow : class
            where THide : class
        {
            // Wrap the typed onShow callback to match the non-generic signature.
            void RequestedScreenTOnShow(IScreenBase screenBase)
            {
                if (screenBase is ScreenBaseT<TConfig, TShow, THide> screenBaseT)
                    requestedScreenOnShow?.Invoke(screenBaseT);
            }
            
            // Delegate to the non-generic SendTrigger method.
            await SendTrigger(uiEntity, args, enqueue, RequestedScreenTOnShow, previousScreenOnHide);
        }

        // Navigates back to the previous screen in the history stack.
        public async Task MoveBack(System.Object args = null, bool enqueue = true, Action<IScreenBase> onShow = null, Action<IScreenBase> onHide = null)
        {
            // Create a command to move back to the previous screen.
            ScreenFlowCommand command = new ScreenFlowCommand(ScreenFlowCommandType.MoveBack, null, args, onShow, onHide);
            // Process the command, either immediately or by enqueuing it.
            await ProcessCommand(command, enqueue);
        }

        // Closes the topmost popup in the stack.
        public void CloseForegroundPopup(System.Object args = null, bool enqueue = true, Action<IScreenBase> onShow = null, Action<IScreenBase> onHide = null)
        {
            // Check if there is an active popup to close.
            if (CurrentPopupInstance != null)
            {
                // Delegate to the ClosePopup method for the current popup.
                ClosePopup(CurrentPopupInstance, args, enqueue, onShow, onHide);
            }
        }

        // Closes a specific popup instance.
        public void ClosePopup(IPopupBase popupBase, System.Object args = null, bool enqueue = true, Action<IScreenBase> onShow = null, Action<IScreenBase> onHide = null)
        {
            // Create a command to close the specified popup.
            ScreenFlowCommand command = new ScreenFlowCommand(ScreenFlowCommandType.ClosePopup, popupBase, args, onShow, onHide);
            // If no transition is in progress, process the command immediately.
            if (!IsTransiting)
            {
                commandQueue.Add(command);
                transitionRoutine = ProcessCommandQueue();
            }
            // Otherwise, enqueue the command if requested.
            else if (enqueue)
            {
                commandQueue.Add(command);
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
        
        // Initializes the screen flow system, setting up configurations and loading the start screen.
        public async Task Initialize()
        {
            // Validate the configuration before proceeding.
            if (!ValidateConfig()) return;
            
            // Initialize the popup canvas manager to handle popup canvas allocation.
            popupCanvasManager = new PopupCanvasManager(Config, GetComponent<Canvas>(), GetComponent<CanvasScaler>(),
                GetComponent<GraphicRaycaster>());
            
            // Populate the UI entities lookup dictionary for quick access by name.
            PopulateUIEntitiesLookup();
            // Process any screens and popups already present in the scene.
            ProcessInSceneEntities();
            
            // If a start screen is specified, trigger its display.
            if (StartScreen != null)
            {
                await SendTrigger(StartScreen);
            }
            
            // Preload any assets marked for preloading.
            await Preload();
        }

        // Validates the screen flow configuration.
        private bool ValidateConfig()
        {
            // Check if the Config is assigned; log an error and return false if not.
            if (Config == null)
            {
                Debug.LogError("[ScreenFlow:Start] -> Config is null");
                return false;
            }
            return true;
        }

        // Populates the UI entities lookup dictionary with screens and popups from the config.
        private void PopulateUIEntitiesLookup()
        {
            // Clear the existing lookup dictionary.
            uiEntitiesLookup.Clear();
            
            // Add screens and popups to the lookup, logging errors for duplicates.
            AddUIEntitiesToLookup(Config.Screens, "[ScreenFlow:Start()] -> UI Entity {0} already exists in ScreenFlow");
            AddUIEntitiesToLookup(Config.Popups, "[ScreenFlow:Start()] -> UI Entity {0} already exists in ScreenFlow");
        }

        // Adds UI entities to the lookup dictionary, checking for duplicates.
        private void AddUIEntitiesToLookup<T>(IEnumerable<T> entities, string errorMessage) 
            where T : UIEntityBaseConfig
        {
            // Iterate through the entities and add them to the lookup.
            foreach (T entity in entities)
            {
                // Add the entity if it doesn't already exist; otherwise, log an error.
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

        // Processes screens and popups already present in the scene.
        private void ProcessInSceneEntities()
        {
            // Process screens and popups in the scene.
            ProcessInSceneScreens();
            ProcessInScenePopups();
        }

        // Processes screens present in the scene, adding them to the lookup.
        private void ProcessInSceneScreens()
        {
            // Iterate through screens in the scene.
            foreach (ScreenInScene screenInScene in ScreensInScene)
            {
                // Add the screen to the lookup if it doesn't already exist.
                if (!uiEntitiesLookup.ContainsKey(screenInScene.Config.name))
                {
                    uiEntitiesLookup.Add(screenInScene.Config.name, screenInScene.Config);
                    // Mark the screen as a scene asset.
                    screenInScene.Config.AssetLoaderConfig.SetAsSceneAsset(screenInScene.ScreenInstance);
                }
                else
                {
                    Debug.LogError($"[ScreenFlow:Start()] -> UI Entity {screenInScene.Config.name} already exists in ScreenFlow");
                }
            }
        }

        // Processes popups present in the scene, adding them to the lookup.
        private void ProcessInScenePopups()
        {
            // Iterate through popups in the scene.
            foreach (PopupInScene popupInScene in PopupsInScene)
            {
                // Add the popup to the lookup if it doesn't already exist.
                if (!uiEntitiesLookup.ContainsKey(popupInScene.Config.name))
                {
                    uiEntitiesLookup.Add(popupInScene.Config.name, popupInScene.Config);
                    // Mark the popup as a scene asset.
                    popupInScene.Config.AssetLoaderConfig.SetAsSceneAsset(popupInScene.PopupInstance);
                }
                else
                {
                    Debug.LogError($"[ScreenFlow:Start()] -> UI Entity {popupInScene.Config.name} already exists in ScreenFlow");
                }
            }
        }

        // Processes a screen flow command, either immediately or by enqueuing it.
        private async Task ProcessCommand(ScreenFlowCommand command, bool enqueue)
        {
            // If no transition is in progress, process the command immediately.
            if (!IsTransiting)
            {
                commandQueue.Add(command);
                transitionRoutine = ProcessCommandQueue();
                await transitionRoutine;
            }
            // Otherwise, handle based on enqueue flag.
            else
            {
                if (enqueue)
                {
                    // Enqueue the command and wait for it to be processed.
                    commandQueue.Add(command);
                    await WaitForCommandProcessing(command);
                }
                else
                {
                    // Wait for the current transition to complete, then process the command.
                    await transitionRoutine;
                    commandQueue.Add(command);
                    transitionRoutine = ProcessCommandQueue();
                    await transitionRoutine;
                }
            }
        }

        // Waits for a specific command to be processed from the queue.
        private async Task WaitForCommandProcessing(ScreenFlowCommand command)
        {
            // Yield until the command is removed from the queue (i.e., processed).
            while (commandQueue.Contains(command))
            {
                await Task.Yield();
            }
        }

        // Processes the command queue, executing commands in order.
        private async Task ProcessCommandQueue()
        {
            // Continue processing while there are commands in the queue.
            while (commandQueue.Count > 0)
            {
                // Get and remove the next command from the queue.
                ScreenFlowCommand next = commandQueue[0];
                commandQueue.RemoveAt(0);

                // Process the command based on its type.
                switch (next.Type)
                {
                    case ScreenFlowCommandType.Trigger:
                    {
                        switch (next.Object)
                        {
                            case ScreenConfig screenConfig:
                                // Trigger a transition to a new screen.
                                await ScreenTransitTo(screenConfig, false, next.Args, next.OnShow, next.OnHide);
                                break;
                            case PopupConfig popupConfig:
                            {
                                // Trigger a transition to a new popup if the current screen allows it.
                                if (CurrentScreenConfig != null && CurrentScreenConfig.AllowPopups)
                                    await PopupTransitTo(popupConfig, next.Args, next.OnShow, next.OnHide);
                                break;
                            }
                        }
                        break;
                    }
                    case ScreenFlowCommandType.MoveBack:
                    {
                        // Navigate back to the previous screen.
                        await MoveBackOp(next.Args, next.OnShow, next.OnHide);
                        break;
                    }
                    case ScreenFlowCommandType.ClosePopup:
                    {
                        // Close the specified popup.
                        await ClosePopupOp(next.Object as IPopupBase, next.Args, next.OnShow, next.OnHide);
                        break;
                    }
                }
            }

            // Clear the transition routine when the queue is empty.
            transitionRoutine = null;
        }

        // Handles navigation to the previous screen in the history stack.
        private async Task MoveBackOp(System.Object args, Action<IScreenBase> onShow = null, Action<IScreenBase> onHide = null)
        {
            // Get the previous screen configuration from the history stack.
            EntityArgPair<ScreenConfig> previousScreenConfig = ScreensHistory.Count > 1 ? ScreensHistory[ScreensHistory.Count - 2] : null;
            if (previousScreenConfig != null)
            {
                // Check if navigation back is allowed from the current screen and to the previous screen.
                if (CurrentScreenConfig.CanMoveBackFromHere && previousScreenConfig.Entity.CanMoveBackToHere)
                {
                    // Transition to the previous screen, reusing its arguments if none provided.
                    await ScreenTransitTo(previousScreenConfig.Entity, 
                        true, args ?? previousScreenConfig.Args, onShow, onHide);
                }
            }
        }
        
        // Closes a specific popup instance from the stack.
        private async Task ClosePopupOp(IPopupBase popupBase, System.Object args, Action<IScreenBase> onShow = null, 
            Action<IScreenBase> onHide = null)
        {
            // Find the index of the popup in the stack.
            int stackIndex = PopupInstancesStack.FindIndex(item => item == popupBase);

            if (stackIndex >= 0)
            {
                // Determine if the popup is at the top of the stack.
                bool isTopOfStack = stackIndex == PopupInstancesStack.Count - 1;
                PopupConfig popupConfig = PopupConfigsStack[stackIndex];

                // Get the popup behind the current one, if any.
                PopupConfig behindPopupConfig = null;
                IPopupBase behindPopupInstance = null;
                if (stackIndex - 1 >= 0)
                {
                    behindPopupConfig = PopupConfigsStack[stackIndex - 1];
                    behindPopupInstance = PopupInstancesStack[stackIndex - 1];
                }

                if (isTopOfStack)
                {
                    // Handle popup hiding based on its animation type.
                    switch (popupConfig.AnimationType)
                    {
                        case AnimationType.NoAnimation:
                        case AnimationType.Wait:
                        {
                            // Hide the popup and dispose of it.
                            await popupBase.RequestHide(args);
                            onHide?.Invoke(CurrentPopupInstance);
                            CallOnHideForController(popupBase);
                            DisposePopupFromHide(popupConfig, popupBase);
                            break;
                        }
                        case AnimationType.Intersection:
                        {
                            // Start hiding the popup asynchronously.
                            hidePopupRoutine = popupBase.RequestHide(args);
                            break;
                        }
                    }

                    // Show the behind popup if it exists and needs to be shown.
                    if (behindPopupInstance != null && 
                        (behindPopupConfig.GoingBackgroundBehaviour == PopupGoingBackgroundBehaviour.JustHide ||
                         behindPopupConfig.GoingBackgroundBehaviour == PopupGoingBackgroundBehaviour.HideAndDestroy))
                    {
                        switch (popupConfig.AnimationType)
                        {
                            case AnimationType.NoAnimation:
                            case AnimationType.Wait:
                            {
                                await behindPopupInstance.Show(args);
                                break;
                            }
                            case AnimationType.Intersection:
                            {
                                showPopupRoutine = behindPopupInstance.Show(args);
                                break;
                            }
                        }
                    }

                    // Wait for the hide routine to complete if it was started.
                    if (hidePopupRoutine != null)
                    {
                        await hidePopupRoutine;
                        onHide?.Invoke(CurrentPopupInstance);
                        CallOnHideForController(popupBase);
                        DisposePopupFromHide(CurrentPopupConfig, CurrentPopupInstance);
                        hidePopupRoutine = null;
                    }

                    // Wait for the show routine to complete if it was started.
                    if (showPopupRoutine != null)
                    {
                        await showPopupRoutine;
                        showPopupRoutine = null;
                    }
                }
                else
                {
                    // For non-top popups, simply dispose of them.
                    onHide?.Invoke(popupBase);
                    DisposePopupFromHide(popupConfig, popupBase);
                }
                
                // Invoke the onShow callback for the behind popup.
                onShow?.Invoke(behindPopupInstance);
            }
        }

        // Preloads assets marked for preloading in the configuration.
        private async Task Preload()
        {
            // Clear the preload queue.
            PreloadQueue.Clear();
            
            // Add screens marked for preloading to the queue.
            ScreenConfig[] screens = Array.FindAll(Config.Screens, item => item.AssetLoaderConfig.PreLoad);
            if(screens.Length > 0)
                PreloadQueue.AddRange(screens);

            // Add popups marked for preloading to the queue.
            PopupConfig[] popups = Array.FindAll(Config.Popups, item => item.AssetLoaderConfig.PreLoad);
            if(popups.Length > 0)
                PreloadQueue.AddRange(Array.FindAll(Config.Popups, item => item.AssetLoaderConfig.PreLoad));

            // Start preloading the queued assets.
            await PreloadingAssets();
        }

        // Preloads all assets in the preload queue.
        private async Task PreloadingAssets()
        {
            // Iterate through the preload queue and wait for each asset to load.
            foreach (UIEntityBaseConfig uiEntityBaseConfig in PreloadQueue)
            {
                await uiEntityBaseConfig.AssetLoaderConfig.WaitForLoadingAsync<GameObject>();
            }
        }

        // Transitions to a new screen, handling hiding of the current screen and showing the new one.
        private async Task ScreenTransitTo(ScreenConfig screenConfig, bool isMoveBack = false,
            System.Object args = null, Action<IScreenBase> onShow = null, Action<IScreenBase> onHide = null)
        {
            // Start the screen transition process.
            await StartScreenTransition(screenConfig, args);
            
            // Cache the current screen config and instance.
            ScreenConfig oldScreenConfig = CurrentScreenConfig;
            IScreenBase oldScreenInstance = CurrentScreenInstance;
            
            // Hide the current screen if it exists.
            await HideCurrentScreen(oldScreenConfig, oldScreenInstance, args, onHide);
            
            // Load and instantiate the new screen.
            ScreenBase newScreenInstance = null;
            await LoadAndInstantiateScreen(screenConfig, instance => newScreenInstance = instance);
            if (newScreenInstance == null) return;
            
            // Show the new screen.
            await ShowNewScreen(screenConfig, newScreenInstance, args);
            
            // Update the screen state (history and current instance).
            UpdateScreenState(screenConfig, newScreenInstance, isMoveBack, args);
            // Notify listeners of the screen change.
            NotifyScreenChange(oldScreenConfig, oldScreenInstance, screenConfig, newScreenInstance, onShow);
        }

        // Starts the transition to a new screen, initiating asset loading if necessary.
        private async Task StartScreenTransition(ScreenConfig screenConfig, System.Object args)
        {
            // Check if the screen asset needs to be loaded.
            if (!screenConfig.AssetLoaderConfig.IsLoaded && !screenConfig.AssetLoaderConfig.IsLoading)
            {
                // Remove from preload queue if present.
                if (PreloadQueue.Contains(screenConfig))
                {
                    PreloadQueue.Remove(screenConfig);
                }
                // Start loading the screen asset.
                nextScreenLoading = screenConfig.AssetLoaderConfig.WaitForLoadingAsync<GameObject>();
            }
            
            // Handle any popups during the screen transition.
            await HandlePopupsOnScreenTransit(args);
        }

        // Hides the current screen based on its animation type.
        private async Task HideCurrentScreen(ScreenConfig oldScreenConfig, IScreenBase oldScreenInstance, 
            System.Object args, Action<IScreenBase> onHide)
        {
            // Return if there is no current screen to hide.
            if (oldScreenConfig == null || oldScreenInstance == null) return;

            // Handle hiding based on the screen's animation type.
            switch (oldScreenConfig.AnimationType)
            {
                case AnimationType.NoAnimation:
                case AnimationType.Wait:
                    // Hide the screen and handle completion immediately.
                    await oldScreenInstance.RequestHide(args);
                    HandleScreenHideCompletion(oldScreenConfig, oldScreenInstance, onHide);
                    break;
                case AnimationType.Intersection:
                    // Start hiding the screen asynchronously.
                    hideScreenRoutine = oldScreenInstance.RequestHide(args);
                    break;
            }
        }

        // Handles the completion of a screen hide operation, disposing of the screen if necessary.
        private void HandleScreenHideCompletion(ScreenConfig screenConfig, IScreenBase screenInstance, 
            Action<IScreenBase> onHide)
        {
            // Invoke the onHide callback.
            onHide?.Invoke(screenInstance);
            // Notify controllers of the hide event.
            CallOnHideForController(screenInstance);

            // Handle disposal based on whether the screen is in the scene.
            if (screenConfig.AssetLoaderConfig.IsInScene)
            {
                // Deactivate scene screens instead of destroying them.
                screenInstance.GameObject.SetActive(false);
            }
            else
            {
                // Destroy non-scene screens and unload their assets if configured.
                Destroy(screenInstance.GameObject);
                if (!screenConfig.AssetLoaderConfig.DontUnloadAfterLoad)
                {
                    screenConfig.AssetLoaderConfig.Unload();
                }
            }
        }

        // Loads and instantiates a new screen instance.
        private async Task LoadAndInstantiateScreen(ScreenConfig screenConfig, Action<ScreenBase> onInstanceCreated)
        {
            // Wait for the screen asset to load if it was initiated.
            if (nextScreenLoading != null)
            {
                await nextScreenLoading;
                nextScreenLoading = null;
            }
            // Wait until the asset is loaded if it wasn't preloaded.
            else if (!screenConfig.AssetLoaderConfig.IsLoaded)
            {
                while (!screenConfig.AssetLoaderConfig.IsLoaded)
                {
                    await Task.Yield();
                }
            }

            // Check if the asset was loaded successfully.
            if (screenConfig.AssetLoaderConfig.LoadedAsset == null)
            {
                Debug.LogError($"[ScreenFlow:ScreenTransitTo] -> Failed to load {screenConfig.AssetLoaderConfig.name}", 
                    screenConfig);
                return;
            }

            // Get the screen prefab from the loaded asset.
            ScreenBase newScreenPrefab = GetScreenPrefab(screenConfig);
            if (newScreenPrefab == null)
            {
                Debug.LogError($"[ScreenFlow:ScreenTransitTo] -> {screenConfig.AssetLoaderConfig.LoadedAsset.GetType()} doesn't have any component that inherits from ScreenBase class", 
                    screenConfig);
                return;
            }

            ScreenBase newScreenInstance;
            if (screenConfig.AssetLoaderConfig.IsInScene)
            {
                // Use the scene instance and activate it.
                newScreenInstance = newScreenPrefab;
                newScreenInstance.gameObject.SetActive(true);
            }
            else
            {
                // Instantiate a new instance for non-scene screens.
                newScreenInstance = InstantiateUIElement(newScreenPrefab, rectTransform, 
                    out RectTransform instanceRT, out RectTransform prefabRT);
            }

            // Invoke the created callback with the new screen instance.
            onInstanceCreated?.Invoke(newScreenInstance);
        }

        // Retrieves the ScreenBase component from the loaded asset.
        private ScreenBase GetScreenPrefab(ScreenConfig screenConfig)
        {
            // Handle different types of loaded assets.
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
            // Handle showing the screen based on its animation type.
            switch (screenConfig.AnimationType)
            {
                case AnimationType.NoAnimation:
                case AnimationType.Wait:
                    // Show the screen and wait for completion.
                    await newScreenInstance.Show(args);
                    break;
                case AnimationType.Intersection:
                    // Start showing the screen asynchronously.
                    showScreenRoutine = newScreenInstance.Show(args);
                    break;
            }

            // Wait for the hide routine to complete if it was started.
            if (hideScreenRoutine != null)
            {
                await hideScreenRoutine;
                HandleScreenHideCompletion(CurrentScreenConfig, CurrentScreenInstance, null);
                hideScreenRoutine = null;
            }

            // Wait for the show routine to complete if it was started.
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
            LoadAndInstantiatePopup(popupConfig, instance => newPopupInstance = instance, canvas => canvasPopup = canvas);
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
                            hidePopupRoutine = oldPopupInstance.RequestHide(args);
                        }
                    }
                    else
                    {
                        hidePopupRoutine = oldPopupInstance.RequestHide(args);
                    }
                    break;
            }
        }

        private void LoadAndInstantiatePopup(PopupConfig popupConfig, Action<IPopupBase> onInstanceCreated, 
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
                case BackKeyBehaviour.ScreenMoveBack: MoveBack().FireAndForget(); break;
                case BackKeyBehaviour.CloseFirstPopup:
                    if (CurrentPopupInstance != null)
                        CloseForegroundPopup();
                    else MoveBack().FireAndForget();
                    break;
            }
        }

        public void Dispose()
        {
        }
    }
}