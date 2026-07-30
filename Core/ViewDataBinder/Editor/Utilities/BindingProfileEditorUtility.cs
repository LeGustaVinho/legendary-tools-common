using System;
using UnityEditor;
using UnityEngine;

namespace LegendaryTools.ViewBinding.Editor
{
    internal static class BindingProfileEditorUtility
    {
        public static bool TryCaptureSharedRoots(
            ViewDataBinder binder,
            out RootSnapshot sourceRoot,
            out RootSnapshot targetRoot,
            out string error)
        {
            var serializedBinder = new SerializedObject(binder);
            SerializedProperty bindings = serializedBinder.FindProperty("bindings");
            sourceRoot = default;
            targetRoot = default;
            bool hasSource = false;
            bool hasTarget = false;

            for (int bindingIndex = 0; bindingIndex < bindings.arraySize; bindingIndex++)
            {
                SerializedProperty binding = bindings.GetArrayElementAtIndex(bindingIndex);
                SerializedProperty sources = binding.FindPropertyRelative("sources");
                for (int sourceIndex = 0; sourceIndex < sources.arraySize; sourceIndex++)
                {
                    SerializedProperty instance = sources
                        .GetArrayElementAtIndex(sourceIndex)
                        .FindPropertyRelative("endpoint")
                        .FindPropertyRelative("instance");
                    if (!TryCaptureRoot(instance, out RootSnapshot current, out error))
                    {
                        return false;
                    }

                    if (!hasSource)
                    {
                        sourceRoot = current;
                        hasSource = true;
                    }
                    else if (!sourceRoot.Equals(current))
                    {
                        error = "Local bindings use more than one Source root. Create the profile manually and use named contexts for the additional roots.";
                        return false;
                    }
                }

                SerializedProperty targetInstance = binding
                    .FindPropertyRelative("target")
                    .FindPropertyRelative("instance");
                if (!TryCaptureRoot(targetInstance, out RootSnapshot currentTarget, out error))
                {
                    return false;
                }

                if (!hasTarget)
                {
                    targetRoot = currentTarget;
                    hasTarget = true;
                }
                else if (!targetRoot.Equals(currentTarget))
                {
                    error = "Local bindings use more than one Target root. Create the profile manually and use named contexts for the additional roots.";
                    return false;
                }
            }

            if (!hasSource || !hasTarget)
            {
                error = "At least one complete local binding is required to create a parameterized profile.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        public static void CopyLocalBindingsToProfile(
            ViewDataBinder binder,
            ViewDataBindingProfile profile,
            RootSnapshot sourceRoot,
            RootSnapshot targetRoot)
        {
            string json = EditorJsonUtility.ToJson(binder);
            EditorJsonUtility.FromJsonOverwrite(json, profile);

            var serializedProfile = new SerializedObject(profile);
            SerializedProperty bindings = serializedProfile.FindProperty("bindings");
            for (int bindingIndex = 0; bindingIndex < bindings.arraySize; bindingIndex++)
            {
                SerializedProperty binding = bindings.GetArrayElementAtIndex(bindingIndex);
                SerializedProperty id = binding.FindPropertyRelative("id");
                if (string.IsNullOrWhiteSpace(id.stringValue))
                {
                    id.stringValue = Guid.NewGuid().ToString("N");
                }

                SerializedProperty sources = binding.FindPropertyRelative("sources");
                for (int sourceIndex = 0; sourceIndex < sources.arraySize; sourceIndex++)
                {
                    SerializedProperty instance = sources
                        .GetArrayElementAtIndex(sourceIndex)
                        .FindPropertyRelative("endpoint")
                        .FindPropertyRelative("instance");
                    ConfigureContextReference(
                        instance,
                        BindingContextConstants.ProfileSource,
                        sourceRoot.DeclaredTypeName);
                }

                SerializedProperty targetInstance = binding
                    .FindPropertyRelative("target")
                    .FindPropertyRelative("instance");
                ConfigureContextReference(
                    targetInstance,
                    BindingContextConstants.ProfileTarget,
                    targetRoot.DeclaredTypeName);
            }

            serializedProfile.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(profile);
        }

        public static void AddDisabledProfileReference(
            ViewDataBinder binder,
            ViewDataBindingProfile profile,
            RootSnapshot sourceRoot,
            RootSnapshot targetRoot)
        {
            var serializedBinder = new SerializedObject(binder);
            SerializedProperty profiles = serializedBinder.FindProperty("profiles");
            int index = profiles.arraySize;
            profiles.arraySize++;

            SerializedProperty profileReference = profiles.GetArrayElementAtIndex(index);
            profileReference.FindPropertyRelative("id").stringValue = Guid.NewGuid().ToString("N");
            profileReference.FindPropertyRelative("enabled").boolValue = false;
            profileReference.FindPropertyRelative("profile").objectReferenceValue = profile;
            ApplyContextValue(profileReference.FindPropertyRelative("sourceRoot"), sourceRoot);
            ApplyContextValue(profileReference.FindPropertyRelative("targetRoot"), targetRoot);
            profileReference.FindPropertyRelative("namedContexts").arraySize = 0;
            serializedBinder.ApplyModifiedProperties();
        }

        private static bool TryCaptureRoot(
            SerializedProperty instance,
            out RootSnapshot snapshot,
            out string error)
        {
            BindingInstanceKind kind = (BindingInstanceKind)instance
                .FindPropertyRelative("kind")
                .enumValueIndex;
            if (kind == BindingInstanceKind.Context)
            {
                snapshot = default;
                error = "A local Context root is already indirect. Create the profile asset directly or replace it with a concrete root before using Create from Local.";
                return false;
            }

            UnityEngine.Object objectReference = instance
                .FindPropertyRelative("objectReference")
                .objectReferenceValue;
            UnityEngine.Object providerReference = instance
                .FindPropertyRelative("providerReference")
                .objectReferenceValue;
            string staticTypeName = instance
                .FindPropertyRelative("staticTypeName")
                .stringValue;
            string runtimeTypeName = instance
                .FindPropertyRelative("runtimeTypeName")
                .stringValue;

            string declaredTypeName;
            switch (kind)
            {
                case BindingInstanceKind.UnityObject:
                    if (objectReference == null)
                    {
                        snapshot = default;
                        error = "Every local UnityObject root must be assigned before creating a parameterized profile.";
                        return false;
                    }

                    declaredTypeName = objectReference.GetType().AssemblyQualifiedName;
                    break;

                case BindingInstanceKind.StaticType:
                    if (string.IsNullOrWhiteSpace(staticTypeName))
                    {
                        snapshot = default;
                        error = "Every local static root must have a selected type before creating a parameterized profile.";
                        return false;
                    }

                    declaredTypeName = staticTypeName;
                    break;

                case BindingInstanceKind.Provider:
                    if (!(providerReference is IBindingInstanceProvider provider))
                    {
                        snapshot = default;
                        error = "Every local Provider root must implement IBindingInstanceProvider before creating a parameterized profile.";
                        return false;
                    }

                    try
                    {
                        Type declaredType = provider.GetBindingInstanceType() ??
                                            provider.GetBindingInstance()?.GetType();
                        declaredTypeName = declaredType?.AssemblyQualifiedName;
                    }
                    catch (Exception exception)
                    {
                        snapshot = default;
                        error = $"Provider root inspection failed: {exception.Message}";
                        return false;
                    }

                    if (string.IsNullOrWhiteSpace(declaredTypeName))
                    {
                        snapshot = default;
                        error = "Every Provider root must expose a declared type before creating a parameterized profile.";
                        return false;
                    }
                    break;

                case BindingInstanceKind.Runtime:
                    if (string.IsNullOrWhiteSpace(runtimeTypeName))
                    {
                        snapshot = default;
                        error = "Every local Runtime root must have a declared type before creating a parameterized profile.";
                        return false;
                    }

                    declaredTypeName = runtimeTypeName;
                    break;

                default:
                    snapshot = default;
                    error = $"Unsupported root kind: {kind}.";
                    return false;
            }

            snapshot = new RootSnapshot(
                kind,
                objectReference,
                providerReference,
                staticTypeName,
                runtimeTypeName,
                declaredTypeName);
            error = string.Empty;
            return true;
        }

        private static void ConfigureContextReference(
            SerializedProperty instance,
            string contextName,
            string contextTypeName)
        {
            instance.FindPropertyRelative("kind").enumValueIndex = (int)BindingInstanceKind.Context;
            instance.FindPropertyRelative("objectReference").objectReferenceValue = null;
            instance.FindPropertyRelative("staticTypeName").stringValue = string.Empty;
            instance.FindPropertyRelative("providerReference").objectReferenceValue = null;
            instance.FindPropertyRelative("contextName").stringValue = contextName;
            instance.FindPropertyRelative("contextTypeName").stringValue = contextTypeName ?? string.Empty;
            instance.FindPropertyRelative("runtimeTypeName").stringValue = string.Empty;
        }

        private static void ApplyContextValue(
            SerializedProperty contextValue,
            RootSnapshot snapshot)
        {
            contextValue.FindPropertyRelative("objectReference").objectReferenceValue = null;
            contextValue.FindPropertyRelative("providerReference").objectReferenceValue = null;
            contextValue.FindPropertyRelative("staticTypeName").stringValue = string.Empty;
            contextValue.FindPropertyRelative("declaredTypeName").stringValue =
                snapshot.DeclaredTypeName ?? string.Empty;

            switch (snapshot.Kind)
            {
                case BindingInstanceKind.UnityObject:
                    contextValue.FindPropertyRelative("kind").enumValueIndex =
                        (int)BindingContextValueKind.UnityObject;
                    contextValue.FindPropertyRelative("objectReference").objectReferenceValue =
                        snapshot.ObjectReference;
                    break;

                case BindingInstanceKind.Provider:
                    contextValue.FindPropertyRelative("kind").enumValueIndex =
                        (int)BindingContextValueKind.Provider;
                    contextValue.FindPropertyRelative("providerReference").objectReferenceValue =
                        snapshot.ProviderReference;
                    break;

                case BindingInstanceKind.StaticType:
                    contextValue.FindPropertyRelative("kind").enumValueIndex =
                        (int)BindingContextValueKind.StaticType;
                    contextValue.FindPropertyRelative("staticTypeName").stringValue =
                        snapshot.StaticTypeName;
                    break;

                case BindingInstanceKind.Runtime:
                    contextValue.FindPropertyRelative("kind").enumValueIndex =
                        (int)BindingContextValueKind.UnityObject;
                    break;
            }
        }

        internal readonly struct RootSnapshot : IEquatable<RootSnapshot>
        {
            public RootSnapshot(
                BindingInstanceKind kind,
                UnityEngine.Object objectReference,
                UnityEngine.Object providerReference,
                string staticTypeName,
                string runtimeTypeName,
                string declaredTypeName)
            {
                Kind = kind;
                ObjectReference = objectReference;
                ProviderReference = providerReference;
                StaticTypeName = staticTypeName;
                RuntimeTypeName = runtimeTypeName;
                DeclaredTypeName = declaredTypeName;
            }

            public BindingInstanceKind Kind { get; }

            public UnityEngine.Object ObjectReference { get; }

            public UnityEngine.Object ProviderReference { get; }

            public string StaticTypeName { get; }

            public string RuntimeTypeName { get; }

            public string DeclaredTypeName { get; }

            public bool Equals(RootSnapshot other)
            {
                return Kind == other.Kind &&
                       ObjectReference == other.ObjectReference &&
                       ProviderReference == other.ProviderReference &&
                       string.Equals(StaticTypeName, other.StaticTypeName, StringComparison.Ordinal) &&
                       string.Equals(RuntimeTypeName, other.RuntimeTypeName, StringComparison.Ordinal);
            }

            public override bool Equals(object obj)
            {
                return obj is RootSnapshot other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hashCode = (int)Kind;
                    hashCode = (hashCode * 397) ^ (ObjectReference != null ? ObjectReference.GetHashCode() : 0);
                    hashCode = (hashCode * 397) ^ (ProviderReference != null ? ProviderReference.GetHashCode() : 0);
                    hashCode = (hashCode * 397) ^ (StaticTypeName != null ? StaticTypeName.GetHashCode() : 0);
                    hashCode = (hashCode * 397) ^ (RuntimeTypeName != null ? RuntimeTypeName.GetHashCode() : 0);
                    return hashCode;
                }
            }
        }
    }
}
