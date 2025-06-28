using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace LegendaryTools.Systems.ScreenFlow
{
    public class UIEntityLoader
    {
        private readonly List<UIEntityBaseConfig> preloadQueue = new List<UIEntityBaseConfig>();
        private readonly ScreenFlow screenFlow;
        private readonly PopupManager popupManager;
        private Task nextScreenLoading;
        private Task hideScreenRoutine;
        private Task showScreenRoutine;

        public bool IsPreloading => preloadQueue.Count > 0;

        public UIEntityLoader(ScreenFlow screenFlow, PopupManager popupManager)
        {
            this.screenFlow = screenFlow;
            this.popupManager = popupManager;
        }

        public async Task Preload(ScreenFlowConfig config)
        {
            preloadQueue.Clear();
            ScreenConfig[] screens = Array.FindAll(config.Screens, item => item.AssetLoaderConfig.PreLoad);
            if (screens.Length > 0)
                preloadQueue.AddRange(screens);
            PopupConfig[] popups = Array.FindAll(config.Popups, item => item.AssetLoaderConfig.PreLoad);
            if (popups.Length > 0)
                preloadQueue.AddRange(popups);
            await PreloadingAssets();
        }

        public async Task StartScreenTransition(ScreenConfig screenConfig, object args)
        {
            if (!screenConfig.AssetLoaderConfig.IsLoaded && !screenConfig.AssetLoaderConfig.IsLoading)
            {
                if (preloadQueue.Contains(screenConfig))
                {
                    preloadQueue.Remove(screenConfig);
                }
                nextScreenLoading = screenConfig.AssetLoaderConfig.WaitForLoadingAsync<GameObject>();
            }
            await popupManager.HandlePopupsOnScreenTransit(screenFlow.CurrentScreenConfig, args);
        }

        public async Task HideCurrentScreen(ScreenConfig oldScreenConfig, IScreenBase oldScreenInstance, object args, Action<IScreenBase> onHide)
        {
            if (oldScreenConfig == null || oldScreenInstance == null) return;

            switch (oldScreenConfig.TransitionMode)
            {
                case TransitionMode.Sequential:
                    await oldScreenInstance.RequestHide(args);
                    HandleScreenHideCompletion(oldScreenConfig, oldScreenInstance, onHide);
                    break;
                case TransitionMode.Parallel:
                    hideScreenRoutine = oldScreenInstance.RequestHide(args);
                    break;
            }
        }

        public async Task<ScreenBase> LoadAndInstantiateScreen(ScreenConfig screenConfig)
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
                Debug.LogError($"[UIEntityLoader:LoadAndInstantiateScreen] -> Failed to load {screenConfig.AssetLoaderConfig.name}", screenConfig);
                return null;
            }

            ScreenBase newScreenPrefab = GetScreenPrefab(screenConfig);
            if (newScreenPrefab == null)
            {
                Debug.LogError($"[UIEntityLoader:LoadAndInstantiateScreen] -> {screenConfig.AssetLoaderConfig.LoadedAsset.GetType()} doesn't have any component that inherits from ScreenBase class", screenConfig);
                return null;
            }

            ScreenBase newScreenInstance;
            if (screenConfig.AssetLoaderConfig.IsInScene)
            {
                newScreenInstance = newScreenPrefab;
                newScreenInstance.gameObject.SetActive(true);
            }
            else
            {
                newScreenInstance = InstantiateUIElement(newScreenPrefab, screenFlow.GetComponent<RectTransform>(),
                    out RectTransform instanceRT, out RectTransform prefabRT);
            }

            return newScreenInstance;
        }

        public async Task ShowNewScreen(ScreenConfig screenConfig, ScreenBase newScreenInstance, object args)
        {
            switch (screenConfig.TransitionMode)
            {
                case TransitionMode.Sequential:
                    await newScreenInstance.Show(args);
                    break;
                case TransitionMode.Parallel:
                    showScreenRoutine = newScreenInstance.Show(args);
                    break;
            }

            if (hideScreenRoutine != null)
            {
                await hideScreenRoutine;
                HandleScreenHideCompletion(screenFlow.CurrentScreenConfig, screenFlow.CurrentScreenInstance, null);
                hideScreenRoutine = null;
            }

            if (showScreenRoutine != null)
            {
                await showScreenRoutine;
                showScreenRoutine = null;
            }
        }

        public T InstantiateUIElement<T>(T screenPrefab, Transform parent, out RectTransform elementInstanceRT,
            out RectTransform elementPrefabRT) where T : ScreenBase
        {
            if (screenPrefab == null)
            {
                Debug.LogError("[UIEntityLoader:InstantiateUIElement] -> Screen prefab is null");
                elementInstanceRT = null;
                elementPrefabRT = null;
                return null;
            }

            T elementInstance = UnityEngine.Object.Instantiate(screenPrefab);
            elementInstanceRT = elementInstance.GetComponent<RectTransform>();
            elementPrefabRT = screenPrefab.GetComponent<RectTransform>();
            ReparentUIElement(elementInstanceRT, elementPrefabRT, parent);
            return elementInstance;
        }

        public void ReparentUIElement(RectTransform elementInstanceRT, RectTransform elementPrefabRT, Transform parent)
        {
            if (elementInstanceRT == null || elementPrefabRT == null)
            {
                Debug.LogError("[UIEntityLoader:ReparentUIElement] -> Invalid parameters: elementInstanceRT or elementPrefabRT is null");
                return;
            }

            elementInstanceRT.SetParent(parent);
            elementInstanceRT.SetAsLastSibling();
            elementInstanceRT.localPosition = elementPrefabRT.localPosition;
            elementInstanceRT.localRotation = elementPrefabRT.localRotation;
            elementInstanceRT.localScale = elementPrefabRT.localScale;
            elementInstanceRT.anchoredPosition3D = elementPrefabRT.anchoredPosition3D;
            elementInstanceRT.anchorMin = elementPrefabRT.anchorMin;
            elementInstanceRT.anchorMax = elementPrefabRT.anchorMax;
            elementInstanceRT.sizeDelta = elementPrefabRT.sizeDelta;
            elementInstanceRT.offsetMin = elementPrefabRT.offsetMin;
            elementInstanceRT.offsetMax = elementPrefabRT.offsetMax;
        }

        public void Dispose()
        {
            preloadQueue.Clear();
            nextScreenLoading = null;
            hideScreenRoutine = null;
            showScreenRoutine = null;
        }

        private async Task PreloadingAssets()
        {
            foreach (UIEntityBaseConfig uiEntityBaseConfig in preloadQueue)
            {
                await uiEntityBaseConfig.AssetLoaderConfig.WaitForLoadingAsync<GameObject>();
            }
        }

        private void HandleScreenHideCompletion(ScreenConfig screenConfig, IScreenBase screenInstance, Action<IScreenBase> onHide)
        {
            onHide?.Invoke(screenInstance);
            screenFlow.CallOnHideForController(screenInstance);

            if (screenConfig.AssetLoaderConfig.IsInScene)
            {
                screenInstance.GameObject.SetActive(false);
            }
            else
            {
                UnityEngine.Object.Destroy(screenInstance.GameObject);
                if (!screenConfig.AssetLoaderConfig.DontUnloadAfterLoad)
                {
                    screenConfig.AssetLoaderConfig.Unload();
                }
            }
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
    }
}