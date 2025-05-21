using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LegendaryTools.UI
{
    public interface IProximityUi<T> : IProximityDetector<T> , IDisposable
        where T : MonoBehaviour, ProximityDetector<T>.IProximityDetectable
    {
        event Action<IProximityUi<T>, T, UIFollowTransform> OnUiCreated;
        event Action<IProximityUi<T>, T, UIFollowTransform> OnUiRemoved;
    }
    
    public class ProximityUi<T> : IProximityUi<T>
        where T : MonoBehaviour, ProximityDetector<T>.IProximityDetectable
    {
        private IProximityDetector<T> ProximityDetector;
        private UIFollowTransform UiPrefab;
        private Canvas Canvas;
        private Vector2 Offset;
        private Camera Camera;
        private Transform Target;

        public event Action<IProximityUi<T>, T, UIFollowTransform> OnUiCreated;
        public event Action<IProximityUi<T>, T, UIFollowTransform> OnUiRemoved;
        
        public event Action<T> OnEntered
        {
            add => ProximityDetector.OnEntered += value;
            remove => ProximityDetector.OnEntered -= value;
        }
        public event Action<T> OnExited
        {
            add => ProximityDetector.OnExited += value;
            remove => ProximityDetector.OnExited -= value;
        }
        
        public List<T> OverlappingActors => ProximityDetector.OverlappingActors;
        
        protected readonly Dictionary<T, UIFollowTransform> Instances = new Dictionary<T, UIFollowTransform>();
        
        public ProximityUi(IProximityDetector<T> proximityDetector, UIFollowTransform uiPrefab, Canvas canvas, Camera camera, Transform target, Vector2 offset)
        {
            ProximityDetector = proximityDetector;
            UiPrefab = uiPrefab;
            Canvas = canvas;
            Offset = offset;
            Camera = camera;
            Target = target;

            ProximityDetector.OnEntered += OnEnterDetected;
            ProximityDetector.OnExited += OnExitDetected;
        }
        
        public void Dispose()
        {
            ProximityDetector.OnEntered -= OnEnterDetected;
            ProximityDetector.OnExited -= OnExitDetected;
            
            Instances.Clear();
            
            ProximityDetector = null;
            UiPrefab = null;
            Canvas = null;
            Offset = Vector2.zero;
            Camera = null;
            Target = null;
        }
        
        protected virtual void OnEnterDetected(T actor)
        {
            if (Canvas == null)
            {
                Debug.LogError("[ProximityUi:OnEnterDetected] You must provide a canvas");
            }
            if (UiPrefab == null)
            {
                Debug.LogError("[ProximityUi:OnEnterDetected] You must provide a UiPrefab");
            }

            if (Instances.ContainsKey(actor)) return;

            UIFollowTransform uiFollowTransform = CreateUi(UiPrefab, Canvas);

            if (Camera == null)
            {
                Camera = Camera.main;
            }

            uiFollowTransform.Camera = Camera;
            uiFollowTransform.Canvas = Canvas;
            uiFollowTransform.Target = Target;
            uiFollowTransform.Offset = Offset != Vector2.zero ? Offset : uiFollowTransform.Offset;
            
            Instances.Add(actor, uiFollowTransform);
            OnUiCreated?.Invoke(this, actor, uiFollowTransform);
        }

        protected virtual void OnExitDetected(T actor)
        {
            if (Instances.TryGetValue(actor, out UIFollowTransform uiFollowTransform))
            {
                OnUiRemoved?.Invoke(this, actor, uiFollowTransform);
                if (uiFollowTransform != null)
                {
                    DestroyUi(uiFollowTransform);
                }
                Instances.Remove(actor);
            }
        }

        protected virtual UIFollowTransform CreateUi(UIFollowTransform uiPrefab, Canvas canvas)
        {
            return Object.Instantiate(uiPrefab, canvas.transform);
        }
        
        protected virtual void DestroyUi(UIFollowTransform uiFollowTransform)
        {
            Object.Destroy(uiFollowTransform.gameObject);
        }
        
        public void ReportAsDestroyed(T actor, bool shouldCallTriggerExit = false)
        {
            ProximityDetector.ReportAsDestroyed(actor, shouldCallTriggerExit);
        }
    }
}