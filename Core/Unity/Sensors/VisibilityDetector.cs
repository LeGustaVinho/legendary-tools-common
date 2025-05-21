using System;
using UnityEngine;

namespace LegendaryTools
{
    [RequireComponent(typeof(Renderer))]
    public class VisibilityDetector : MonoBehaviour
    {
        [SerializeField] private bool checkOnStart;
#if ODIN_INSPECTOR
        [Sirenix.OdinInspector.ShowInInspector]
#endif
        public bool IsVisible { get; private set; }
        public event Action<bool> OnCameraVisibilityChange;

        protected void Start()
        {
            if (checkOnStart)
            {
                Renderer rendererComponent = GetComponent<Renderer>();
                if (rendererComponent != null)
                {
                    IsVisible = rendererComponent.isVisible;
                    OnCameraVisibilityChange?.Invoke(false);
                }
            }
        }

        private void OnBecameVisible()
        {
            IsVisible = true;
            OnCameraVisibilityChange?.Invoke(true);
        }
        
        private void OnBecameInvisible()
        {
            IsVisible = false;
            OnCameraVisibilityChange?.Invoke(false);
        }
    }
}