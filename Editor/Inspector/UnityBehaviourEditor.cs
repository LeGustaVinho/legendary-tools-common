using System;
using System.Collections;
using System.Reflection;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using LegendaryTools.Inspector;

namespace LegendaryTools.Editor.Inspector
{
    [CustomEditor(typeof(UnityBehaviour), editorForChildClasses: true)]
    public class UnityBehaviourEditor : UnityEditor.Editor
    {
        // Dictionary to store parameter values for each method
        private Dictionary<string, object[]> methodParameters = new Dictionary<string, object[]>();
        // Dictionary to store foldout state for methods with parameters
        private Dictionary<string, bool> foldoutStates = new Dictionary<string, bool>();

        public override void OnInspectorGUI()
        {
            // Initialize the serialized object
            SerializedObject serializedObject = new SerializedObject(target);
            SerializedProperty iterator = serializedObject.GetIterator();

            // Draw the script reference
            iterator.NextVisible(true); // Move to m_Script
            EditorGUILayout.PropertyField(iterator, true);

            // Collect serialized fields and their order values
            List<(SerializedProperty property, float order)> serializedProperties = new List<(SerializedProperty, float)>();
            EditorGUILayout.LabelField("Unity Behaviour", EditorStyles.boldLabel);

            while (iterator.NextVisible(false))
            {
                // Get field info for the current serialized property
                FieldInfo fieldInfo = target.GetType().GetField(iterator.name,
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

                if (fieldInfo != null)
                {
                    // Check for PropertyOrderAttribute
                    PropertyOrderAttribute orderAttribute = fieldInfo
                        .GetCustomAttributes(typeof(PropertyOrderAttribute), true)
                        .FirstOrDefault() as PropertyOrderAttribute;
                    float order = orderAttribute != null ? orderAttribute.Order : 0f;
                    serializedProperties.Add((iterator.Copy(), order));
                }
                else
                {
                    // Default order if no field info is found
                    serializedProperties.Add((iterator.Copy(), 0f));
                }
            }

            // Collect C# native properties, only from the declaring class (not ancestors)
            List<(PropertyInfo property, float order, bool isReadOnly)> nativeProperties = new List<(PropertyInfo, float, bool)>();
            PropertyInfo[] properties = target.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            foreach (PropertyInfo prop in properties)
            {
                if (prop.GetMethod != null) // Ensure the property has a getter
                {
                    PropertyOrderAttribute orderAttribute = prop
                        .GetCustomAttributes(typeof(PropertyOrderAttribute), true)
                        .FirstOrDefault() as PropertyOrderAttribute;
                    float order = orderAttribute != null ? orderAttribute.Order : 0f;
                    bool isReadOnly = !prop.CanWrite;
                    nativeProperties.Add((prop, order, isReadOnly));
                }
            }

            // Combine serialized fields and native properties, sorted by order
            List<(object item, float order)> allItems = new List<(object, float)>();
            allItems.AddRange(serializedProperties.Select(p => (item: (object)p.property, p.order)));
            allItems.AddRange(nativeProperties.Select(p => (item: (object)p.property, p.order)));
            allItems = allItems.OrderBy(x => x.order).ToList();

            // Draw all items in order
            foreach (var item in allItems)
            {
                if (item.item is SerializedProperty serializedProp)
                {
                    EditorGUILayout.PropertyField(serializedProp, true);
                }
                else if (item.item is PropertyInfo nativeProp)
                {
                    bool isReadOnly = nativeProperties.First(p => p.property == nativeProp).isReadOnly;
                    DrawNativeProperty(nativeProp, isReadOnly);
                }
            }

            // Apply any changes to the serialized object
            serializedObject.ApplyModifiedProperties();

            // Draw buttons for methods with ButtonAttribute
            DrawButtons();
        }

        private void DrawNativeProperty(PropertyInfo prop, bool isReadOnly)
        {
            Type propType = prop.PropertyType;
            object currentValue = prop.GetValue(target);
            
            EditorGUI.BeginDisabledGroup(isReadOnly);
            if (propType.IsArray || (propType.IsGenericType && propType.GetGenericTypeDefinition() == typeof(List<>)))
            {
                Type elementType = propType.IsArray ? propType.GetElementType() : propType.GetGenericArguments()[0];
                DrawArrayOrListField(prop.Name, elementType, currentValue, isReadOnly);
            }
            else
            {
                DrawSingleField(prop.Name, propType, currentValue, isReadOnly);
            }
            EditorGUI.EndDisabledGroup();
        }

        private void DrawButtons()
        {
            UnityBehaviour behaviour = (UnityBehaviour)target;
            Type type = behaviour.GetType();
            MethodInfo[] methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            foreach (MethodInfo method in methods)
            {
                ButtonAttribute buttonAttribute = method
                    .GetCustomAttributes(typeof(ButtonAttribute), true)
                    .FirstOrDefault() as ButtonAttribute;

                if (buttonAttribute != null)
                {
                    // Determine button name: use custom name or method name
                    string buttonName = string.IsNullOrEmpty(buttonAttribute.Name) ? method.Name : buttonAttribute.Name;
                    int buttonHeight = buttonAttribute.HasDefinedButtonHeight ? buttonAttribute.ButtonHeight : 0;

                    // Get method parameters
                    ParameterInfo[] parameters = method.GetParameters();

                    // Begin a GUI box for the button and parameters
                    Rect boxRect = EditorGUILayout.BeginVertical(GUI.skin.box);
                    GUI.Box(boxRect, GUIContent.none);

                    if (parameters.Length > 0 && buttonAttribute.DisplayParameters)
                    {
                        // Initialize parameter values if not already done
                        if (!methodParameters.ContainsKey(method.Name))
                        {
                            methodParameters[method.Name] = new object[parameters.Length];
                        }

                        // Initialize foldout state if not already done
                        if (!foldoutStates.ContainsKey(method.Name))
                        {
                            foldoutStates[method.Name] = buttonAttribute.Expanded;
                        }

                        // Draw parameters, optionally inside a foldout
                        if (!buttonAttribute.Expanded)
                        {
                            foldoutStates[method.Name] = EditorGUILayout.Foldout(foldoutStates[method.Name], $"{buttonName} Parameters", true);
                        }

                        if (buttonAttribute.Expanded || foldoutStates[method.Name])
                        {
                            EditorGUI.indentLevel++;
                            for (int i = 0; i < parameters.Length; i++)
                            {
                                ParameterInfo param = parameters[i];
                                Type paramType = param.ParameterType;
                                string paramName = param.Name;

                                // Handle array and List<T> parameters
                                if (paramType.IsArray || (paramType.IsGenericType && paramType.GetGenericTypeDefinition() == typeof(List<>)))
                                {
                                    Type elementType = paramType.IsArray ? paramType.GetElementType() : paramType.GetGenericArguments()[0];
                                    methodParameters[method.Name][i] = DrawArrayOrListField(paramName, elementType, methodParameters[method.Name][i]);
                                }
                                else
                                {
                                    // Handle single-value parameters
                                    methodParameters[method.Name][i] = DrawSingleField(paramName, paramType, methodParameters[method.Name][i]);
                                }
                            }
                            EditorGUI.indentLevel--;
                        }
                    }

                    // Draw the button
                    if (buttonHeight > 0)
                    {
                        if (GUILayout.Button(buttonName, GUILayout.Height(buttonHeight)))
                        {
                            InvokeMethod(method, behaviour, parameters);
                        }
                    }
                    else
                    {
                        if (GUILayout.Button(buttonName))
                        {
                            InvokeMethod(method, behaviour, parameters);
                        }
                    }

                    // End the GUI box
                    EditorGUILayout.EndVertical();
                }
            }
        }

        private object DrawSingleField(string label, Type type, object currentValue, bool isReadOnly = false)
        {
            if (type == typeof(int))
            {
                return EditorGUILayout.IntField(label, (int)(currentValue ?? 0));
            }
            else if (type == typeof(float))
            {
                return EditorGUILayout.FloatField(label, (float)(currentValue ?? 0f));
            }
            else if (type == typeof(string))
            {
                return EditorGUILayout.TextField(label, (string)(currentValue ?? ""));
            }
            else if (type == typeof(bool))
            {
                return EditorGUILayout.Toggle(label, (bool)(currentValue ?? false));
            }
            else if (type == typeof(Vector2))
            {
                return EditorGUILayout.Vector2Field(label, (Vector2)(currentValue ?? Vector2.zero));
            }
            else if (type == typeof(Vector3))
            {
                return EditorGUILayout.Vector3Field(label, (Vector3)(currentValue ?? Vector3.zero));
            }
            else if (type == typeof(Vector4))
            {
                return EditorGUILayout.Vector4Field(label, (Vector4)(currentValue ?? Vector4.zero));
            }
            else if (type == typeof(Color))
            {
                return EditorGUILayout.ColorField(label, (Color)(currentValue ?? Color.white));
            }
            else if (type == typeof(Rect))
            {
                return EditorGUILayout.RectField(label, (Rect)(currentValue ?? Rect.zero));
            }
            else if (type == typeof(Bounds))
            {
                return EditorGUILayout.BoundsField(label, (Bounds)(currentValue ?? new Bounds()));
            }
            else if (type == typeof(Quaternion))
            {
                Vector3 euler = ((Quaternion)(currentValue ?? Quaternion.identity)).eulerAngles;
                euler = EditorGUILayout.Vector3Field(label, euler);
                return Quaternion.Euler(euler);
            }
            else if (typeof(UnityEngine.Object).IsAssignableFrom(type))
            {
                return EditorGUILayout.ObjectField(label, (UnityEngine.Object)currentValue, type, true);
            }
            else
            {
                EditorGUILayout.LabelField($"Unsupported type: {type.Name}");
                return currentValue;
            }
        }

        private object DrawArrayOrListField(string label, Type elementType, object currentValue, bool isReadOnly = false)
        {
            // Initialize array or list if null
            if (currentValue == null)
            {
                if (elementType == typeof(int)) currentValue = new int[0];
                else if (elementType == typeof(float)) currentValue = new float[0];
                else if (elementType == typeof(string)) currentValue = new string[0];
                else if (elementType == typeof(bool)) currentValue = new bool[0];
                else if (elementType == typeof(Vector2)) currentValue = new Vector2[0];
                else if (elementType == typeof(Vector3)) currentValue = new Vector3[0];
                else if (elementType == typeof(Vector4)) currentValue = new Vector4[0];
                else if (elementType == typeof(Color)) currentValue = new Color[0];
                else if (elementType == typeof(Rect)) currentValue = new Rect[0];
                else if (elementType == typeof(Bounds)) currentValue = new Bounds[0];
                else if (elementType == typeof(Quaternion)) currentValue = new Quaternion[0];
                else if (typeof(UnityEngine.Object).IsAssignableFrom(elementType)) currentValue = Array.CreateInstance(elementType, 0);
                else
                {
                    EditorGUILayout.LabelField($"Unsupported array/list element type: {elementType.Name}");
                    EditorGUI.EndDisabledGroup();
                    return currentValue;
                }
            }

            // Get current array/list size
            int size = currentValue is Array array1 ? array1.Length : ((IList)currentValue).Count;
            size = isReadOnly ? size : EditorGUILayout.IntField($"{label} Size", size);

            // Resize array or list
            if (!isReadOnly)
            {
                if (currentValue is Array arrayValue)
                {
                    Array newArray = Array.CreateInstance(elementType, size);
                    for (int i = 0; i < Math.Min(size, arrayValue.Length); i++)
                    {
                        newArray.SetValue(arrayValue.GetValue(i), i);
                    }
                    currentValue = newArray;
                }
                else // List<T>
                {
                    IList listValue = (IList)currentValue;
                    Type listType = typeof(List<>).MakeGenericType(elementType);
                    currentValue = Activator.CreateInstance(listType);
                    IList newList = (IList)currentValue;
                    for (int i = 0; i < Math.Min(size, listValue.Count); i++)
                    {
                        newList.Add(listValue[i]);
                    }
                    for (int i = listValue.Count; i < size; i++)
                    {
                        newList.Add(GetDefaultValue(elementType));
                    }
                }
            }

            // Draw elements
            EditorGUI.indentLevel++;
            for (int i = 0; i < size; i++)
            {
                object elementValue = currentValue is Array array2 ? array2.GetValue(i) : ((IList)currentValue)[i];
                object newValue = DrawSingleField($"{label} Element {i}", elementType, elementValue, isReadOnly);
                if (!isReadOnly)
                {
                    if (currentValue is Array array)
                    {
                        array.SetValue(newValue, i);
                    }
                    else
                    {
                        ((IList)currentValue)[i] = newValue;
                    }
                }
            }
            EditorGUI.indentLevel--;
            return currentValue;
        }

        private object GetDefaultValue(Type type)
        {
            if (type == typeof(int)) return 0;
            if (type == typeof(float)) return 0f;
            if (type == typeof(string)) return "";
            if (type == typeof(bool)) return false;
            if (type == typeof(Vector2)) return Vector2.zero;
            if (type == typeof(Vector3)) return Vector3.zero;
            if (type == typeof(Vector4)) return Vector4.zero;
            if (type == typeof(Color)) return Color.white;
            if (type == typeof(Rect)) return Rect.zero;
            if (type == typeof(Bounds)) return new Bounds();
            if (type == typeof(Quaternion)) return Quaternion.identity;
            if (typeof(UnityEngine.Object).IsAssignableFrom(type)) return null;
            return null;
        }

        private void InvokeMethod(MethodInfo method, UnityBehaviour behaviour, ParameterInfo[] parameters)
        {
            if (parameters.Length == 0)
            {
                method.Invoke(behaviour, null);
            }
            else if (methodParameters.ContainsKey(method.Name))
            {
                method.Invoke(behaviour, methodParameters[method.Name]);
            }
            else
            {
                Debug.LogError($"Parameters for method {method.Name} not provided.");
            }
        }
    }
}