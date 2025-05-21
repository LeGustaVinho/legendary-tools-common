using UnityEngine;
using UnityEditor;
using System;
using System.Reflection;
using System.Collections.Generic;

namespace LegendaryTools.Editor
{
    [CustomEditor(typeof(FieldSync))]
    public class FieldSyncEditor : UnityEditor.Editor
    {
        /// <summary>
        /// Draws the custom inspector GUI.
        /// </summary>
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            FieldSync fieldSync = (FieldSync)target;

            // Display Origin and Destination objects (can be Component or GameObject).
            EditorGUILayout.PropertyField(serializedObject.FindProperty("Origin"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("Destination"));

            // Properties storing the names of the selected members.
            SerializedProperty originMemberProp = serializedObject.FindProperty("originMember");
            SerializedProperty destinationMemberProp = serializedObject.FindProperty("destinationMember");
            string selectedOriginMember = originMemberProp.stringValue;
            string selectedDestinationMember = destinationMemberProp.stringValue;

            // If the Origin object is assigned, build the dropdown for its members.
            if (fieldSync.Origin != null)
            {
                List<MemberInfo> originMembers = GetSerializableMembers(fieldSync.Origin);
                List<string> originMemberNames = new List<string>();
                int selectedIndex = 0;

                for (int i = 0; i < originMembers.Count; i++)
                {
                    originMemberNames.Add(originMembers[i].Name + " (" + GetMemberType(originMembers[i]).Name + ")");
                    if (originMembers[i].Name == selectedOriginMember)
                        selectedIndex = i;
                }

                int newSelectedIndex =
                    EditorGUILayout.Popup("Origin Member", selectedIndex, originMemberNames.ToArray());
                if (newSelectedIndex < originMembers.Count)
                {
                    selectedOriginMember = originMembers[newSelectedIndex].Name;
                    originMemberProp.stringValue = selectedOriginMember;
                }

                // Get the type of the selected member to filter Destination members.
                Type originMemberType = GetMemberType(originMembers[newSelectedIndex]);

                if (fieldSync.Destination != null && originMemberType != null)
                {
                    List<MemberInfo> destMembers = GetSerializableMembers(fieldSync.Destination, originMemberType);
                    List<string> destMemberNames = new List<string>();
                    int selectedDestIndex = 0;
                    for (int i = 0; i < destMembers.Count; i++)
                    {
                        destMemberNames.Add(destMembers[i].Name + " (" + GetMemberType(destMembers[i]).Name + ")");
                        if (destMembers[i].Name == selectedDestinationMember)
                            selectedDestIndex = i;
                    }

                    if (destMembers.Count > 0)
                    {
                        int newDestIndex = EditorGUILayout.Popup("Destination Member", selectedDestIndex,
                            destMemberNames.ToArray());
                        if (newDestIndex < destMembers.Count)
                        {
                            selectedDestinationMember = destMembers[newDestIndex].Name;
                            destinationMemberProp.stringValue = selectedDestinationMember;
                        }
                    }
                    else
                    {
                        EditorGUILayout.HelpBox(
                            "No compatible member found in Destination for type: " + originMemberType.Name,
                            MessageType.Warning);
                    }
                }
            }
            else
            {
                EditorGUILayout.HelpBox("Please assign an Origin object.", MessageType.Info);
            }

            // Display the operation mode if both members are selected.
            if (fieldSync.Origin != null && fieldSync.Destination != null &&
                !string.IsNullOrEmpty(selectedOriginMember) && !string.IsNullOrEmpty(selectedDestinationMember))
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("mode"), new GUIContent("Operation Mode"));
            }

