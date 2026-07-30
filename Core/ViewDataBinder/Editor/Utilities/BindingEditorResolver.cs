using System;
using UnityEditor;
using UnityEngine;

namespace LegendaryTools.ViewBinding.Editor
{
    public static class BindingEditorResolver
    {
        public static bool TryResolveInstance(
            SerializedProperty instanceProperty,
            out BindingInstanceHandle handle,
            out string error)
        {
            handle = default;
            error = string.Empty;

            if (instanceProperty == null)
            {
                error = "Instance property is null.";
                return false;
            }

            SerializedProperty kindProperty = instanceProperty.FindPropertyRelative("kind");
            BindingInstanceKind kind = (BindingInstanceKind)kindProperty.enumValueIndex;

            switch (kind)
            {
                case BindingInstanceKind.UnityObject:
                {
                    UnityEngine.Object value = instanceProperty
                        .FindPropertyRelative("objectReference")
                        .objectReferenceValue;

                    if (value == null)
                    {
                        error = "No Unity Object is assigned.";
                        return false;
                    }

                    handle = new BindingInstanceHandle(value, value.GetType(), false);
                    return true;
                }

                case BindingInstanceKind.StaticType:
                {
                    string typeName = instanceProperty
                        .FindPropertyRelative("staticTypeName")
                        .stringValue;
                    Type type = DefaultBindingInstanceResolver.FindType(typeName);

                    if (type == null)
                    {
                        error = "The static type could not be resolved.";
                        return false;
                    }

                    handle = new BindingInstanceHandle(null, type, true);
                    return true;
                }

                case BindingInstanceKind.Provider:
                {
                    UnityEngine.Object value = instanceProperty
                        .FindPropertyRelative("providerReference")
                        .objectReferenceValue;

                    if (!(value is IBindingInstanceProvider provider))
                    {
                        error = "The Provider must implement IBindingInstanceProvider.";
                        return false;
                    }

                    try
                    {
                        Type type = provider.GetBindingInstanceType();
                        if (type == null)
                        {
                            object instance = provider.GetBindingInstance();
                            type = instance?.GetType();
                        }

                        if (type == null)
                        {
                            error = "The Provider returned neither an instance nor a declared type.";
                            return false;
                        }

                        handle = new BindingInstanceHandle(null, type, false);
                        return true;
                    }
                    catch (Exception exception)
                    {
                        error = $"Provider resolution failed: {exception.Message}";
                        return false;
                    }
                }

                case BindingInstanceKind.Context:
                {
                    string contextName = instanceProperty
                        .FindPropertyRelative("contextName")
                        .stringValue;
                    string contextTypeName = instanceProperty
                        .FindPropertyRelative("contextTypeName")
                        .stringValue;

                    if (instanceProperty.serializedObject.targetObject is Component owner)
                    {
                        Transform current = owner.transform;
                        while (current != null)
                        {
                            if (current.TryGetComponent(out BindingDataContext dataContext) &&
                                dataContext.TryResolveContext(contextName, out handle, out error))
                            {
                                return true;
                            }

                            current = current.parent;
                        }
                    }

                    Type declaredType = DefaultBindingInstanceResolver.FindType(contextTypeName);
                    if (declaredType == null)
                    {
                        error = $"Context '{BindingDataContext.NormalizeName(contextName)}' is not currently resolved and has no declared type.";
                        return false;
                    }

                    handle = new BindingInstanceHandle(null, declaredType, false);
                    error = string.Empty;
                    return true;
                }

                case BindingInstanceKind.Runtime:
                {
                    string typeName = instanceProperty
                        .FindPropertyRelative("runtimeTypeName")
                        .stringValue;
                    Type type = DefaultBindingInstanceResolver.FindType(typeName);
                    if (type == null)
                    {
                        error = "Select a declared Runtime type to configure members in the Inspector.";
                        return false;
                    }

                    handle = new BindingInstanceHandle(null, type, false);
                    return true;
                }

                default:
                    error = $"Unsupported instance kind: {kind}.";
                    return false;
            }
        }

        public static bool TryGetMemberMetadata(
            SerializedProperty endpointProperty,
            out BindingMemberMetadata metadata,
            out string error)
        {
            metadata = default;

            if (endpointProperty == null)
            {
                error = "Endpoint property is null.";
                return false;
            }

            SerializedProperty instanceProperty = endpointProperty.FindPropertyRelative("instance");
            SerializedProperty memberPathProperty = endpointProperty.FindPropertyRelative("memberPath");

            if (!TryResolveInstance(instanceProperty, out BindingInstanceHandle handle, out error))
            {
                return false;
            }

            string memberPath = memberPathProperty.stringValue;
            if (string.IsNullOrWhiteSpace(memberPath))
            {
                error = "No member is selected.";
                return false;
            }

            return BindingBackendRegistry.MemberBackend.TryGetMetadata(
                handle,
                memberPath,
                out metadata,
                out error);
        }

    }
}
