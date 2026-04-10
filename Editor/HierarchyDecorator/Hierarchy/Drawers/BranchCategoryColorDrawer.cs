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
            return item != null
                && item.GameObject != null
                && settings != null
                && settings.enableBranchCategoryColorDrawer;
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

            Color color = GetCategoryColor(settings, category);
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

        private static Color GetCategoryColor(Settings settings, CategoryType category)
        {
            if (settings == null || settings.branchCategoryColors == null)
            {
                return EditorStyles.label.normal.textColor;
            }

            settings.branchCategoryColors.EnsureInitialized();

            BranchCategoryThemeColors colors = EditorGUIUtility.isProSkin
                ? settings.branchCategoryColors.darkMode
                : settings.branchCategoryColors.lightMode;

            switch (category)
            {
                case CategoryType.TwoD:
                    return colors.twoD;

                case CategoryType.UI:
                    return colors.ui;

                case CategoryType.ThreeD:
                    return colors.threeD;

                default:
                    return EditorStyles.label.normal.textColor;
            }
        }
    }
}
