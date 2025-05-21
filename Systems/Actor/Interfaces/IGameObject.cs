using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LegendaryTools.Actor
{
    public interface IGameObject : IUnityObject
    {
        string Name { get; set; }
        
        string Tag { get; set; }
        
        bool ActiveInHierarchy { get; }
        
        bool ActiveSelf { get; }
        
        int Layer { get; set; }
        
        Scene Scene { get; }

        void SetActive(bool value);

        Component AddComponent(Type componentType);
        
        T AddComponent<T>() where T : Component;
    }
}