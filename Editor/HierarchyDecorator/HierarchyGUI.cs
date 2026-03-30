using UnityEngine;
using UnityEditor;

namespace HierarchyDecorator
{
    public static class HierarchyGUI
    {
        private static GUIStyle TextStyle = new GUIStyle(EditorStyles.label);
        private static readonly Color DarkModeText = new Color(0.48f, 0.67f, 0.95f, 1f);
        private static readonly Color WhiteModeText = new Color(0.1f, 0.3f, 0.7f, 1f);
        private static readonly Color MissingPrefabText = new Color(0.95f, 0.55f, 0.55f);

        public static void DrawHierarchyStyle(HierarchyStyle style, Rect styleRect, Rect labelRect, string label, bool removePrefix = true)
        {
            if (removePrefix)
            {
                if (style.isRegex)
                {
                    if(style.capturedGroups != null && style.capturedGroups.Length > 0){
                        label = string.Join("", style.capturedGroups);
                    }
                }
                else
                {
                    label = label.Substring (style.prefix.Length).Trim ();
                }
            }

            ModeOptions styleSetting = style.GetCurrentMode (EditorGUIUtility.isProSkin);

            EditorGUI.DrawRect (styleRect, styleSetting.backgroundColour);
            EditorGUI.LabelField (labelRect, style.FormatString(label), style.style);
        }

        public static void DrawStandardContent(Rect rect, GameObject instance)
        {
            DrawStandardContent(rect, instance, null, null, null);
        }

        public static void DrawStandardContent(Rect rect, GameObject instance, GUIContent overrideContent, Color? overrideTextColour, Color? overrideIconColour)
        {
            HierarchyItem item = HierarchyManager.Current;

            // Get the content needed for the icon

            bool isPrefab = item.IsPrefab;
            bool isPrefabParent = item.PrefabInfo == PrefabInfo.Root; 
            bool isPrefabMissing = item.PrefabTypeInfo == PrefabAssetType.MissingAsset;
           
            GUIContent content = overrideContent ?? GetStandardContent (instance, isPrefabParent);

            // Handle colours

            Color textColour = EditorStyles.label.normal.textColor;
            if (isPrefab)
            {
                if (isPrefabMissing)
                    textColour = MissingPrefabText;
                else
                    textColour = (EditorGUIUtility.isProSkin) ? DarkModeText : WhiteModeText;
            }

            if (overrideTextColour.HasValue)
            {
                textColour = overrideTextColour.Value;
            }

            if (Selection.Contains(instance) && ! isPrefabMissing)
            {
                textColour = Color.white;
            }

            TextStyle.normal.textColor = textColour;
            Color iconColour = overrideIconColour ?? Color.white;
            if (Selection.Contains(instance) && !isPrefabMissing)
            {
                iconColour = Color.white;
            }

            // Draw prefab context icon

            if (isPrefabParent)
            {
                DrawPrefabArrow(rect);
            }

            // Draw label

            DrawStandardLabel (rect, content, instance.name, TextStyle, iconColour);

            // Add the small prefab indicator if required

            if (PrefabUtility.IsAddedGameObjectOverride(instance))
            {
                EditorGUI.LabelField(rect, EditorGUIUtility.IconContent("PrefabOverlayAdded Icon"));
            }
        }

        private static void DrawStandardLabel(Rect rect, GUIContent icon, string label, GUIStyle style, Color iconColour)
        {
            // Draw Label + Icon
            Vector2 originalIconSize = EditorGUIUtility.GetIconSize ();
            Color originalGuiColour = GUI.color;
            EditorGUIUtility.SetIconSize (Vector2.one * rect.height);
            {
                GUI.color = iconColour;
                EditorGUI.LabelField (rect, icon, style);
                GUI.color = originalGuiColour;

                rect.x += 18f;
                rect.y--;

                EditorGUI.LabelField (rect, label, style);
            }
            EditorGUIUtility.SetIconSize (originalIconSize);
            GUI.color = originalGuiColour;
        }

        private static void DrawPrefabArrow(Rect rect)
        {
            Rect iconRect = rect;
            iconRect.x = rect.width + rect.x;
            iconRect.width = rect.height;

            GUI.DrawTexture (iconRect, EditorGUIUtility.IconContent ("tab_next").image, ScaleMode.ScaleToFit);
        }

        // Content Helpers

        public static GUIContent GetStandardContent(GameObject instance, bool isPrefab)
        {
            if (isPrefab)
            {
                return new GUIContent()
                {
                    image = GetPrefabIcon(instance)
                };
            }

            return EditorGUIUtility.IconContent(GetGameObjectIcon(Selection.Contains(instance)));
        }

        public static Color GetTwoToneColour(Rect selectionRect)
        {
            bool isEvenRow = selectionRect.y % 32 != 0;

            if (EditorGUIUtility.isProSkin)
            {
                return isEvenRow ? Constants.DarkModeEvenColor : Constants.DarkModeOddColor;
            }
            else
            {
                return isEvenRow ? Constants.LightModeEvenColor : Constants.LightModeOddColor;
            }
        }

        // Version GUI Helpers

        public static void Space(float width = 9f)
        {
#if UNITY_2019_1_OR_NEWER
            EditorGUILayout.Space (width);
#else
            GUILayout.Space (width);
#endif
        }

        private static string GetGameObjectIcon(bool selected)
        {
            return selected ? "GameObject On Icon" : "GameObject Icon"; 
        }

        private static Texture2D GetPrefabIcon(GameObject instance)
        { 
            return PrefabUtility.GetIconForGameObject(instance);
        }
    }
}
