using System;
using System.Collections;
using System.Threading.Tasks;
using UnityEngine;

namespace LegendaryTools.Systems.ScreenFlow
{
    public interface IScreenBase
    {
        GameObject GameObject { get; }
        T GetComponent<T>();
        T GetComponentInParent<T>();
        T GetComponentInChildren<T>();
        BackKeyBehaviourOverride BackKeyBehaviourOverride { get; set; }
        event Action<IScreenBase> OnHideRequest;
        event Action<IScreenBase> OnHideCompleted;
        event Action<IScreenBase> OnDestroyed;
        Task Show(System.Object args);
        Task RequestHide(System.Object args);
        Task Hide(System.Object args);
    }
}