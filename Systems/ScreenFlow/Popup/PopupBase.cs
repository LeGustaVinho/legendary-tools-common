using System;

namespace LegendaryTools.Systems.ScreenFlow
{
    public abstract class PopupBase : ScreenBase, IPopupBase
    {
        private IScreenBase parentScreen;
#if ODIN_INSPECTOR
        [Sirenix.OdinInspector.HideInEditorMode]
#endif
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
        
        void IPopupBase.GoToBackground(System.Object args)
        {
            OnGoToBackground(args);
            OnGoneToBackground?.Invoke(this);
        }

        public abstract void OnGoToBackground(System.Object args);

        public virtual void ClosePopup()
        {
            onClosePopupRequest?.Invoke(this);
        }
    }
}