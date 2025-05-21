using System;
using System.Collections;
using UnityEngine;

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
        
        public override IEnumerator Show(object args)
        {
            if (args != null)
            {
                if (args is TDataShow typedData)
                    yield return ShowT(typedData);
                else
                {
                    Debug.LogError(
                        $"[ScreenBaseT:Show] TypeMissMatch: Args is type {args.GetType()}, but was expected {typeof(TDataShow)}, Show() will not be called");
                    yield return null;
                }
            }
            else
            {
                yield return ShowT(null);
            }
        }
        
        public override IEnumerator Hide(object args)
        {
            if (args != null)
            {
                if (args is TDataHide typedData)
                    yield return HideT(typedData);
                else
                    yield return HideT(null);
            }
            else
            {
                yield return HideT(null);
            }
        }

        public override IEnumerator RequestHide(object args)
        {
            RaiseOnHideRequest(this);
            OnHideRequestT?.Invoke(this as T);
            yield return Hide(args);
            RaiseOnHideCompleted(this);
            OnHideCompletedT?.Invoke(this as T);
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            OnDestroyedT?.Invoke(this as T);
        }

        public abstract IEnumerator ShowT(TDataShow args);
        public abstract IEnumerator HideT(TDataHide args);
    }
}