using System;
using UnityEngine;

namespace LegendaryTools.Actor
{
    public interface IComponent : IUnityObject
    {
        T GetComponent<T>() where T : Component;

        Component GetComponent(Type type);

        Component GetComponent(string type);

        Component GetComponentInChildren(Type t);

        T GetComponentInChildren<T>() where T : Component;

        Component GetComponentInParent(Type t);

        T GetComponentInParent<T>() where T : Component;

        Component[] GetComponents(Type type);

        T[] GetComponents<T>() where T : Component;

        Component[] GetComponentsInChildren(Type t, bool includeInactive);

        T[] GetComponentsInChildren<T>(bool includeInactive) where T : Component;

        Component[] GetComponentsInParent(Type t, bool includeInactive = false);

        T[] GetComponentsInParent<T>(bool includeInactive = false) where T : Component;
    }
}