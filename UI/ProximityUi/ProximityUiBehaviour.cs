using System;
using UnityEngine;

namespace LegendaryTools.UI
{
    [RequireComponent(typeof(Collider))]
    public class ProximityUiBehaviour<T> : ProximityDetector<T>, IProximityUi<T>
        where T : MonoBehaviour, ProximityDetector<T>.IProximityDetectable
    {
#if ODIN_INSPECTOR
        [Sirenix.OdinInspector.Required]
#endif
        public UIFollowTransform UiPrefab;
#if ODIN_INSPECTOR
        [Sirenix.OdinInspector.Required]
#endif
        public Canvas Canvas;
        public Vector2 Offset;
        public Camera Camera;
        public Transform Target;
        
        public event Action<IProximityUi<T>, T, UIFollowTransform> OnUiCreated
        {
            add => ProximityUi.OnUiCreated += value;
            remove => ProximityUi.OnUiCreated -= value;
        }
        public event Action<IProximityUi<T>, T, UIFollowTransform> OnUiRemoved
        {
            add => ProximityUi.OnUiRemoved += value;
            remove => ProximityUi.OnUiRemoved -= value;
        }
        
        protected ProximityUi<T> ProximityUi;
        
        protected virtual void Awake()
        {
            ProximityUi = new ProximityUi<T>(this, UiPrefab, Canvas, Camera, 
                Target == null ? transform : Target, Offset);
        }

        protected virtual void OnDestroy()
        {
            ProximityUi.Dispose();
        }
        
        public void Dispose()
        {
            Destroy(gameObject);
        }
    }
} 