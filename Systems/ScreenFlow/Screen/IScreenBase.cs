using System;
using System.Collections;
using UnityEngine;

namespace LegendaryTools.Systems.ScreenFlow
{
    public interface IScreenBase
    {
        GameObject GameObject { get; }
        T GetComponent<T>();
        T GetComponentInParent<T>();
        BackKeyBehaviourOverride BackKeyBehaviourOverride { get; set; }
        event Action<IScreenBase> OnHideRequest;
        event Action<IScreenBase> OnHideCompleted;
        event Action<IScreenBase> OnDestroyed;
        IEnumerator Show(System.Object args);
        IEnumerator RequestHide(System.Object args);
        IEnumerator Hide(System.Object args);
    }
}