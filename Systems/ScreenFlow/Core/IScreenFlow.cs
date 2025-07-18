using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace LegendaryTools.Systems.ScreenFlow
{
    public interface IScreenFlow : IDisposable
    {
        bool IsTransiting { get; }
        bool IsPreloading { get; }
        ScreenConfig CurrentScreenConfig { get; }
        IScreenBase CurrentScreenInstance { get; }
        PopupConfig CurrentPopupConfig { get; }
        IPopupBase CurrentPopupInstance { get; }
        List<IPopupBase> CurrentPopupInstancesStack { get; }
        
        event Action<ScreenConfig, IScreenBase> OnStart;
        event Action<(ScreenConfig, IScreenBase), (ScreenConfig, IScreenBase)> OnScreenChange;
        event Action<(PopupConfig, IPopupBase), (PopupConfig, IPopupBase)> OnPopupOpen;
        
        int PopupStackCount { get; }
        Task Initialize();
        Task SendTrigger(string name, System.Object args = null, Action<IScreenBase> requestedScreenOnShow = null, Action<IScreenBase> previousScreenOnHide = null);
        
        public void BindController<T>(IScreenViewController<T> controller)
            where T : IScreenBase;
        
        public void UnBindController<T>(IScreenViewController<T> controller)
            where T : IScreenBase;
        Task SendTrigger(UIEntityBaseConfig uiEntity, System.Object args = null, Action<IScreenBase> requestedScreenOnShow = null, Action<IScreenBase> previousScreenOnHide = null);

        Task SendTriggerT<TConfig, TShow>(TConfig uiEntity, TShow args = null, Action<IScreenBase> requestedScreenOnShow = null, Action<IScreenBase> previousScreenOnHide = null)
            where TConfig : UIEntityBaseConfig
            where TShow : class;

        public Task SendTriggerT<TConfig, TShow, THide>(TConfig uiEntity, TShow args = null, 
            Action<ScreenBaseT<TConfig, TShow, THide>> requestedScreenOnShow = null,
            Action<IScreenBase> previousScreenOnHide = null)
            where TConfig : UIEntityBaseConfig
            where TShow : class
            where THide : class;

        Task MoveBack(System.Object args = null, Action<IScreenBase> onShow = null, Action<IScreenBase> onHide = null);
        void CloseForegroundPopup(System.Object args = null, Action<IScreenBase> onShow = null, Action<IScreenBase> onHide = null);
        void ClosePopup(IPopupBase popupBase, System.Object args = null, Action<IScreenBase> onShow = null, Action<IScreenBase> onHide = null);
    }
}