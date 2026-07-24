using UnityEditor;
using UnityEngine;

namespace LegendaryTools.ViewBinding.Editor
{
    public static class BindingInspectorStyles
    {
        private static GUIStyle cardStyle;
        private static GUIStyle headerStyle;
        private static GUIStyle sectionTitleStyle;
        private static GUIStyle typeLabelStyle;
        private static GUIStyle pathButtonStyle;

        public static GUIStyle CardStyle
        {
            get
            {
                if (cardStyle == null)
                {
                    cardStyle = CreateCardStyle();
                }

                return cardStyle;
            }
        }

        public static GUIStyle HeaderStyle
        {
            get
            {
                if (headerStyle == null)
                {
                    headerStyle = CreateHeaderStyle();
                }

                return headerStyle;
            }
        }

        public static GUIStyle SectionTitleStyle
        {
            get
            {
                if (sectionTitleStyle == null)
                {
                    sectionTitleStyle = CreateSectionTitleStyle();
                }

                return sectionTitleStyle;
            }
        }

        public static GUIStyle TypeLabelStyle
        {
            get
            {
                if (typeLabelStyle == null)
                {
                    typeLabelStyle = CreateTypeLabelStyle();
                }

                return typeLabelStyle;
            }
        }

        public static GUIStyle PathButtonStyle
        {
            get
            {
                if (pathButtonStyle == null)
                {
                    pathButtonStyle = CreatePathButtonStyle();
                }

                return pathButtonStyle;
            }
        }

        private static GUIStyle CreateCardStyle()
        {
            var style = new GUIStyle(EditorStyles.helpBox)
            {
                padding = new RectOffset(10, 10, 8, 10),
                margin = new RectOffset(0, 0, 4, 8)
            };
            return style;
        }

        private static GUIStyle CreateHeaderStyle()
        {
            return new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 13,
                alignment = TextAnchor.MiddleLeft
            };
        }

        private static GUIStyle CreateSectionTitleStyle()
        {
            return new GUIStyle(EditorStyles.miniBoldLabel)
            {
                fontSize = 10
            };
        }

        private static GUIStyle CreateTypeLabelStyle()
        {
            return new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleRight,
                clipping = TextClipping.Clip
            };
        }

        private static GUIStyle CreatePathButtonStyle()
        {
            return new GUIStyle(EditorStyles.popup)
            {
                alignment = TextAnchor.MiddleLeft
            };
        }
    }
}
