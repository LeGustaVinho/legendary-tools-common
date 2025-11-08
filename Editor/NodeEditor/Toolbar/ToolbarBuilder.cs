using System;
using UnityEditor;
using UnityEngine;

namespace LegendaryTools.NodeEditor
{
    /// <summary>
    /// Concrete builder bridging high-level toolbar API with Unity's GUI calls.
    /// </summary>
    public class ToolbarBuilder : IToolbarBuilder
    {
        private readonly ToolbarContext _ctx;

        public ToolbarBuilder(ToolbarContext ctx)
        {
            _ctx = ctx;
        }

        public void AddButton(string text, Action onClick, Func<bool> enabled = null, string tooltip = null)
        {
            using (new EditorGUI.DisabledScope(enabled != null && !enabled()))
            {
                GUIContent content = new(text, string.IsNullOrEmpty(tooltip) ? text : tooltip);
                if (GUILayout.Button(content, EditorStyles.toolbarButton, GUILayout.MinWidth(40)))
                {
                    onClick?.Invoke();
                    _ctx.Repaint();
                }
            }
        }

        public void AddDropdown(string text, Action<IMenuBuilder> buildMenu, Func<bool> enabled = null,
            string tooltip = null)
        {
            using (new EditorGUI.DisabledScope(enabled != null && !enabled()))
            {
                GUIContent content = new(text, string.IsNullOrEmpty(tooltip) ? text : tooltip);
                if (EditorGUILayout.DropdownButton(content, FocusType.Passive, EditorStyles.toolbarDropDown,
                        GUILayout.MinWidth(60)))
                {
                    ContextMenuBuilder contextMenu = new();
                    try
                    {
                        buildMenu?.Invoke(contextMenu);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogException(ex);
                    }

                    contextMenu.ShowAsContext();
                }
            }
        }

        public void AddLabel(Func<string> textProvider, string tooltip = null)
        {
            string txt = textProvider != null ? textProvider() : string.Empty;
            GUIContent content = new(txt, string.IsNullOrEmpty(tooltip) ? txt : tooltip);
            GUILayout.Label(content, EditorStyles.miniLabel);
        }

        public void AddToggle(Func<bool> get, Action<bool> set, string label = null, Func<bool> enabled = null,
            string tooltip = null)
        {
            using (new EditorGUI.DisabledScope(enabled != null && !enabled()))
            {
                bool value = get != null && get();
                GUIContent content = new(label ?? string.Empty, tooltip ?? string.Empty);
                bool newVal = GUILayout.Toggle(value, content, EditorStyles.toolbarButton);
                if (newVal != value) set?.Invoke(newVal);
            }
        }

        public void AddObjectField<TObj>(Func<UnityEngine.Object> get, Action<UnityEngine.Object> set, float width,
            string tooltip = null)
            where TObj : UnityEngine.Object
        {
            using (new EditorGUIUtility.IconSizeScope(Vector2.zero))
            {
                float prev = EditorGUIUtility.labelWidth;
                EditorGUIUtility.labelWidth = 0f;
                TObj obj = (TObj)EditorGUILayout.ObjectField(
                    GUIContent.none,
                    get != null ? get() as TObj : null,
                    typeof(TObj),
                    false,
                    GUILayout.Width(width));
                EditorGUIUtility.labelWidth = prev;

                if (set != null) set(obj);
            }
        }

        public void AddTextField(Func<string> get, Action<string> set, float width, string placeholder = null,
            Func<bool> enabled = null, string tooltip = null)
        {
            using (new EditorGUI.DisabledScope(enabled != null && !enabled()))
            {
                string val = get != null ? get() : string.Empty;
                string newVal = EditorGUILayout.TextField(
                    new GUIContent(placeholder ?? string.Empty, tooltip ?? string.Empty), val,
                    EditorStyles.toolbarTextField, GUILayout.Width(width));
                if (!string.Equals(val, newVal)) set?.Invoke(newVal);
            }
        }

        public void AddFlexibleSpace()
        {
            GUILayout.FlexibleSpace();
        }

        public void AddSeparator()
        {
            Rect rect = GUILayoutUtility.GetRect(1, 1, GUILayout.Width(8));
            rect.y += 2;
            rect.height = EditorGUIUtility.singleLineHeight - 4;
            EditorGUI.DrawRect(new Rect(rect.x + 3, rect.y, 1, rect.height), new Color(1, 1, 1, 0.15f));
        }
    }
}