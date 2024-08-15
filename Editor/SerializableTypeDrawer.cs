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
            // Obtem a propriedade typeName
            SerializedProperty typeNameProperty = property.FindPropertyRelative("typeName");
            string currentTypeName = typeNameProperty.stringValue;

            //Obtem o attribute que define o filtro do type
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

            // Cria uma lista de nomes de tipos para o popup
            List<string> typeNames = types.Select(type => type.FullName).ToList();
            int currentIndex = !string.IsNullOrEmpty(currentTypeName) ? typeNames.IndexOf(currentTypeName) : 0;

            // Ajuste para um valor válido se necessário
            currentIndex = Mathf.Max(currentIndex, 0);

            // Cria um popup para selecionar o tipo
            int selectedIndex = EditorGUI.Popup(position, label.text, currentIndex,
                typeNames.ToArray());

            // Atualiza o valor da propriedade se um novo tipo for selecionado
            if (selectedIndex >= 0 && selectedIndex < typeNames.Count)
            {
                typeNameProperty.stringValue = typeNames[selectedIndex];
                property.serializedObject.ApplyModifiedProperties();
            }
        }
    }
}