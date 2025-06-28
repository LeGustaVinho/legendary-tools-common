using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace LegendaryTools.Systems.ScreenFlow
{
    public class PopupManager
    {
        private readonly ScreenFlow screenFlow;
        private readonly ScreenFlowConfig config;
        private readonly Canvas canvas;
        private readonly CanvasScaler canvasScaler;
        private readonly GraphicRaycaster graphicRaycaster;
        private readonly PopupCanvasManager popupCanvasManager;
        private readonly List<PopupConfig> popupConfigsStack = new();
        private readonly List<IPopupBase> popupInstancesStack = new();

        public PopupConfig CurrentPopupConfig =>
            popupConfigsStack.Count > 0 ? popupConfigsStack[popupConfigsStack.Count - 1] : null;

        public IPopupBase CurrentPopupInstance =>
            popupInstancesStack.Count > 0 ? popupInstancesStack[popupInstancesStack.Count - 1] : null;

        public List<IPopupBase> CurrentPopupInstancesStack2 => new(popupInstancesStack);
        public int PopupStackCount => popupInstancesStack.Count;

        public event Action<(PopupConfig, IPopupBase), (PopupConfig, IPopupBase)> OnPopupOpen;

        public PopupManager(ScreenFlow screenFlow, ScreenFlowConfig config, Canvas canvas, CanvasScaler canvasScaler,
            GraphicRaycaster graphicRaycaster)
        {
            this.screenFlow = screenFlow ?? throw new ArgumentNullException(nameof(screenFlow));
            this.config = config ?? throw new ArgumentNullException(nameof(config));
            this.canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));
            this.canvasScaler = canvasScaler ?? throw new ArgumentNullException(nameof(canvasScaler));
            this.graphicRaycaster = graphicRaycaster ?? throw new ArgumentNullException(nameof(graphicRaycaster));

            popupCanvasManager = new PopupCanvasManager(config, canvas, canvasScaler, graphicRaycaster);
            if (popupCanvasManager == null)
                Debug.LogError("[PopupManager:Constructor] -> Failed to initialize PopupCanvasManager");

            if (screenFlow.Verbose) Debug.Log("[PopupManager:Constructor] -> PopupManager initialized");
        }

        public async Task PopupTransitTo(PopupConfig popupConfig, ScreenConfig currentScreenConfig, object args,
            Action<IScreenBase> onShow, Action<IScreenBase> onHide, ScreenFlow screenFlow)
        {
            if (popupConfig == null)
            {
                Debug.LogError("[PopupManager:PopupTransitTo] -> PopupConfig is null");
                return;
            }

            if (currentScreenConfig == null)
            {
                Debug.LogError("[PopupManager:PopupTransitTo] -> CurrentScreenConfig is null");
                return;
            }

            if (screenFlow == null)
            {
                Debug.LogError("[PopupManager:PopupTransitTo] -> ScreenFlow is null");
                return;
            }

            PopupConfig oldPopupConfig = CurrentPopupConfig;
            IPopupBase oldPopupInstance = CurrentPopupInstance;

            await StartPopupTransition(popupConfig, args);
            await HandleCurrentPopup(oldPopupConfig, oldPopupInstance, args, popupConfig, currentScreenConfig,
                screenFlow);

            (IPopupBase instanceCreated, Canvas canvasAssigned) popupInstance =
                LoadAndInstantiatePopup(popupConfig, screenFlow);
            if (popupInstance.instanceCreated == null)
            {
                Debug.LogError($"[PopupManager:PopupTransitTo] -> Failed to instantiate popup: {popupConfig.name}");
                return;
            }

            await ShowNewPopup(popupConfig, popupInstance.instanceCreated, popupInstance.canvasAssigned, args,
                screenFlow);
            UpdatePopupState(popupConfig, popupInstance.instanceCreated);
            NotifyPopupChange(oldPopupConfig, oldPopupInstance, popupConfig, popupInstance.instanceCreated, onShow,
                screenFlow);
            if (screenFlow.Verbose)
                Debug.Log($"[PopupManager:PopupTransitTo] -> Transitioned to popup: {popupConfig.name}");
        }

        public async Task ClosePopupOp(IPopupBase popupBase, object args, Action<IScreenBase> onShow,
            Action<IScreenBase> onHide, ScreenFlow screenFlow)
        {
            if (popupBase == null)
            {
                Debug.LogError("[PopupManager:ClosePopupOp] -> PopupBase is null");
                return;
            }

            int stackIndex = popupInstancesStack.FindIndex(item => item == popupBase);
            if (stackIndex < 0)
            {
                Debug.LogError($"[PopupManager:ClosePopupOp] -> Popup not found in stack");
                return;
            }

            bool isTopOfStack = stackIndex == popupInstancesStack.Count - 1;
            PopupConfig popupConfig = popupConfigsStack[stackIndex];

            PopupConfig behindPopupConfig = null;
            IPopupBase behindPopupInstance = null;
            if (stackIndex - 1 >= 0)
            {
                behindPopupConfig = popupConfigsStack[stackIndex - 1];
                behindPopupInstance = popupInstancesStack[stackIndex - 1];
            }

            Task hidePopupRoutine = null;
            Task showPopupRoutine = null;

            if (isTopOfStack)
            {
                switch (popupConfig.TransitionMode)
                {
                    case TransitionMode.Sequential:
                        await popupBase.RequestHide(args);
                        onHide?.Invoke(popupBase);
                        screenFlow?.CallOnHideForController(popupBase);
                        DisposePopupFromHide(popupConfig, popupBase);
                        if (screenFlow.Verbose)
                            Debug.Log($"[PopupManager:ClosePopupOp] -> Sequentially closed popup: {popupConfig.name}");
                        break;
                    case TransitionMode.Parallel:
                        hidePopupRoutine = popupBase.RequestHide(args);
                        if (screenFlow.Verbose)
                            Debug.Log(
                                $"[PopupManager:ClosePopupOp] -> Initiated parallel hide for popup: {popupConfig.name}");
                        break;
                }

                if (behindPopupInstance != null &&
                    (behindPopupConfig.GoingBackgroundBehaviour == PopupGoingBackgroundBehaviour.JustHide ||
                     behindPopupConfig.GoingBackgroundBehaviour == PopupGoingBackgroundBehaviour.HideAndDestroy))
                    switch (popupConfig.TransitionMode)
                    {
                        case TransitionMode.Sequential:
                            await behindPopupInstance.Show(args);
                            if (screenFlow.Verbose)
                                Debug.Log(
                                    $"[PopupManager:ClosePopupOp] -> Sequentially showed behind popup: {behindPopupConfig.name}");
                            break;
                        case TransitionMode.Parallel:
                            showPopupRoutine = behindPopupInstance.Show(args);
                            if (screenFlow.Verbose)
                                Debug.Log(
                                    $"[PopupManager:ClosePopupOp] -> Initiated parallel show for behind popup: {behindPopupConfig.name}");
                            break;
                    }

                if (hidePopupRoutine != null)
                {
                    await hidePopupRoutine;
                    onHide?.Invoke(popupBase);
                    screenFlow?.CallOnHideForController(popupBase);
                    DisposePopupFromHide(popupConfig, popupBase);
                    if (screenFlow.Verbose)
                        Debug.Log(
                            $"[PopupManager:ClosePopupOp] -> Completed parallel hide for popup: {popupConfig.name}");
                }

                if (showPopupRoutine != null)
                {
                    await showPopupRoutine;
                    if (screenFlow.Verbose)
                        Debug.Log(
                            $"[PopupManager:ClosePopupOp] -> Completed parallel show for behind popup: {behindPopupConfig?.name}");
                }
            }
            else
            {
                onHide?.Invoke(popupBase);
                DisposePopupFromHide(popupConfig, popupBase);
                if (screenFlow.Verbose)
                    Debug.Log($"[PopupManager:ClosePopupOp] -> Closed non-top-of-stack popup: {popupConfig.name}");
            }

            onShow?.Invoke(behindPopupInstance);
            if (screenFlow.Verbose)
                Debug.Log(
                    $"[PopupManager:ClosePopupOp] -> Invoked onShow for behind popup: {(behindPopupInstance != null ? behindPopupInstance.GameObject.name : "none")}");
        }

        public async Task HandlePopupsOnScreenTransit(ScreenConfig currentScreenConfig, object args)
        {
            if (currentScreenConfig == null) return;

            if (CurrentPopupInstance == null)
            {
                if (screenFlow.Verbose) Debug.Log("[PopupManager:HandlePopupsOnScreenTransit] -> No popups to handle");
                return;
            }

            switch (currentScreenConfig.PopupBehaviourOnScreenTransition)
            {
                case PopupsBehaviourOnScreenTransition.PreserveAllOnHide:
                    if (screenFlow.Verbose)
                        Debug.Log("[PopupManager:HandlePopupsOnScreenTransit] -> Preserving all popups");
                    break;
                case PopupsBehaviourOnScreenTransition.HideFirstThenTransit:
                    await ClosePopupOp(CurrentPopupInstance, args, null, null, null);
                    for (int i = popupConfigsStack.Count - 1; i >= 0; i--)
                    {
                        DisposePopupFromHide(popupConfigsStack[i], popupInstancesStack[i]);
                        if (screenFlow.Verbose)
                            Debug.Log(
                                $"[PopupManager:HandlePopupsOnScreenTransit] -> Disposed popup: {popupConfigsStack[i].name}");
                    }

                    break;
                case PopupsBehaviourOnScreenTransition.DestroyAllThenTransit:
                    for (int i = popupConfigsStack.Count - 1; i >= 0; i--)
                    {
                        DisposePopupFromHide(popupConfigsStack[i], popupInstancesStack[i]);
                        if (screenFlow.Verbose)
                            Debug.Log(
                                $"[PopupManager:HandlePopupsOnScreenTransit] -> Disposed popup: {popupConfigsStack[i].name}");
                    }

                    break;
            }
        }

        public void Dispose()
        {
            if (popupInstancesStack == null || popupConfigsStack == null)
            {
                Debug.LogError("[PopupManager:Dispose] -> Popup stacks are null");
                return;
            }

            foreach (IPopupBase popup in popupInstancesStack)
            {
                if (popup != null)
                {
                    popup.OnClosePopupRequest -= OnClosePopupRequest;
                    if (screenFlow.Verbose)
                        Debug.Log(
                            $"[PopupManager:Dispose] -> Unsubscribed from OnClosePopupRequest for popup: {popup.GameObject.name}");
                }
            }

            popupConfigsStack.Clear();
            popupInstancesStack.Clear();
            if (screenFlow.Verbose) Debug.Log("[PopupManager:Dispose] -> PopupManager disposed");
        }

        private async Task StartPopupTransition(PopupConfig popupConfig, object args)
        {
            if (popupConfig?.AssetLoaderConfig == null)
            {
                Debug.LogError("[PopupManager:StartPopupTransition] -> PopupConfig or AssetLoaderConfig is null");
                return;
            }

            if (!popupConfig.AssetLoaderConfig.IsLoaded)
            {
                await popupConfig.AssetLoaderConfig.WaitForLoadingAsync<GameObject>();
                if (screenFlow.Verbose)
                    Debug.Log($"[PopupManager:StartPopupTransition] -> Loaded popup asset: {popupConfig.name}");
            }
        }

        private async Task HandleCurrentPopup(PopupConfig oldPopupConfig, IPopupBase oldPopupInstance, object args,
            PopupConfig newPopupConfig, ScreenConfig currentScreenConfig, ScreenFlow screenFlow)
        {
            if (oldPopupConfig == null || oldPopupInstance == null)
            {
                if (screenFlow.Verbose) Debug.Log("[PopupManager:HandleCurrentPopup] -> No current popup to handle");
                return;
            }

            bool allowStackablePopups = currentScreenConfig.AllowStackablePopups;
            Task hidePopupRoutine = null;

            switch (oldPopupConfig.TransitionMode)
            {
                case TransitionMode.Sequential:
                    if (allowStackablePopups)
                    {
                        oldPopupInstance.GoToBackground(args);
                        screenFlow?.CallOnGoingToBackgroundForController(oldPopupInstance);
                        if (oldPopupConfig.GoingBackgroundBehaviour != PopupGoingBackgroundBehaviour.DontHide)
                        {
                            await oldPopupInstance.RequestHide(args);
                            screenFlow?.CallOnHideForController(oldPopupInstance);
                            if (oldPopupConfig.GoingBackgroundBehaviour == PopupGoingBackgroundBehaviour.HideAndDestroy)
                            {
                                DisposePopupFromHide(oldPopupConfig, oldPopupInstance,
                                    oldPopupConfig == newPopupConfig);
                                if (screenFlow.Verbose)
                                    Debug.Log(
                                        $"[PopupManager:HandleCurrentPopup] -> Disposed popup on background: {oldPopupConfig.name}");
                            }
                        }

                        if (screenFlow.Verbose)
                            Debug.Log(
                                $"[PopupManager:HandleCurrentPopup] -> Moved popup to background sequentially: {oldPopupConfig.name}");
                    }
                    else
                    {
                        await oldPopupInstance.RequestHide(args);
                        screenFlow?.CallOnHideForController(oldPopupInstance);
                        DisposePopupFromHide(oldPopupConfig, oldPopupInstance, oldPopupConfig == newPopupConfig);
                        if (screenFlow.Verbose)
                            Debug.Log(
                                $"[PopupManager:HandleCurrentPopup] -> Hid and disposed popup sequentially: {oldPopupConfig.name}");
                    }

                    break;
                case TransitionMode.Parallel:
                    if (allowStackablePopups)
                    {
                        oldPopupInstance.GoToBackground(args);
                        screenFlow?.CallOnGoingToBackgroundForController(oldPopupInstance);
                        if (oldPopupConfig.GoingBackgroundBehaviour != PopupGoingBackgroundBehaviour.DontHide)
                        {
                            hidePopupRoutine = oldPopupInstance.RequestHide(args);
                            if (screenFlow.Verbose)
                                Debug.Log(
                                    $"[PopupManager:HandleCurrentPopup] -> Initiated parallel hide for popup: {oldPopupConfig.name}");
                        }
                    }
                    else
                    {
                        hidePopupRoutine = oldPopupInstance.RequestHide(args);
                        if (screenFlow.Verbose)
                            Debug.Log(
                                $"[PopupManager:HandleCurrentPopup] -> Initiated parallel hide for popup: {oldPopupConfig.name}");
                    }

                    break;
            }

            if (hidePopupRoutine != null)
            {
                await hidePopupRoutine;
                screenFlow?.CallOnHideForController(oldPopupInstance);
                if (oldPopupConfig.GoingBackgroundBehaviour == PopupGoingBackgroundBehaviour.HideAndDestroy)
                {
                    DisposePopupFromHide(oldPopupConfig, oldPopupInstance);
                    if (screenFlow.Verbose)
                        Debug.Log(
                            $"[PopupManager:HandleCurrentPopup] -> Completed parallel hide and disposed popup: {oldPopupConfig.name}");
                }
            }
        }

        private (IPopupBase instanceCreated, Canvas canvasAssigned) LoadAndInstantiatePopup(PopupConfig popupConfig,
            ScreenFlow screenFlow)
        {
            if (popupConfig?.AssetLoaderConfig?.LoadedAsset == null)
            {
                Debug.LogError(
                    $"[PopupManager:LoadAndInstantiatePopup] -> Failed to load {popupConfig?.name ?? "null"}");
                return (null, null);
            }

            IPopupBase newPopupInstance = GetPopupInstance(popupConfig);
            if (newPopupInstance == null)
            {
                Debug.LogError(
                    $"[PopupManager:LoadAndInstantiatePopup] -> {popupConfig.AssetLoaderConfig.LoadedAsset.GetType()} doesn't have any component that inherits from {nameof(IPopupBase)} interface");
                return (null, null);
            }

            Canvas canvasPopup;
            if (popupConfig.AssetLoaderConfig.IsInScene)
            {
                canvasPopup = screenFlow.GetComponentInParent<Canvas>() ?? screenFlow.GetComponentInChildren<Canvas>();
                if (canvasPopup == null)
                    Debug.LogError(
                        $"[PopupManager:LoadAndInstantiatePopup] -> No canvas found for in-scene popup: {popupConfig.name}");
                newPopupInstance.GameObject.SetActive(true);
                if (screenFlow.Verbose)
                    Debug.Log(
                        $"[PopupManager:LoadAndInstantiatePopup] -> Activated in-scene popup: {popupConfig.name}");
            }
            else
            {
                canvasPopup = popupCanvasManager.GetCanvas(newPopupInstance);
                if (canvasPopup != null)
                {
                    newPopupInstance = screenFlow.InstantiateUIElement(newPopupInstance as ScreenBase, null,
                        out RectTransform instanceRT, out RectTransform prefabRT) as IPopupBase;
                    if (screenFlow.Verbose)
                        Debug.Log(
                            $"[PopupManager:LoadAndInstantiatePopup] -> Instantiated popup with existing canvas: {popupConfig.name}");
                }
                else
                {
                    newPopupInstance = screenFlow.InstantiateUIElement(newPopupInstance as ScreenBase, null,
                        out RectTransform instanceRT, out RectTransform prefabRT) as IPopupBase;
                    canvasPopup = popupCanvasManager.AllocatePopupCanvas(newPopupInstance);
                    screenFlow.ReparentUIElement(instanceRT, prefabRT, canvasPopup.transform);
                    if (screenFlow.Verbose)
                        Debug.Log(
                            $"[PopupManager:LoadAndInstantiatePopup] -> Instantiated popup with new canvas: {popupConfig.name}");
                }
            }

            return (newPopupInstance, canvasPopup);
        }

        private async Task ShowNewPopup(PopupConfig popupConfig, IPopupBase newPopupInstance,
            Canvas canvasPopup, object args, ScreenFlow screenFlow)
        {
            if (popupConfig == null || newPopupInstance == null || canvasPopup == null)
            {
                Debug.LogError(
                    "[PopupManager:ShowNewPopup] -> Invalid parameters: popupConfig, newPopupInstance, or canvasPopup is null");
                return;
            }

            newPopupInstance.ParentScreen = screenFlow.CurrentScreenInstance;
            popupCanvasManager.CalculatePopupCanvasSortOrder(canvasPopup, CurrentPopupInstance);

            Task showPopupRoutine = null;
            switch (popupConfig.TransitionMode)
            {
                case TransitionMode.Sequential:
                    await newPopupInstance.Show(args);
                    if (screenFlow.Verbose)
                        Debug.Log($"[PopupManager:ShowNewPopup] -> Sequentially showed popup: {popupConfig.name}");
                    break;
                case TransitionMode.Parallel:
                    showPopupRoutine = newPopupInstance.Show(args);
                    if (screenFlow.Verbose)
                        Debug.Log(
                            $"[PopupManager:ShowNewPopup] -> Initiated parallel show for popup: {popupConfig.name}");
                    break;
            }

            if (showPopupRoutine != null)
            {
                await showPopupRoutine;
                if (screenFlow.Verbose)
                    Debug.Log($"[PopupManager:ShowNewPopup] -> Completed parallel show for popup: {popupConfig.name}");
            }
        }

        private void UpdatePopupState(PopupConfig popupConfig, IPopupBase newPopupInstance)
        {
            if (popupConfig == null || newPopupInstance == null)
            {
                Debug.LogError("[PopupManager:UpdatePopupState] -> Invalid popup config or instance");
                return;
            }

            newPopupInstance.OnClosePopupRequest += OnClosePopupRequest;
            popupConfigsStack.Add(popupConfig);
            popupInstancesStack.Add(newPopupInstance);
            if (screenFlow.Verbose)
                Debug.Log($"[PopupManager:UpdatePopupState] -> Added popup to stack: {popupConfig.name}");
        }

        private void NotifyPopupChange(PopupConfig oldPopupConfig, IPopupBase oldPopupInstance,
            PopupConfig newPopupConfig, IPopupBase newPopupInstance, Action<IScreenBase> onShow, ScreenFlow screenFlow)
        {
            if (newPopupInstance == null)
            {
                Debug.LogError("[PopupManager:NotifyPopupChange] -> New popup instance is null");
                return;
            }

            onShow?.Invoke(newPopupInstance);
            OnPopupOpen?.Invoke((oldPopupConfig, oldPopupInstance), (newPopupConfig, newPopupInstance));
            screenFlow?.CallOnShowForController(newPopupInstance);
            if (screenFlow.Verbose)
                Debug.Log($"[PopupManager:NotifyPopupChange] -> Notified popup change to: {newPopupConfig.name}");
        }

        private void OnClosePopupRequest(IPopupBase popupToClose)
        {
            if (popupToClose == null)
            {
                Debug.LogError("[PopupManager:OnClosePopupRequest] -> Popup to close is null");
                return;
            }

            screenFlow.ClosePopup(popupToClose);
            if (screenFlow.Verbose)
                Debug.Log(
                    $"[PopupManager:OnClosePopupRequest] -> Requested close for popup: {popupToClose.GameObject.name}");
        }

        private void DisposePopupFromHide(PopupConfig popupConfig, IPopupBase popupInstance,
            bool forceDontUnload = false)
        {
            if (popupConfig == null || popupInstance == null)
            {
                Debug.LogError("[PopupManager:DisposePopupFromHide] -> Invalid popup config or instance");
                return;
            }

            popupConfigsStack.Remove(popupConfig);
            popupInstancesStack.Remove(popupInstance);
            popupCanvasManager.RecyclePopupCanvas(popupInstance);
            popupInstance.OnClosePopupRequest -= OnClosePopupRequest;

            if (popupConfig.AssetLoaderConfig.IsInScene)
            {
                popupInstance.GameObject.SetActive(false);
                if (screenFlow.Verbose)
                    Debug.Log($"[PopupManager:DisposePopupFromHide] -> Deactivated in-scene popup: {popupConfig.name}");
            }
            else
            {
                UnityEngine.Object.Destroy(popupInstance.GameObject);
                if (!popupConfig.AssetLoaderConfig.DontUnloadAfterLoad && !forceDontUnload)
                {
                    popupConfig.AssetLoaderConfig.Unload();
                    if (screenFlow.Verbose)
                        Debug.Log(
                            $"[PopupManager:DisposePopupFromHide] -> Destroyed and unloaded popup: {popupConfig.name}");
                }
                else
                {
                    if (screenFlow.Verbose)
                        Debug.Log(
                            $"[PopupManager:DisposePopupFromHide] -> Destroyed popup without unloading: {popupConfig.name}");
                }
            }
        }

        private IPopupBase GetPopupInstance(PopupConfig popupConfig)
        {
            if (popupConfig?.AssetLoaderConfig?.LoadedAsset == null)
            {
                Debug.LogError("[PopupManager:GetPopupInstance] -> PopupConfig or LoadedAsset is null");
                return null;
            }

            switch (popupConfig.AssetLoaderConfig.LoadedAsset)
            {
                case GameObject screenGameObject:
                    return screenGameObject.GetComponent<ScreenBase>() as IPopupBase;
                case PopupBase popupBase:
                    return popupBase;
                case ScreenBase screenBase:
                    return screenBase as IPopupBase;
                default:
                    Debug.LogError(
                        $"[PopupManager:GetPopupInstance] -> Unsupported asset type: {popupConfig.AssetLoaderConfig.LoadedAsset.GetType()}");
                    return null;
            }
        }
    }
}