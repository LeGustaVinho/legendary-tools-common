using System;

namespace LegendaryTools.Systems.ScreenFlow
{
    public interface IPopupBase : IScreenBase
    {
        IScreenBase ParentScreen { get; internal set; }
        internal event Action<IPopupBase> OnClosePopupRequest;
        event Action<IPopupBase> OnGoneToBackground;
        internal void GoToBackground(System.Object args);
        void OnGoToBackground(System.Object args);
        void ClosePopup();
    }
}