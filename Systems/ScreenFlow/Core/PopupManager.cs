using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace LegendaryTools.Systems.ScreenFlow
{
    public class PopupManager
    {
        private readonly ScreenFlowConfig config;
        private readonly Canvas canvas;
        private readonly CanvasScaler canvasScaler;
        private readonly GraphicRaycaster graphicRaycaster;
        private readonly PopupCanvasManager popupCanvasManager;
        private readonly List<PopupConfig> popupConfigsStack = new List<PopupConfig>();
        private readonly List<IPopupBase> popupInstancesStack = new List<IPopupBase>();

        public PopupConfig CurrentPopupConfig => popupConfigsStack.Count > 0 ? popupConfigsStack[popupConfigsStack.Count - 1] : null;
        public IPopupBase CurrentPopupInstance => popupInstancesStack.Count > 0 ? popupInstancesStack[popupInstancesStack.Count - 1] : null;
        public List<IPopupBase> CurrentPopupInstancesStack2 => new List<IPopupBase>(popupInstancesStack);
        public int PopupStackCount => popupInstancesStack.Count;

        public event Action<(PopupConfig, IPopupBase), (PopupConfig, IPopupBase)> OnPopupOpen;

        public PopupManager(ScreenFlowConfig config, Canvas canvas, CanvasScaler canvasScaler, GraphicRaycaster graphicRaycaster)
        {
            this.config = config;
            this.canvas = canvas;
            this.canvasScaler = canvasScaler;
            this.graphicRaycaster = graphicRaycaster;
            popupCanvasManager = new PopupCanvasManager(config, canvas, canvasScaler, graphicRaycaster);
        }

        public async Task PopupTransitTo(PopupConfig popupConfig, ScreenConfig currentScreenConfig, object args,
            Action<IScreenBase> onShow, Action<IScreenBase> onHide, ScreenFlow screenFlow)
        {
            PopupConfig oldPopupConfig = CurrentPopupConfig;
            IPopupBase oldPopupInstance = CurrentPopupInstance;

            await StartPopupTransition(popupConfig, args);
            await HandleCurrentPopup(oldPopupConfig, oldPopupInstance, args, popupConfig, currentScreenConfig, screenFlow);

            IPopupBase newPopupInstance = null;
            Canvas canvasPopup = null;
            LoadAndInstantiatePopup(popupConfig, instance => newPopupInstance = instance, canvas => canvasPopup = canvas, screenFlow);
            if (newPopupInstance == null) return;

            await ShowNewPopup(popupConfig, newPopupInstance, canvasPopup, args, screenFlow);
            UpdatePopupState(popupConfig, newPopupInstance);
            NotifyPopupChange(oldPopupConfig, oldPopupInstance, popupConfig, newPopupInstance, onShow, screenFlow);
        }

        public async Task ClosePopupOp(IPopupBase popupBase, object args, Action<IScreenBase> onShow, Action<IScreenBase> onHide, ScreenFlow screenFlow)
        {
            int stackIndex = popupInstancesStack.FindIndex(item => item == popupBase);
            if (stackIndex < 0) return;

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
                        screenFlow.CallOnHideForController(popupBase);
                        DisposePopupFromHide(popupConfig, popupBase);
                        break;
                    case TransitionMode.Parallel:
                        hidePopupRoutine = popupBase.RequestHide(args);
                        break;
                }

                if (behindPopupInstance != null &&
                    (behindPopupConfig.GoingBackgroundBehaviour == PopupGoingBackgroundBehaviour.JustHide ||
                     behindPopupConfig.GoingBackgroundBehaviour == PopupGoingBackgroundBehaviour.HideAndDestroy))
                {
                    switch (popupConfig.TransitionMode)
                    {
                        case TransitionMode.Sequential:
                            await behindPopupInstance.Show(args);
                            break;
                        case TransitionMode.Parallel:
                            showPopupRoutine = behindPopupInstance.Show(args);
                            break;
                    }
                }

                if (hidePopupRoutine != null)
                {
                    await hidePopupRoutine;
                    onHide?.Invoke(popupBase);
                    screenFlow.CallOnHideForController(popupBase);
                    DisposePopupFromHide(popupConfig, popupBase);
                }

                if (showPopupRoutine != null)
                {
                    await showPopupRoutine;
                }
            }
            else
            {
                onHide?.Invoke(popupBase);
                DisposePopupFromHide(popupConfig, popupBase);
            }

            onShow?.Invoke(behindPopupInstance);
        }

        public async Task HandlePopupsOnScreenTransit(ScreenConfig currentScreenConfig, object args)
        {
            if (CurrentPopupInstance == null) return;

            switch (currentScreenConfig.PopupBehaviourOnScreenTransition)
            {
                case PopupsBehaviourOnScreenTransition.PreserveAllOnHide:
                    break;
                case PopupsBehaviourOnScreenTransition.HideFirstThenTransit:
                    await ClosePopupOp(CurrentPopupInstance, args, null, null, null);
                    for (int i = popupConfigsStack.Count - 1; i >= 0; i--)
                    {
                        DisposePopupFromHide(popupConfigsStack[i], popupInstancesStack[i]);
                    }
                    break;
                case PopupsBehaviourOnScreenTransition.DestroyAllThenTransit:
                    for (int i = popupConfigsStack.Count - 1; i >= 0; i--)
                    {
                        DisposePopupFromHide(popupConfigsStack[i], popupInstancesStack[i]);
                    }
                    break;
            }
        }

        public void Dispose()
        {
            foreach (var popup in popupInstancesStack)
            {
                popup.OnClosePopupRequest -= OnClosePopupRequest;
            }
            popupConfigsStack.Clear();
            popupInstancesStack.Clear();
        }

        private async Task StartPopupTransition(PopupConfig popupConfig, object args)
        {
            if (!popupConfig.AssetLoaderConfig.IsLoaded)
            {
                await popupConfig.AssetLoaderConfig.WaitForLoadingAsync<GameObject>();
            }
        }

        private async Task HandleCurrentPopup(PopupConfig oldPopupConfig, IPopupBase oldPopupInstance, object args,
            PopupConfig newPopupConfig, ScreenConfig currentScreenConfig, ScreenFlow screenFlow)
        {
            if (oldPopupConfig == null || oldPopupInstance == null) return;

            bool allowStackablePopups = currentScreenConfig.AllowStackablePopups;
            Task hidePopupRoutine = null;

            switch (oldPopupConfig.TransitionMode)
            {
                case TransitionMode.Sequential:
                    if (allowStackablePopups)
                    {
                        oldPopupInstance.GoToBackground(args);
                        screenFlow.CallOnGoingToBackgroundForController(oldPopupInstance);
                        if (oldPopupConfig.GoingBackgroundBehaviour != PopupGoingBackgroundBehaviour.DontHide)
                        {
                            await oldPopupInstance.RequestHide(args);
                            screenFlow.CallOnHideForController(oldPopupInstance);
                            if (oldPopupConfig.GoingBackgroundBehaviour == PopupGoingBackgroundBehaviour.HideAndDestroy)
                            {
                                DisposePopupFromHide(oldPopupConfig, oldPopupInstance, oldPopupConfig == newPopupConfig);
                            }
                        }
                    }
                    else
                    {
                        await oldPopupInstance.RequestHide(args);
                        screenFlow.CallOnHideForController(oldPopupInstance);
                        DisposePopupFromHide(oldPopupConfig, oldPopupInstance, oldPopupConfig == newPopupConfig);
                    }
                    break;
                case TransitionMode.Parallel:
                    if (allowStackablePopups)
                    {
                        oldPopupInstance.GoToBackground(args);
                        screenFlow.CallOnGoingToBackgroundForController(oldPopupInstance);
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

            if (hidePopupRoutine != null)
            {
                await hidePopupRoutine;
                screenFlow.CallOnHideForController(oldPopupInstance);
                if (oldPopupConfig.GoingBackgroundBehaviour == PopupGoingBackgroundBehaviour.HideAndDestroy)
                {
                    DisposePopupFromHide(oldPopupConfig, oldPopupInstance);
                }
            }
        }

        private void LoadAndInstantiatePopup(PopupConfig popupConfig, Action<IPopupBase> onInstanceCreated,
            Action<Canvas> onCanvasAssigned, ScreenFlow screenFlow)
        {
            if (popupConfig.AssetLoaderConfig.LoadedAsset == null)
            {
                Debug.LogError($"[PopupManager:LoadAndInstantiatePopup] -> Failed to load {popupConfig.AssetLoaderConfig.name}", popupConfig);
                return;
            }

            IPopupBase newPopupInstance = GetPopupInstance(popupConfig);
            if (newPopupInstance == null)
            {
                Debug.LogError($"[PopupManager:LoadAndInstantiatePopup] -> {popupConfig.AssetLoaderConfig.LoadedAsset.GetType()} doesn't have any component that inherits from {nameof(IPopupBase)} interface", popupConfig);
                return;
            }

            Canvas canvasPopup;
            if (popupConfig.AssetLoaderConfig.IsInScene)
            {
                canvasPopup = screenFlow.GetComponentInParent<Canvas>() ?? screenFlow.GetComponentInChildren<Canvas>();
                newPopupInstance.GameObject.SetActive(true);
            }
            else
            {
                canvasPopup = popupCanvasManager.GetCanvas(newPopupInstance);
                if (canvasPopup != null)
                {
                    newPopupInstance = screenFlow.InstantiateUIElement(newPopupInstance as ScreenBase, null,
                        out RectTransform instanceRT, out RectTransform prefabRT) as IPopupBase;
                }
                else
                {
                    newPopupInstance = screenFlow.InstantiateUIElement(newPopupInstance as ScreenBase, null,
                        out RectTransform instanceRT, out RectTransform prefabRT) as IPopupBase;
                    canvasPopup = popupCanvasManager.AllocatePopupCanvas(newPopupInstance);
                    screenFlow.ReparentUIElement(instanceRT, prefabRT, canvasPopup.transform);
                }
            }

            onInstanceCreated?.Invoke(newPopupInstance);
            onCanvasAssigned?.Invoke(canvasPopup);
        }

        private async Task ShowNewPopup(PopupConfig popupConfig, IPopupBase newPopupInstance,
            Canvas canvasPopup, object args, ScreenFlow screenFlow)
        {
            newPopupInstance.ParentScreen = screenFlow.CurrentScreenInstance;
            popupCanvasManager.CalculatePopupCanvasSortOrder(canvasPopup, CurrentPopupInstance);

            Task showPopupRoutine = null;
            switch (popupConfig.TransitionMode)
            {
                case TransitionMode.Sequential:
                    await newPopupInstance.Show(args);
                    break;
                case TransitionMode.Parallel:
                    showPopupRoutine = newPopupInstance.Show(args);
                    break;
            }

            if (showPopupRoutine != null)
            {
                await showPopupRoutine;
            }
        }

        private void UpdatePopupState(PopupConfig popupConfig, IPopupBase newPopupInstance)
        {
            newPopupInstance.OnClosePopupRequest += OnClosePopupRequest;
            popupConfigsStack.Add(popupConfig);
            popupInstancesStack.Add(newPopupInstance);
        }

        private void NotifyPopupChange(PopupConfig oldPopupConfig, IPopupBase oldPopupInstance,
            PopupConfig newPopupConfig, IPopupBase newPopupInstance, Action<IScreenBase> onShow, ScreenFlow screenFlow)
        {
            onShow?.Invoke(newPopupInstance);
            OnPopupOpen?.Invoke((oldPopupConfig, oldPopupInstance), (newPopupConfig, newPopupInstance));
            screenFlow.CallOnShowForController(newPopupInstance);
        }

        private void OnClosePopupRequest(IPopupBase popupToClose)
        {
            // This could trigger a command to close the popup
        }

        private void DisposePopupFromHide(PopupConfig popupConfig, IPopupBase popupInstance, bool forceDontUnload = false)
        {
            popupConfigsStack.Remove(popupConfig);
            popupInstancesStack.Remove(popupInstance);
            popupCanvasManager.RecyclePopupCanvas(popupInstance);
            popupInstance.OnClosePopupRequest -= OnClosePopupRequest;

            if (popupConfig.AssetLoaderConfig.IsInScene)
            {
                popupInstance.GameObject.SetActive(false);
            }
            else
            {
                UnityEngine.Object.Destroy(popupInstance.GameObject);
                if (!popupConfig.AssetLoaderConfig.DontUnloadAfterLoad && !forceDontUnload)
                {
                    popupConfig.AssetLoaderConfig.Unload();
                }
            }
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
    }
}