#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace LegendaryTools.SOAP
{
    /// <summary>
    /// Utility to ensure [SubAsset]-marked fields on a ScriptableObject are created and assigned as child assets.
    /// </summary>
    public static class SubAssetEditorUtility
    {
        /// <summary>
        /// Ensure all [SubAsset]-marked fields are created and assigned for the given ScriptableObject.
        /// </summary>
        public static bool EnsureSubAssets(UnityEngine.Object target)
        {
            if (target == null) return false;

            string path = AssetDatabase.GetAssetPath(target);
            if (string.IsNullOrEmpty(path))
                return false; // Not an asset (e.g., scene instance) — skip.

            if (target is not ScriptableObject so) return false;

            BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            IEnumerable<FieldInfo> fields = so.GetType()
                .GetFields(flags)
                .Where(f => f.IsDefined(typeof(SubAssetAttribute), true));

            bool anyCreated = false;

            foreach (FieldInfo field in fields)
            {
                if (!typeof(ScriptableObject).IsAssignableFrom(field.FieldType))
                    continue;

                if (!FieldIsSerialized(field))
                    continue;

                ScriptableObject current = field.GetValue(so) as ScriptableObject;
                if (current != null)
                    continue;

                Type fieldType = field.FieldType;
                if (fieldType.IsAbstract || fieldType.ContainsGenericParameters)
                {
                    Debug.LogWarning(
                        $"[SubAsset] Field '{field.Name}' on '{so.name}' cannot be instantiated (abstract/open generic).");
                    continue;
                }

                ScriptableObject child = ScriptableObject.CreateInstance(fieldType);
                child.name = BuildChildName(so, field);

                AssetDatabase.AddObjectToAsset(child, path);
                AssetDatabase.ImportAsset(path);

                field.SetValue(so, child);
                EditorUtility.SetDirty(so);
                EditorUtility.SetDirty(child);

                anyCreated = true;
            }

            if (anyCreated) AssetDatabase.SaveAssets();

            return anyCreated;
        }

        /// <summary>
        /// Ensures an individual field, when we already know the field info and owner.
        /// Used by the PropertyDrawer.
        /// </summary>
        public static bool EnsureSingleSubAsset(ScriptableObject owner, FieldInfo field, string explicitName = null)
        {
            if (owner == null || field == null) return false;

            string path = AssetDatabase.GetAssetPath(owner);
            if (string.IsNullOrEmpty(path)) return false;

            if (!typeof(ScriptableObject).IsAssignableFrom(field.FieldType)) return false;
            if (!FieldIsSerialized(field)) return false;

            ScriptableObject current = field.GetValue(owner) as ScriptableObject;
            if (current != null) return false;

            Type fieldType = field.FieldType;
            if (fieldType.IsAbstract || fieldType.ContainsGenericParameters) return false;

            ScriptableObject child = ScriptableObject.CreateInstance(fieldType);
            child.name = !string.IsNullOrWhiteSpace(explicitName) ? explicitName : BuildChildName(owner, field);

            AssetDatabase.AddObjectToAsset(child, path);
            AssetDatabase.ImportAsset(path);

            field.SetValue(owner, child);
            EditorUtility.SetDirty(owner);
            EditorUtility.SetDirty(child);
            AssetDatabase.SaveAssets();

            return true;
        }

        private static bool FieldIsSerialized(FieldInfo field)
        {
            if (field.IsPublic && !field.IsStatic && !field.IsInitOnly)
                return true;

            if (field.GetCustomAttribute<SerializeField>(true) != null)
                return true;

            return false;
        }

        private static string BuildChildName(ScriptableObject owner, FieldInfo field)
        {
            SubAssetAttribute attr = field.GetCustomAttribute<SubAssetAttribute>(true);
            if (attr != null && !string.IsNullOrWhiteSpace(attr.ChildName))
                return attr.ChildName;

            string typeShort = field.FieldType.Name;
            return $"{owner.name}.{field.Name} ({typeShort})";
        }
    }
}
#endif