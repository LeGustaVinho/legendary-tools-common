using UnityEditor;
using UnityEngine;

namespace HierarchyDecorator
{
    public class BranchCategoryColorDrawer : HierarchyDrawer
    {
        private enum CategoryType
        {
            None,
            TwoD,
            UI,
            ThreeD,
        }

        protected override bool DrawerIsEnabled(HierarchyItem item, Settings settings)
        {
            return item != null && item.GameObject != null;
        }

        protected override void DrawInternal(Rect rect, HierarchyItem item, Settings settings)
        {
            if (settings.styleData.HasStyle(item.DisplayName))
            {
                return;
            }

            CategoryType category = ResolveBranchCategory(item.Transform);
            if (category == CategoryType.None)
            {
                return;
            }

            Color color = GetCategoryColor(category);
            HierarchyGUI.DrawStandardContent(rect, item.GameObject, null, color, color);
        }

        private static CategoryType ResolveBranchCategory(Transform transform)
        {
            while (transform != null)
            {
                CategoryType category = ResolveSelfCategory(transform.gameObject);
                if (category != CategoryType.None)
                {
                    return category;
                }

                transform = transform.parent;
            }

            return CategoryType.None;
        }

        private static CategoryType ResolveSelfCategory(GameObject gameObject)
        {
            bool hasUI = false;
            bool has2D = false;
            bool has3D = false;

            var components = gameObject.GetComponents<Component>();
            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (component == null)
                {
                    continue;
                }

                if (IsUI(component))
                {
                    hasUI = true;
                    break;
                }

                if (IsTwoD(component))
                {
                    has2D = true;
                    continue;
                }

                if (IsThreeD(component))
                {
                    has3D = true;
                }
            }

            if (hasUI)
            {
                return CategoryType.UI;
            }

            if (has2D)
            {
                return CategoryType.TwoD;
            }

            if (has3D)
            {
                return CategoryType.ThreeD;
            }

            return CategoryType.None;
        }

        private static bool IsUI(Component component)
        {
            if (component is Canvas || component is RectTransform || component is CanvasRenderer)
            {
                return true;
            }

            string fullName = component.GetType().FullName;
            if (string.IsNullOrEmpty(fullName))
            {
                return false;
            }

            return fullName.StartsWith("UnityEngine.UI.")
                || fullName.StartsWith("UnityEngine.EventSystems.");
        }

        private static bool IsTwoD(Component component)
        {
            if (component is SpriteRenderer)
            {
                return true;
            }

            string fullName = component.GetType().FullName;
            if (string.IsNullOrEmpty(fullName))
            {
                return false;
            }

            return fullName.Contains("SortingGroup")
                || fullName.Contains("Tilemap")
                || fullName.Contains("Collider2D")
                || fullName.Contains("Rigidbody2D")
                || fullName.Contains("Joint2D")
                || fullName.Contains("Effector2D");
        }

        private static bool IsThreeD(Component component)
        {
            if (component is MeshRenderer || component is SkinnedMeshRenderer)
            {
                return true;
            }

            string fullName = component.GetType().FullName;
            if (string.IsNullOrEmpty(fullName))
            {
                return false;
            }

            return fullName.Contains("MeshFilter")
                || fullName.Contains("Collider")
                || fullName.Contains("Rigidbody")
                || fullName.Contains("Light")
                || fullName.Contains("Camera")
                || fullName.Contains("Terrain")
                || fullName.Contains("ReflectionProbe")
                || fullName.Contains("ParticleSystemRenderer");
        }

        private static Color GetCategoryColor(CategoryType category)
        {
            bool isDark = EditorGUIUtility.isProSkin;
            switch (category)
            {
                case CategoryType.TwoD:
                    return isDark ? new Color(0.38f, 0.86f, 0.67f, 1f) : new Color(0.13f, 0.50f, 0.35f, 1f);

                case CategoryType.UI:
                    return isDark ? new Color(1.00f, 0.79f, 0.38f, 1f) : new Color(0.72f, 0.41f, 0.10f, 1f);

                case CategoryType.ThreeD:
                    return isDark ? new Color(0.80f, 0.70f, 0.98f, 1f) : new Color(0.41f, 0.25f, 0.72f, 1f);

                default:
                    return EditorStyles.label.normal.textColor;
            }
        }
    }
}
