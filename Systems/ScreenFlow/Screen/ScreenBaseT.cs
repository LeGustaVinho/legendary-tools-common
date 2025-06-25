using System;
using System.Collections;
using UnityEngine;
using System.Threading.Tasks;

namespace LegendaryTools.Systems.ScreenFlow
{
    public abstract class ScreenBaseT<T, TDataShow, TDataHide> : ScreenBase
        where T : class
        where TDataShow : class
        where TDataHide : class
    {
        public event Action<T> OnHideRequestT;
        public event Action<T> OnHideCompletedT;
        public event Action<T> OnDestroyedT;
        
        public override async Task Show(object args)
        {
            if (args != null)
            {
                if (args is TDataShow typedData)
                    await ShowT(typedData);
                else
                {
                    Debug.LogError(
                        $"[ScreenBaseT:Show] TypeMissMatch: Args is type {args.GetType()}, but was expected {typeof(TDataShow)}, Show() will not be called");
                }
            }
            else
            {
                await ShowT(null);
            }
        }
        
        public override async Task Hide(object args)
        {
            if (args != null)
            {
                if (args is TDataHide typedData)
                    await HideT(typedData);
                else
                    await HideT(null);
            }
            else
            {
                await HideT(null);
            }
        }

        public override async Task RequestHide(object args)
        {
            RaiseOnHideRequest(this);
            OnHideRequestT?.Invoke(this as T);
            await Hide(args);
            RaiseOnHideCompleted(this);
            OnHideCompletedT?.Invoke(this as T);
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            OnDestroyedT?.Invoke(this as T);
        }

        public abstract Task ShowT(TDataShow args);
        public abstract Task HideT(TDataHide args);
    }
}