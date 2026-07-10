using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace LegendaryTools.ViewBinding.Editor
{
    public static class BindingEndpointInspectorDrawer
    {
        private const int MemberTreeDepth = 8;

        public static void Draw(
            SerializedObject owner,
            SerializedProperty endpointProperty,
            bool requireReadable,
            bool requireWritable,
            string emptyLabel,
            Type compatibleType = null)
        {
            SerializedProperty instanceProperty = endpointProperty.FindPropertyRelative("instance");
            SerializedProperty memberPathProperty = endpointProperty.FindPropertyRelative("memberPath");

            DrawInstanceReference(owner, instanceProperty);
            EditorGUILayout.Space(2f);
            DrawMemberSelector(
                owner,
                instanceProperty,
                memberPathProperty,
                requireReadable,
                requireWritable,
                emptyLabel,
                compatibleType);
        }

        private static void DrawInstanceReference(
            SerializedObject owner,
            SerializedProperty instanceProperty)
        {
            SerializedProperty kindProperty = instanceProperty.FindPropertyRelative("kind");
            SerializedProperty objectProperty = instanceProperty.FindPropertyRelative("objectReference");
            SerializedProperty staticTypeProperty = instanceProperty.FindPropertyRelative("staticTypeName");
            SerializedProperty providerProperty = instanceProperty.FindPropertyRelative("providerReference");

            EditorGUILayout.PropertyField(kindProperty, new GUIContent("Instance Kind"));
            BindingInstanceKind kind = (BindingInstanceKind)kindProperty.enumValueIndex;

            switch (kind)
            {
                case BindingInstanceKind.UnityObject:
                    EditorGUILayout.PropertyField(objectProperty, new GUIContent("Instance"));
                    break;

                case BindingInstanceKind.StaticType:
                    DrawStaticTypeSelector(owner, staticTypeProperty);
                    break;

                case BindingInstanceKind.Provider:
                    EditorGUILayout.PropertyField(providerProperty, new GUIContent("Provider"));
                    if (providerProperty.objectReferenceValue != null &&
                        !(providerProperty.objectReferenceValue is IBindingInstanceProvider))
                    {
                        EditorGUILayout.HelpBox(
                            "The selected object does not implement IBindingInstanceProvider.",
                            MessageType.Error);
                    }
                    break;
            }
        }

        private static void DrawStaticTypeSelector(
            SerializedObject owner,
            SerializedProperty staticTypeProperty)
        {
            Type currentType = DefaultBindingInstanceResolver.FindType(staticTypeProperty.stringValue);
            string label = currentType == null
                ? "Select Static Type"
                : currentType.FullName;

            Rect buttonRect = EditorGUILayout.GetControlRect();
            buttonRect = EditorGUI.PrefixLabel(buttonRect, new GUIContent("Static Type"));

            if (!GUI.Button(buttonRect, label, BindingInspectorStyles.PathButtonStyle))
            {
                return;
            }

            string propertyPath = staticTypeProperty.propertyPath;
            PopupWindow.Show(
                buttonRect,
                new StaticTypePickerWindow(type =>
                {
                    owner.Update();
                    SerializedProperty property = owner.FindProperty(propertyPath);
                    if (property != null)
                    {
                        property.stringValue = type.AssemblyQualifiedName;
                        owner.ApplyModifiedProperties();
                    }
                }));
        }

        private static void DrawMemberSelector(
            SerializedObject owner,
            SerializedProperty instanceProperty,
            SerializedProperty memberPathProperty,
            bool requireReadable,
            bool requireWritable,
            string emptyLabel,
            Type compatibleType)
        {
            string buttonLabel = string.IsNullOrWhiteSpace(memberPathProperty.stringValue)
                ? emptyLabel
                : ComponentBindingPath.GetDisplayPath(memberPathProperty.stringValue);

            Rect row = EditorGUILayout.GetControlRect();
            Rect buttonRect = EditorGUI.PrefixLabel(row, new GUIContent("Member"));

            bool instanceResolved = BindingEditorResolver.TryResolveInstance(
                instanceProperty,
                out BindingInstanceHandle handle,
                out string instanceError);

            using (new EditorGUI.DisabledScope(!instanceResolved))
            {
                if (GUI.Button(buttonRect, buttonLabel, BindingInspectorStyles.PathButtonStyle))
                {
                    IReadOnlyList<BindingMemberDescriptor> members =
                        BindingBackendRegistry.MemberBackend.GetMembers(handle, MemberTreeDepth);

                    string propertyPath = memberPathProperty.propertyPath;
                    Func<string, int, IReadOnlyList<BindingMemberDescriptor>> searchProvider = null;

                    if (BindingBackendRegistry.MemberBackend is IBindingMemberSearchBackend searchBackend)
                    {
                        searchProvider = (query, maxResults) => searchBackend.SearchMembers(
                            handle,
                            MemberTreeDepth,
                            query,
                            maxResults);
                    }

                    PopupWindow.Show(
                        buttonRect,
                        new BindingMemberPickerWindow(
                            members,
                            requireReadable,
                            requireWritable,
                            descriptor =>
                            {
                                owner.Update();
                                SerializedProperty property = owner.FindProperty(propertyPath);
                                if (property != null)
                                {
                                    property.stringValue = descriptor.Path;
                                    owner.ApplyModifiedProperties();
                                }
                            },
                            compatibleType,
                            searchProvider));
                }
            }

            if (!instanceResolved && HasAnyInstanceConfiguration(instanceProperty))
            {
                EditorGUILayout.HelpBox(instanceError, MessageType.Error);
            }
            else if (instanceResolved && !string.IsNullOrWhiteSpace(memberPathProperty.stringValue))
            {
                DrawSelectedMemberType(memberPathProperty);
            }
        }

        private static void DrawSelectedMemberType(SerializedProperty memberPathProperty)
        {
            SerializedProperty endpointProperty = memberPathProperty.serializedObject.FindProperty(
                memberPathProperty.propertyPath.Substring(
                    0,
                    memberPathProperty.propertyPath.Length - ".memberPath".Length));

            if (BindingEditorResolver.TryGetMemberMetadata(
                    endpointProperty,
                    out BindingMemberMetadata metadata,
                    out string error))
            {
                string access = metadata.CanRead && metadata.CanWrite
                    ? "Read / Write"
                    : metadata.CanRead
                        ? "Read Only"
                        : metadata.CanWrite
                            ? "Write Only"
                            : "Unavailable";

                EditorGUILayout.LabelField(
                    string.Empty,
                    $"{GetFriendlyTypeName(metadata.ValueType)}  •  {access}",
                    EditorStyles.miniLabel);
            }
            else
            {
                EditorGUILayout.HelpBox(error, MessageType.Warning);
            }
        }

        private static bool HasAnyInstanceConfiguration(SerializedProperty instanceProperty)
        {
            return instanceProperty.FindPropertyRelative("objectReference").objectReferenceValue != null ||
                   !string.IsNullOrWhiteSpace(instanceProperty.FindPropertyRelative("staticTypeName").stringValue) ||
                   instanceProperty.FindPropertyRelative("providerReference").objectReferenceValue != null;
        }

        private static string GetFriendlyTypeName(Type type)
        {
            return type == null ? "Unknown" : type.Name;
        }
    }
}
