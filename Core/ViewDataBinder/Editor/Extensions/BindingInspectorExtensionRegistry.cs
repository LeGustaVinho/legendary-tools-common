using System;
using System.Collections.Generic;

namespace LegendaryTools.ViewBinding.Editor
{
    public static class BindingInspectorExtensionRegistry
    {
        private static readonly List<IViewDataBindingInspectorExtension> Extensions =
            new List<IViewDataBindingInspectorExtension>();

        public static IReadOnlyList<IViewDataBindingInspectorExtension> RegisteredExtensions => Extensions;

        public static void Register(IViewDataBindingInspectorExtension extension)
        {
            if (extension == null)
            {
                throw new ArgumentNullException(nameof(extension));
            }

            if (Extensions.Contains(extension))
            {
                return;
            }

            Extensions.Add(extension);
            Extensions.Sort((left, right) =>
            {
                int placementComparison = left.Placement.CompareTo(right.Placement);
                return placementComparison != 0
                    ? placementComparison
                    : left.Order.CompareTo(right.Order);
            });
        }

        public static void Unregister(IViewDataBindingInspectorExtension extension)
        {
            if (extension == null)
            {
                return;
            }

            Extensions.Remove(extension);
        }
    }
}
