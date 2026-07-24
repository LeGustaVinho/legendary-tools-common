using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace LegendaryTools.ViewBinding.Editor
{
    public static class BindingEndpointInspectorUtility
    {
        private const int MemberTreeDepth = 8;

        public static void DrawEndpoint(
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

        public static void ResetEndpoint(SerializedProperty endpointProperty)
        {
            endpointProperty.FindPropertyRelative("memberPath").stringValue = string.Empty;
            ResetInstanceReference(endpointProperty.FindPropertyRelative("instance"));
        }

        public static void ResetInstanceReference(SerializedProperty instanceProperty)
        {
            instanceProperty.FindPropertyRelative("kind").enumValueIndex = (int)BindingInstanceKind.UnityObject;
            instanceProperty.FindPropertyRelative("objectReference").objectReferenceValue = null;
            instanceProperty.FindPropertyRelative("staticTypeName").stringValue = string.Empty;
            instanceProperty.FindPropertyRelative("providerReference").objectReferenceValue = null;
            instanceProperty.FindPropertyRelative("contextName").stringValue = BindingContextConstants.Default;
            instanceProperty.FindPropertyRelative("contextTypeName").stringValue = string.Empty;
        }

        private static void DrawInstanceReference(
            SerializedObject owner,
            SerializedProperty instanceProperty)
        {
            SerializedProperty kindProperty = instanceProperty.FindPropertyRelative("kind");
            SerializedProperty objectProperty = instanceProperty.FindPropertyRelative("objectReference");
            SerializedProperty staticTypeProperty = instanceProperty.FindPropertyRelative("staticTypeName");
            SerializedProperty providerProperty = instanceProperty.FindPropertyRelative("providerReference");
            SerializedProperty contextNameProperty = instanceProperty.FindPropertyRelative("contextName");
            SerializedProperty contextTypeProperty = instanceProperty.FindPropertyRelative("contextTypeName");

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

                case BindingInstanceKind.Context:
                    EditorGUILayout.PropertyField(contextNameProperty, new GUIContent("Context"));
                    DrawTypeSelector(owner, contextTypeProperty, "Declared Type", "Select Context Type");
                    EditorGUILayout.LabelField(
                        "Use $Source or $Target inside profiles, or any named hierarchical context.",
                        EditorStyles.wordWrappedMiniLabel);
                    break;
            }
        }

        private static void DrawStaticTypeSelector(
            SerializedObject owner,
            SerializedProperty staticTypeProperty)
        {
            DrawTypeSelector(owner, staticTypeProperty, "Static Type", "Select Static Type");
        }

        private static void DrawTypeSelector(
            SerializedObject owner,
            SerializedProperty typeProperty,
            string fieldLabel,
            string emptyLabel)
        {
            Type currentType = DefaultBindingInstanceResolver.FindType(typeProperty.stringValue);
            string label = currentType == null ? emptyLabel : currentType.FullName;

            Rect buttonRect = EditorGUILayout.GetControlRect();
            buttonRect = EditorGUI.PrefixLabel(buttonRect, new GUIContent(fieldLabel));

            if (!GUI.Button(buttonRect, label, BindingInspectorStyles.PathButtonStyle))
            {
                return;
            }

            string propertyPath = typeProperty.propertyPath;
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
            BindingInstanceKind kind = (BindingInstanceKind)instanceProperty
                .FindPropertyRelative("kind")
                .enumValueIndex;

            switch (kind)
            {
                case BindingInstanceKind.UnityObject:
                    return instanceProperty.FindPropertyRelative("objectReference").objectReferenceValue != null;

                case BindingInstanceKind.StaticType:
                    return !string.IsNullOrWhiteSpace(
                        instanceProperty.FindPropertyRelative("staticTypeName").stringValue);

                case BindingInstanceKind.Provider:
                    return instanceProperty.FindPropertyRelative("providerReference").objectReferenceValue != null;

                case BindingInstanceKind.Context:
                    return !string.IsNullOrWhiteSpace(
                               instanceProperty.FindPropertyRelative("contextName").stringValue) ||
                           !string.IsNullOrWhiteSpace(
                               instanceProperty.FindPropertyRelative("contextTypeName").stringValue);

                default:
                    return false;
            }
        }

        private static string GetFriendlyTypeName(Type type)
        {
            if (type == null)
            {
                return "Unknown";
            }

            if (!type.IsGenericType)
            {
                return type.Name;
            }

            string name = type.Name;
            int tickIndex = name.IndexOf('`');
            if (tickIndex >= 0)
            {
                name = name.Substring(0, tickIndex);
            }

            Type[] arguments = type.GetGenericArguments();
            var names = new string[arguments.Length];
            for (int i = 0; i < arguments.Length; i++)
            {
                names[i] = GetFriendlyTypeName(arguments[i]);
            }

            return $"{name}<{string.Join(", ", names)}>";
        }
    }
}
