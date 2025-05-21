using UnityEngine;

namespace LegendaryTools
{
    public interface IPoolableGameObject : IPoolable
    {
        void OnConstruct(Vector3 position, Quaternion rotation, Transform parent);

        void OnCreate(Vector3 position, Quaternion rotation, Transform parent);
    }
}