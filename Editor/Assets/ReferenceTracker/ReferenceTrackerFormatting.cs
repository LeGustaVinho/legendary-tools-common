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

        public static string GetSerializedPropertyReferenceLabel(string propertyPath, string displayName)
        {
            string compactPath = CompactSerializedPropertyPath(propertyPath);
            if (!string.IsNullOrEmpty(compactPath))
            {
                return compactPath;
            }

            return string.IsNullOrEmpty(displayName) ? propertyPath : displayName;
        }

        private static string CompactSerializedPropertyPath(string propertyPath)
        {
            if (string.IsNullOrEmpty(propertyPath))
            {
                return string.Empty;
            }

            string[] parts = propertyPath.Split('.');
            System.Text.StringBuilder builder = new System.Text.StringBuilder(propertyPath.Length);

            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i];
                if (string.Equals(part, "Array", System.StringComparison.Ordinal) &&
                    i + 1 < parts.Length &&
                    TryGetArrayIndex(parts[i + 1], out int index))
                {
                    builder.Append('[');
                    builder.Append(index);
                    builder.Append(']');
                    i++;
                    continue;
                }

                if (builder.Length > 0 && builder[builder.Length - 1] != ']')
                {
                    builder.Append('.');
                }
                else if (builder.Length > 0 && builder[builder.Length - 1] == ']')
                {
                    builder.Append('.');
                }

                builder.Append(part);
            }

            return builder.ToString();
        }

        private static bool TryGetArrayIndex(string part, out int index)
        {
            index = -1;

            const string prefix = "data[";
            if (string.IsNullOrEmpty(part) ||
                !part.StartsWith(prefix, System.StringComparison.Ordinal) ||
                part[part.Length - 1] != ']')
            {
                return false;
            }

            string value = part.Substring(prefix.Length, part.Length - prefix.Length - 1);
            return int.TryParse(value, out index);
        }
    }
}
