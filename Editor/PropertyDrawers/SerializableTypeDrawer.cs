using UnityEngine;
using UnityEditor;
using System;
using System.Linq;
using System.Collections.Generic;

namespace LegendaryTools.Editor
{
    [CustomPropertyDrawer(typeof(SerializableType))]
    public class SerializableTypeDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            SerializedProperty typeNameProperty = property.FindPropertyRelative("typeName");
            string currentTypeName = typeNameProperty.stringValue;
            
            object[] customAttributes = fieldInfo.GetCustomAttributes(false);
            TypeFilterAttribute filterAttribute = null;
            foreach (object custom in customAttributes)
            {
                if (custom is TypeFilterAttribute typeFilterAttribute)
                {
                    filterAttribute = typeFilterAttribute;
                }
            }
            
            Type[] types = Type.EmptyTypes;
            
            if (filterAttribute == null)
            {
                types = TypeExtension.GetAllTypes(t => !t.IsAbstract && !t.IsEnum && !t.IsPrimitive && !t.IsClass && t.IsPublic);
            }
            else
            {
                types = TypeExtension.GetAllTypes(t => t.IsSameOrSubclass(filterAttribute.BaseType) &&
                                                       !t.IsAbstract && !t.IsGenericType);
            }
            
            List<string> typeNames = types.Select(type => type.FullName).ToList();
            int currentIndex = !string.IsNullOrEmpty(currentTypeName) ? typeNames.IndexOf(currentTypeName) : 0;
            
            currentIndex = Mathf.Max(currentIndex, 0);
            
            int selectedIndex = EditorGUI.Popup(position, label.text, currentIndex,
                typeNames.ToArray());
            
            if (selectedIndex >= 0 && selectedIndex < typeNames.Count)
            {
                typeNameProperty.stringValue = typeNames[selectedIndex];
                property.serializedObject.ApplyModifiedProperties();
            }
        }
    }
}