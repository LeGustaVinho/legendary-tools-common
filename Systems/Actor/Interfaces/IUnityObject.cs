using UnityEngine;

namespace LegendaryTools.Actor
{
    public interface IUnityObject
    {
        HideFlags HideFlags { get; set; }

        int GetInstanceID();
    }
}