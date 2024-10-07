using UnityEngine;

namespace LegendaryTools
{
    public interface IScriptableObjectContent<T>
        where T : IScriptableObjectContent<T>
    {
        ScriptableObjectContainer<T> Parent { get; set; }
        T Clone();
    }
    
    [System.Serializable]
    public class ScriptableObjectContainer<T> : UnityObject
        where T : IScriptableObjectContent<T>
    {
#if ODIN_INSPECTOR
        [Sirenix.OdinInspector.InlineProperty]
#endif
        [SerializeField] private T data;
        public virtual T Data
        {
            get
            {
                T clone = data.Clone();
                clone.Parent = this;
                return clone;
            }
        }
    }
}