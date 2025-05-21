using System;
using System.Collections;
using UnityEngine;

namespace LegendaryTools.Systems.ScreenFlow
{
    public abstract class ScreenBase : UnityBehaviour, IScreenBase
    {
#if ODIN_INSPECTOR
        [HideInInspector]    
#endif
        [SerializeField] private BackKeyBehaviourOverride backKeyBehaviourOverride = BackKeyBehaviourOverride.Inherit;
        
#if ODIN_INSPECTOR
        [Sirenix.OdinInspector.ShowInInspector]
#endif
        public BackKeyBehaviourOverride BackKeyBehaviourOverride
        {
            get => backKeyBehaviourOverride;
            set => backKeyBehaviourOverride = value;
        }
        
        public GameObject GameObject => this.gameObject;
        public event Action<IScreenBase> OnHideRequest;
        public event Action<IScreenBase> OnHideCompleted;
        public event Action<IScreenBase> OnDestroyed;
        public abstract IEnumerator Show(System.Object args);

        public virtual IEnumerator RequestHide(System.Object args)
        {
            OnHideRequest?.Invoke(this);
            yield return Hide(args);
            OnHideCompleted?.Invoke(this);
        }
        
        public abstract IEnumerator Hide(System.Object args);

        protected virtual void OnDestroy()
        {
            OnDestroyed?.Invoke(this);
        }

        protected internal void RaiseOnHideRequest(ScreenBase screenBase)
        {
            OnHideRequest?.Invoke(screenBase);
        }
        
        protected internal void RaiseOnHideCompleted(ScreenBase screenBase)
        {
            OnHideCompleted?.Invoke(screenBase);
        }
    }
}