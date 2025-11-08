#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LegendaryTools.NodeEditor
{
    /// <summary>
    /// Resolves node rectangles and GUIStyles, and renders [ShowInNode] properties.
    /// </summary>
    public class NodeAppearance
    {
        /// <summary>
        /// Calculates the logical rectangle for a node, honoring custom size and ShowInNode fields.
        /// </summary>
        public Rect GetNodeRect(IEditorNode node, Dictionary<Type, FieldInfo[]> showCache)
        {
            if (node == null) return new Rect();

            if (node.HasCustomNodeSize)
            {
                Vector2 s = node.NodeSize;
                return new Rect(node.Position, new Vector2(Mathf.Max(10f, s.x), Mathf.Max(10f, s.y)));
            }

            FieldInfo[] fields = GetShowInNodeFields(node, showCache);
            float dynamicAdd = fields.Length > 0
                ? fields.Length * (NodeEditorLayout.InlineFieldHeight + 4f) + 6f
                : 0f;

            float height = NodeEditorLayout.DefaultNodeBaseHeight + dynamicAdd;
            return new Rect(node.Position, new Vector2(NodeEditorLayout.DefaultNodeWidth, height));
        }

        /// <summary>
        /// Resolves the GUIStyle to use for a node based on selected state and the node's custom style names.
        /// This method never returns a cached instance that would break after assembly reload.
        /// </summary>
        public GUIStyle ResolveStyle(NodeEditorContext ctx, IEditorNode node, bool selected)
        {
            if (node == null)
                return GuiStyleService.Resolve(null, null, selected);

            if (node.HasCustomNodeStyles)
            {
                string styleName = selected ? node.SelectedStyleName : node.NormalStyleName;
                return GuiStyleService.Resolve(node.StyleSkin, styleName, selected);
            }

            // Use service defaults
            return GuiStyleService.Resolve(null, null, selected);
        }

        /// <summary>
        /// Draws serialized properties decorated with [ShowInNode] inside the node window.
        /// </summary>
        public void DrawInlineFields(IEditorNode node, Dictionary<Type, FieldInfo[]> cache)
        {
            if (!(node is Object uo)) return;

            using (SerializedObject so = new(uo))
            {
                so.UpdateIfRequiredOrScript();

                foreach (FieldInfo fi in GetShowInNodeFields(node, cache))
                {
                    SerializedProperty sp = so.FindProperty(fi.Name);
                    if (sp == null) continue;
                    EditorGUILayout.PropertyField(sp, true);
                }

                if (so.ApplyModifiedProperties())
                    EditorUtility.SetDirty(uo);
            }
        }

        private static FieldInfo[] GetShowInNodeFields(IEditorNode node, Dictionary<Type, FieldInfo[]> cache)
        {
            if (node is null) return Array.Empty<FieldInfo>();
            Type t = node.GetType();
            if (cache.TryGetValue(t, out FieldInfo[] cached)) return cached;

            BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            FieldInfo[] fields = t.GetFields(flags);
            List<FieldInfo> list = new();

            foreach (FieldInfo f in fields)
            {
                if (f.IsStatic) continue;

                bool isSerializable =
                    (f.IsPublic && f.GetCustomAttribute<NonSerializedAttribute>() == null)
                    || f.GetCustomAttribute<SerializeField>() != null;

                if (!isSerializable) continue;
                if (f.GetCustomAttribute<ShowInNodeAttribute>() != null)
                    list.Add(f);
            }

            FieldInfo[] arr = list.ToArray();
            cache[t] = arr;
            return arr;
        }
    }
}
#endif