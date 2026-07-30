using System.Collections.Generic;
using UnityEngine;

namespace LegendaryTools.ViewBinding
{
    [CreateAssetMenu(
        fileName = "ViewDataBindingProfile",
        menuName = "Legendary Tools/View Binding/Binding Profile")]
    public sealed class ViewDataBindingProfile : ScriptableObject
    {
        [SerializeField] private List<ViewDataBinding> bindings = new List<ViewDataBinding>();

        public IReadOnlyList<ViewDataBinding> Bindings => bindings;

        public int AddBinding(ViewDataBinding binding)
        {
            if (binding == null)
            {
                throw new System.ArgumentNullException(nameof(binding));
            }

            binding.EnsureId();
            bindings.Add(binding);
            return bindings.Count - 1;
        }

        public bool RemoveBindingAt(int bindingIndex)
        {
            if (bindingIndex < 0 || bindingIndex >= bindings.Count)
            {
                return false;
            }

            bindings.RemoveAt(bindingIndex);
            return true;
        }

        internal void EnsureBindingIds()
        {
            for (int i = 0; i < bindings.Count; i++)
            {
                bindings[i]?.EnsureId();
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            EnsureBindingIds();
        }
#endif
    }
}
