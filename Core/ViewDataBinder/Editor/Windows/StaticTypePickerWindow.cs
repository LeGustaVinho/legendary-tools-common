using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace LegendaryTools.ViewBinding.Editor
{
    public sealed class StaticTypePickerWindow : PopupWindowContent
    {
        private static IReadOnlyList<Type> cachedTypes;

        private readonly Action<Type> onSelected;
        private readonly IReadOnlyList<Type> types;
        private string search = string.Empty;
        private Vector2 scroll;

        public StaticTypePickerWindow(Action<Type> onSelected)
        {
            this.onSelected = onSelected;
            types = GetStaticTypes();
        }

        public override Vector2 GetWindowSize()
        {
            return new Vector2(520f, 420f);
        }

        public override void OnGUI(Rect rect)
        {
            EditorGUILayout.Space(4f);
            GUI.SetNextControlName("StaticTypeSearch");
            search = EditorGUILayout.TextField(search, EditorStyles.toolbarSearchField);
            EditorGUILayout.Space(4f);

            scroll = EditorGUILayout.BeginScrollView(scroll);

            IEnumerable<Type> filtered = types;
            if (!string.IsNullOrWhiteSpace(search))
            {
                filtered = filtered.Where(type =>
                    type.FullName != null &&
                    type.FullName.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            foreach (Type type in filtered.Take(300))
            {
                string label = type.FullName ?? type.Name;
                if (GUILayout.Button(label, EditorStyles.label))
                {
                    onSelected?.Invoke(type);
                    editorWindow.Close();
                }
            }

            EditorGUILayout.EndScrollView();
        }

        private static IReadOnlyList<Type> GetStaticTypes()
        {
            if (cachedTypes != null)
            {
                return cachedTypes;
            }

            var result = new List<Type>();
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    result.AddRange(assembly
                        .GetTypes()
                        .Where(HasBindableStaticMember));
                }
                catch (ReflectionTypeLoadException exception)
                {
                    result.AddRange(exception.Types
                        .Where(type => type != null && HasBindableStaticMember(type)));
                }
            }

            cachedTypes = result
                .Distinct()
                .OrderBy(type => type.FullName, StringComparer.Ordinal)
                .ToArray();
            return cachedTypes;
        }

        private static bool HasBindableStaticMember(Type type)
        {
            if (type == null || type.ContainsGenericParameters)
            {
                return false;
            }

            const BindingFlags flags = BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy;

            if (type.GetFields(flags).Any(field => !field.IsSpecialName))
            {
                return true;
            }

            return type.GetProperties(flags).Any(property => property.GetIndexParameters().Length == 0);
        }
    }
}
