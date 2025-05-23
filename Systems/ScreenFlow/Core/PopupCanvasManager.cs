using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace LegendaryTools.Systems.ScreenFlow
{
    public class PopupCanvasManager
    {
        private readonly Dictionary<IPopupBase, Canvas> allocatedPopupCanvas = new Dictionary<IPopupBase, Canvas>();
        private readonly List<Canvas> availablePopupCanvas = new List<Canvas>();
        private readonly ScreenFlowConfig config;
        private readonly Canvas canvas;
        private readonly CanvasScaler canvasScaler;
        private readonly GraphicRaycaster graphicRaycaster;

        public PopupCanvasManager(ScreenFlowConfig config, Canvas canvas, CanvasScaler canvasScaler, GraphicRaycaster graphicRaycaster)
        {
            this.config = config;
            this.canvas = canvas;
            this.canvasScaler = canvasScaler;
            this.graphicRaycaster = graphicRaycaster;
        }
        
        public Canvas GetCanvas(IScreenBase popupBase)
        {
            Canvas canvasPopup = popupBase.GetComponentInParent<Canvas>();
            return canvasPopup;
        }

        public void ConfigureCanvasComponents(Canvas canvas, CanvasScaler canvasScaler, GraphicRaycaster graphicRaycaster)
        {
            canvas.renderMode = this.canvas.renderMode;
            canvas.pixelPerfect = this.canvas.pixelPerfect;
            canvas.sortingLayerID = this.canvas.sortingLayerID;
            canvas.sortingOrder = this.canvas.sortingOrder;
            canvas.targetDisplay = this.canvas.targetDisplay;
            canvas.additionalShaderChannels = this.canvas.additionalShaderChannels;
            canvas.worldCamera = this.canvas.worldCamera;
            
            canvasScaler.uiScaleMode = this.canvasScaler.uiScaleMode;
            canvasScaler.referenceResolution = this.canvasScaler.referenceResolution;
            canvasScaler.screenMatchMode = this.canvasScaler.screenMatchMode;
            canvasScaler.matchWidthOrHeight = this.canvasScaler.matchWidthOrHeight;
            canvasScaler.referencePixelsPerUnit = this.canvasScaler.referencePixelsPerUnit;
            canvasScaler.scaleFactor = this.canvasScaler.scaleFactor;
            canvasScaler.physicalUnit = this.canvasScaler.physicalUnit;
            canvasScaler.fallbackScreenDPI = this.canvasScaler.fallbackScreenDPI;
            canvasScaler.defaultSpriteDPI = this.canvasScaler.defaultSpriteDPI;
            
            graphicRaycaster.ignoreReversedGraphics = this.graphicRaycaster.ignoreReversedGraphics;
            graphicRaycaster.blockingObjects = this.graphicRaycaster.blockingObjects;
        }

        public Canvas AllocatePopupCanvas(IPopupBase popupInstance)
        {
            Canvas availableCanvas = null;
            if (availablePopupCanvas.Count > 0)
            {
                availableCanvas = availablePopupCanvas[availablePopupCanvas.Count - 1];
                availablePopupCanvas.RemoveAt(availablePopupCanvas.Count - 1);
            }
            else
            {
                availableCanvas = CreatePopupCanvas();
            }

            allocatedPopupCanvas.Add(popupInstance, availableCanvas);
            availableCanvas.gameObject.SetActive(true);
            return availableCanvas;
        }

        public void RecyclePopupCanvas(IPopupBase popupInstance)
        {
            if (allocatedPopupCanvas.TryGetValue(popupInstance, out Canvas popupCanvas))
            {
                allocatedPopupCanvas.Remove(popupInstance);
                availablePopupCanvas.Add(popupCanvas);
                popupCanvas.gameObject.SetActive(false);
            }
        }

        public Canvas CreatePopupCanvas()
        {
            Canvas canvasPopup;
            if (config.OverridePopupCanvasPrefab != null)
            {
                canvasPopup = UnityEngine.Object.Instantiate(config.OverridePopupCanvasPrefab);
                UnityEngine.Object.DontDestroyOnLoad(canvasPopup);
            }
            else
            {
                GameObject canvasPopupGo = new GameObject("[Canvas] - Popup");
                UnityEngine.Object.DontDestroyOnLoad(canvasPopupGo);

                canvasPopup = canvasPopupGo.AddComponent<Canvas>();
                CanvasScaler canvasScalerPopup = canvasPopupGo.AddComponent<CanvasScaler>();
                GraphicRaycaster graphicRaycasterPopup = canvasPopupGo.AddComponent<GraphicRaycaster>();

                ConfigureCanvasComponents(canvasPopup, canvasScalerPopup, graphicRaycasterPopup);
            }

            return canvasPopup;
        }
        
        public void CalculatePopupCanvasSortOrder(Canvas canvasPopup, IPopupBase topOfStackPopupInstance)
        {
            if (canvasPopup != null)
            {
                if (topOfStackPopupInstance == null)
                {
                    canvasPopup.sortingOrder = canvas.sortingOrder + 1;
                }
                else
                {
                    if (allocatedPopupCanvas.TryGetValue(topOfStackPopupInstance, out Canvas currentPopupCanvas))
                    {
                        canvasPopup.sortingOrder = currentPopupCanvas.sortingOrder + 1;
                    }
                }

                canvasPopup.name = "[Canvas] - Popup #" + canvasPopup.sortingOrder;
            }
        }
    }
}