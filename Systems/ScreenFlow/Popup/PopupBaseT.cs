using System;
using UnityEngine;

namespace LegendaryTools.Systems.ScreenFlow
{
    public abstract class PopupBaseT<T, TDataShow, TDataHide> : ScreenBaseT<T, TDataShow, TDataHide>, IPopupBase
        where T : class
        where TDataShow : class
        where TDataHide : class
    {
#if ODIN_INSPECTOR
        [Sirenix.OdinInspector.HideInEditorMode]
#endif
        private IScreenBase parentScreen;
        IScreenBase IPopupBase.ParentScreen
        {
            get => parentScreen;
            set => parentScreen = value;
        }

        private Action<IPopupBase> onClosePopupRequest;
        event Action<IPopupBase> IPopupBase.OnClosePopupRequest
        {
            add => onClosePopupRequest += value;
            remove => onClosePopupRequest -= value;
        }
        public event Action<IPopupBase> OnGoneToBackground;

        public event Action<T> OnClosePopupRequestT;
        public event Action<T> OnGoneToBackgroundT;
        
        public abstract void OnGoToBackgroundT(TDataHide args);
        
        void IPopupBase.GoToBackground(object args)
        {
            if (args != null && args is TDataHide typedData)
                OnGoToBackgroundT(typedData);
            else
                OnGoToBackgroundT(null);
            
            OnGoneToBackground?.Invoke(this);
            OnGoneToBackgroundT?.Invoke(this as T);
        }

        public void OnGoToBackground(object args)
        {
        }

        public virtual void ClosePopup()
        {
            onClosePopupRequest?.Invoke(this);
            OnClosePopupRequestT?.Invoke(this as T);
        }
    }
}