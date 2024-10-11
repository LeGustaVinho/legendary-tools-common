using UnityEngine;

namespace LegendaryTools
{
    public enum UniqueType
    {
        GameObject,
        ScriptableObject,
    }
    
    public interface IUnique
    {
        string Name { get; }
        string Guid { get; }
        GameObject GameObject { get; }
        ScriptableObject ScriptableObject { get; }
        UniqueType Type { get; }
        void AssignNewGuid();
    }
}