            // Display the Inverse field if both selected members are of Boolean type.
            if (fieldSync.Origin != null && fieldSync.Destination != null &&
                !string.IsNullOrEmpty(selectedOriginMember) && !string.IsNullOrEmpty(selectedDestinationMember))
            {
                MemberInfo originInfo = GetMemberInfo(fieldSync.Origin, selectedOriginMember);
                MemberInfo destinationInfo = GetMemberInfo(fieldSync.Destination, selectedDestinationMember);
                if (originInfo != null && destinationInfo != null &&
                    GetMemberType(originInfo) == typeof(bool) && GetMemberType(destinationInfo) == typeof(bool))
                {
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("Inverse"), new GUIContent("Inverse"));
                }
            }

            serializedObject.ApplyModifiedProperties();
        }

        /// <summary>
        /// Returns all public (or serializable) members (fields and properties) of the object,
        /// including base classes.
        /// 
        /// For GameObject, all public fields and properties are returned (e.g. activeInHierarchy, activeSelf).
        /// For other objects (typically Components) the members are filtered to include public or [SerializeField] fields
        /// and public properties with getter/setter.
        /// 
        /// If filterType is specified, only members of that type are returned.
        /// </summary>
        /// <param name="obj">The object to inspect (Component or GameObject).</param>
        /// <param name="filterType">Optional type filter.</param>
        /// <returns>A list of MemberInfo objects.</returns>
        private List<MemberInfo> GetSerializableMembers(UnityEngine.Object obj, Type filterType = null)
        {
            List<MemberInfo> members = new List<MemberInfo>();
            Type current = obj.GetType();

            // If the object is a GameObject, list all public members.
            if (obj is GameObject)
            {
                while (current != null)
                {
                    BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly;

                    // Add public fields.
                    FieldInfo[] fields = current.GetFields(flags);
                    foreach (FieldInfo field in fields)
                    {
                        if (filterType == null || field.FieldType == filterType)
                            members.Add(field);
                    }

                    // Add public properties (include read-only ones).
                    PropertyInfo[] properties = current.GetProperties(flags);
                    foreach (PropertyInfo property in properties)
                    {
                        // For GameObject, we list all public properties regardless of setter.
                        if (filterType == null || property.PropertyType == filterType)
                            members.Add(property);
                    }

                    current = current.BaseType;
                }
            }
            else // For Components or other objects, use the prior filtering.
            {
                while (current != null)
                {
                    BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic |
                                         BindingFlags.DeclaredOnly;

                    // Add fields: public or marked with [SerializeField].
                    FieldInfo[] fields = current.GetFields(flags);
                    foreach (FieldInfo field in fields)
                    {
                        if (field.IsPublic || Attribute.IsDefined(field, typeof(SerializeField)))
                        {
                            if (filterType == null || field.FieldType == filterType)
                                members.Add(field);
                        }
                    }

                    // Add properties: only public ones with getter and setter.
                    PropertyInfo[] properties =
                        current.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
                    foreach (PropertyInfo property in properties)
                    {
                        if (property.CanRead && property.CanWrite &&
                            property.GetMethod != null && property.SetMethod != null &&
                            property.GetMethod.IsPublic && property.SetMethod.IsPublic)
                        {
                            if (filterType == null || property.PropertyType == filterType)
                                members.Add(property);
                        }
                    }

                    current = current.BaseType;
                }
            }

            return members;
        }

        /// <summary>
        /// Retrieves the member (field or property) from the object by name, traversing its class hierarchy.
        /// </summary>
        /// <param name="obj">The object to search (Component or GameObject).</param>
        /// <param name="memberName">The name of the member.</param>
        /// <returns>The MemberInfo if found; otherwise, null.</returns>
        private MemberInfo GetMemberInfo(UnityEngine.Object obj, string memberName)
        {
            Type current = obj.GetType();
            while (current != null)
            {
                FieldInfo field = current.GetField(memberName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (field != null)
                    return field;
                PropertyInfo prop = current.GetProperty(memberName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (prop != null)
                    return prop;
                current = current.BaseType;
            }

            return null;
        }

        /// <summary>
        /// Gets the type of the specified member (field or property).
        /// </summary>
        /// <param name="member">The MemberInfo object.</param>
        /// <returns>The type of the member.</returns>
        private Type GetMemberType(MemberInfo member)
        {
            if (member is FieldInfo field)
                return field.FieldType;
            else if (member is PropertyInfo prop)
                return prop.PropertyType;
            return null;
        }
    }
}