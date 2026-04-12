using UnityEngine;

namespace LegendaryTools.Editor
{
    internal static class ReferenceTrackerFormatting
    {
        public static string GetHierarchyPath(GameObject gameObject)
        {
            if (gameObject == null)
            {
                return "<null>";
            }

            Transform current = gameObject.transform;
            string path = current.name;

            while (current.parent != null)
            {
                current = current.parent;
                path = current.name + "/" + path;
            }

            return path;
        }

        public static string GetComponentLabel(Component component)
        {
            if (component == null)
            {
                return "<null>";
            }

            string typeName = component.GetType().Name;
            Component[] sameTypeComponents = component.GetComponents(component.GetType());

            if (sameTypeComponents == null || sameTypeComponents.Length <= 1)
            {
                return typeName;
            }

            int index = 0;
            for (int i = 0; i < sameTypeComponents.Length; i++)
            {
                if (sameTypeComponents[i] == component)
                {
                    index = i;
                    break;
                }
            }

            return string.Format("{0} [{1}]", typeName, index);
        }
    }
}
