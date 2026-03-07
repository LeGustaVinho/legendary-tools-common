using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LegendaryTools.Editor
{
    public enum AssetUsageFinderContextualTargetKind
    {
        None = 0,
        GameObject = 1,
        Component = 2,
        SerializedProperty = 3
    }

    public enum AssetUsageFinderContextualReferenceKind
    {
        None = 0,
        GameObjectReference = 1,
        ComponentReference = 2,
        AttachedComponentReference = 3,
        UnityEventTarget = 4,
        SerializedPropertyPathMatch = 5,
        SerializedPropertyValueMatch = 6
    }

    public enum AssetUsageFinderPrefabProvenanceKind
    {
        None = 0,
        AssetObject = 1,
        SceneObject = 2,
        PrefabAssetDefinition = 3,
        PrefabVariantDefinition = 4,
        PrefabInstanceRoot = 5,
        PrefabInstanceChild = 6,
        NestedPrefabInstanceRoot = 7,
        AddedGameObjectOverride = 8,
        AddedComponentOverride = 9
    }

    public sealed class AssetUsageFinderContextualReferenceInfo
    {
        public AssetUsageFinderContextualReferenceKind Kind { get; }
        public string Label { get; }
        public string Details { get; }

        public AssetUsageFinderContextualReferenceInfo(
            AssetUsageFinderContextualReferenceKind kind,
            string label,
            string details)
        {
            Kind = kind;
            Label = label ?? string.Empty;
            Details = details ?? string.Empty;
        }
    }

    public sealed class AssetUsageFinderPrefabProvenanceInfo
    {
        public AssetUsageFinderPrefabProvenanceKind Kind { get; }
        public string Label { get; }
        public string Details { get; }
        public string SourceAssetPath { get; }

        public AssetUsageFinderPrefabProvenanceInfo(
            AssetUsageFinderPrefabProvenanceKind kind,
            string label,
            string details,
            string sourceAssetPath = "")
        {
            Kind = kind;
            Label = label ?? string.Empty;
            Details = details ?? string.Empty;
            SourceAssetPath = sourceAssetPath ?? string.Empty;
        }
    }

    public sealed class AssetUsageFinderObjectSnapshot
    {
        public Object DirectReference { get; }
        public string AssetGuid { get; }
        public long LocalFileId { get; }

        public bool HasPersistentIdentity =>
            !string.IsNullOrEmpty(AssetGuid) && LocalFileId != 0;

        private AssetUsageFinderObjectSnapshot(Object directReference, string assetGuid, long localFileId)
        {
            DirectReference = directReference;
            AssetGuid = assetGuid ?? string.Empty;
            LocalFileId = localFileId;
        }

        public static AssetUsageFinderObjectSnapshot Capture(Object target)
        {
            if (target == null)
                return null;

            TryGetPersistentIdentity(target, out string guid, out long localFileId);
            return new AssetUsageFinderObjectSnapshot(target, guid, localFileId);
        }

        public bool Matches(Object candidate)
        {
            if (candidate == null)
                return false;

            if (DirectReference != null && candidate == DirectReference)
                return true;

            return HasPersistentIdentity &&
                   TryGetPersistentIdentity(candidate, out string candidateGuid, out long candidateLocalFileId) &&
                   string.Equals(candidateGuid, AssetGuid, StringComparison.OrdinalIgnoreCase) &&
                   candidateLocalFileId == LocalFileId;
        }

        private static bool TryGetPersistentIdentity(Object obj, out string guid, out long localFileId)
        {
            guid = string.Empty;
            localFileId = 0;

            if (obj == null)
                return false;

            try
            {
                return AssetDatabase.TryGetGUIDAndLocalFileIdentifier(obj, out guid, out localFileId);
            }
            catch
            {
                guid = string.Empty;
                localFileId = 0;
                return false;
            }
        }
    }

    public sealed class AssetUsageFinderContextualRequest
    {
        public AssetUsageFinderContextualTargetKind TargetKind { get; }
        public Object TargetObject { get; }
        public AssetUsageFinderObjectSnapshot TargetSnapshot { get; }
        public IReadOnlyList<AssetUsageFinderObjectSnapshot> RelatedTargetSnapshots { get; }
        public string DisplayName { get; }
        public string Description { get; }
        public AssetUsageFinderSearchScope SuggestedScope { get; }

        public Type OwnerType { get; }
        public string PropertyPath { get; }
        public string PropertyDisplayName { get; }
        public Type ValueType { get; }
        public SerializedFieldValueBox Value { get; }
        public AssetUsageFinderObjectSnapshot ValueObjectSnapshot { get; }
        public bool MatchValue { get; }

        private AssetUsageFinderContextualRequest(
            AssetUsageFinderContextualTargetKind targetKind,
            Object targetObject,
            AssetUsageFinderObjectSnapshot targetSnapshot,
            IReadOnlyList<AssetUsageFinderObjectSnapshot> relatedTargetSnapshots,
            string displayName,
            string description,
            AssetUsageFinderSearchScope suggestedScope,
            Type ownerType = null,
            string propertyPath = "",
            string propertyDisplayName = "",
            Type valueType = null,
            SerializedFieldValueBox value = null,
            AssetUsageFinderObjectSnapshot valueObjectSnapshot = null,
            bool matchValue = false)
        {
            TargetKind = targetKind;
            TargetObject = targetObject;
            TargetSnapshot = targetSnapshot;
            RelatedTargetSnapshots = relatedTargetSnapshots ?? Array.Empty<AssetUsageFinderObjectSnapshot>();
            DisplayName = displayName ?? string.Empty;
            Description = description ?? string.Empty;
            SuggestedScope = suggestedScope;
            OwnerType = ownerType;
            PropertyPath = propertyPath ?? string.Empty;
            PropertyDisplayName = propertyDisplayName ?? string.Empty;
            ValueType = valueType;
            Value = value;
            ValueObjectSnapshot = valueObjectSnapshot;
            MatchValue = matchValue;
        }

        public static AssetUsageFinderContextualRequest CreateForGameObject(GameObject gameObject)
        {
            if (gameObject == null)
                return null;

            return new AssetUsageFinderContextualRequest(
                AssetUsageFinderContextualTargetKind.GameObject,
                gameObject,
                AssetUsageFinderObjectSnapshot.Capture(gameObject),
                CaptureRelatedComponentSnapshots(gameObject),
                $"{GetObjectContextLabel(gameObject)}",
                $"Searches serialized object references that point to GameObject '{gameObject.name}' or any of its components.",
                BuildSuggestedScopeForObjectTarget(gameObject));
        }

        public static AssetUsageFinderContextualRequest CreateForComponent(Component component)
        {
            if (component == null)
                return null;

            return new AssetUsageFinderContextualRequest(
                AssetUsageFinderContextualTargetKind.Component,
                component,
                AssetUsageFinderObjectSnapshot.Capture(component),
                Array.Empty<AssetUsageFinderObjectSnapshot>(),
                $"{component.GetType().Name} on {component.gameObject.name}",
                $"Searches serialized object references that point to component '{component.GetType().FullName}'.",
                BuildSuggestedScopeForObjectTarget(component));
        }

        public static AssetUsageFinderContextualRequest CreateForSerializedProperty(SerializedProperty property)
        {
            if (property == null || property.serializedObject == null)
                return null;

            Object owner = property.serializedObject.targetObject;
            if (owner == null)
                return null;

            Type ownerType = owner.GetType();
            string propertyPath = property.propertyPath ?? string.Empty;
            string propertyDisplayName = property.displayName ?? propertyPath;

            SerializedFieldValueBox value = BuildValueSnapshot(property, out Type valueType, out bool matchValue);
            AssetUsageFinderObjectSnapshot valueSnapshot =
                valueType != null && typeof(Object).IsAssignableFrom(valueType) && value != null
                    ? AssetUsageFinderObjectSnapshot.Capture(value.ObjectValue)
                    : null;

            string matchDescription = matchValue
                ? "same property path and current value"
                : "same property path";

            return new AssetUsageFinderContextualRequest(
                AssetUsageFinderContextualTargetKind.SerializedProperty,
                owner,
                AssetUsageFinderObjectSnapshot.Capture(owner),
                Array.Empty<AssetUsageFinderObjectSnapshot>(),
                $"{ownerType.Name}.{propertyDisplayName}",
                $"Searches '{ownerType.FullName}.{propertyPath}' usages with {matchDescription}.",
                BuildSuggestedScopeForSerializedProperty(owner, valueType, value),
                ownerType,
                propertyPath,
                propertyDisplayName,
                valueType,
                value,
                valueSnapshot,
                matchValue);
        }

        public bool MatchesTargetReference(Object candidate)
        {
            if (TargetSnapshot != null && TargetSnapshot.Matches(candidate))
                return true;

            if (TargetKind != AssetUsageFinderContextualTargetKind.GameObject || RelatedTargetSnapshots == null)
                return false;

            for (int i = 0; i < RelatedTargetSnapshots.Count; i++)
            {
                AssetUsageFinderObjectSnapshot snapshot = RelatedTargetSnapshots[i];
                if (snapshot != null && snapshot.Matches(candidate))
                    return true;
            }

            return false;
        }

        public bool MatchesSerializedPropertyValue(SerializedProperty property)
        {
            if (property == null)
                return false;

            if (!MatchValue)
                return true;

            Type requestedType = ValueType;
            SerializedFieldValueBox value = Value;

            if (requestedType == null || value == null)
                return false;

            try
            {
                if (typeof(Object).IsAssignableFrom(requestedType))
                    return ValueObjectSnapshot != null
                        ? ValueObjectSnapshot.Matches(property.objectReferenceValue)
                        : property.objectReferenceValue == null && value.ObjectValue == null;

                if (requestedType == typeof(Enum) || requestedType.IsEnum)
                {
                    string currentName = property.enumDisplayNames != null &&
                                         property.enumValueIndex >= 0 &&
                                         property.enumValueIndex < property.enumDisplayNames.Length
                        ? property.enumDisplayNames[property.enumValueIndex]
                        : property.enumValueIndex.ToString();
                    return string.Equals(currentName, value.EnumName ?? string.Empty, StringComparison.Ordinal);
                }

                if (requestedType == typeof(bool))
                    return property.boolValue == value.BoolValue;

                if (requestedType == typeof(string))
                    return string.Equals(property.stringValue ?? string.Empty, value.StringValue ?? string.Empty,
                        StringComparison.Ordinal);

                if (requestedType == typeof(int) || requestedType == typeof(short) || requestedType == typeof(byte))
                    return property.longValue == value.IntValue;

                if (requestedType == typeof(long) || requestedType == typeof(uint) || requestedType == typeof(ulong))
                    return property.longValue == value.LongValue;

                if (requestedType == typeof(float))
                    return Mathf.Approximately(property.floatValue, value.FloatValue);

                if (requestedType == typeof(double))
                    return Math.Abs(property.doubleValue - value.DoubleValue) <= double.Epsilon;

                if (requestedType == typeof(Vector2))
                    return property.vector2Value == value.Vector2Value;

                if (requestedType == typeof(Vector3))
                    return property.vector3Value == value.Vector3Value;

                if (requestedType == typeof(Vector4))
                    return property.vector4Value == value.Vector4Value;

                if (requestedType == typeof(Color))
                    return property.colorValue.Equals(value.ColorValue);

                if (requestedType == typeof(Rect))
                    return property.rectValue.Equals(value.RectValue);

                if (requestedType == typeof(Bounds))
                    return property.boundsValue.Equals(value.BoundsValue);

                if (requestedType == typeof(AnimationCurve))
                    return CurvesRoughlyEqual(property.animationCurveValue, value.CurveValue);

                if (requestedType == typeof(Quaternion))
                    return property.quaternionValue == value.QuaternionValue;
            }
            catch
            {
                return false;
            }

            return false;
        }

        private static AssetUsageFinderSearchScope BuildSuggestedScopeForObjectTarget(Object target)
        {
            if (IsSceneHierarchyObject(target))
                return AssetUsageFinderSearchScope.OpenScene;

            if (IsOpenPrefabStageObject(target))
            {
                return AssetUsageFinderSearchScope.ProjectScenes |
                       AssetUsageFinderSearchScope.ProjectPrefabs |
                       AssetUsageFinderSearchScope.OpenPrefab |
                       AssetUsageFinderSearchScope.Materials |
                       AssetUsageFinderSearchScope.ScriptableObjects |
                       AssetUsageFinderSearchScope.OtherAssets;
            }

            return AssetUsageFinderSearchScopeUtility.DefaultProjectScope |
                   AssetUsageFinderSearchScope.OpenScene |
                   AssetUsageFinderSearchScope.OpenPrefab;
        }

        private static AssetUsageFinderSearchScope BuildSuggestedScopeForSerializedProperty(
            Object owner,
            Type valueType,
            SerializedFieldValueBox value)
        {
            bool isSceneOwner = IsSceneHierarchyObject(owner);
            bool isSceneReference = valueType != null &&
                                    typeof(Object).IsAssignableFrom(valueType) &&
                                    value != null &&
                                    IsSceneHierarchyObject(value.ObjectValue);

            if (isSceneOwner && isSceneReference)
                return AssetUsageFinderSearchScope.OpenScene;

            if (IsOpenPrefabStageObject(owner) &&
                valueType != null &&
                typeof(Object).IsAssignableFrom(valueType) &&
                value != null &&
                IsOpenPrefabStageObject(value.ObjectValue))
            {
                return AssetUsageFinderSearchScope.OpenPrefab |
                       AssetUsageFinderSearchScope.ProjectScenes |
                       AssetUsageFinderSearchScope.ProjectPrefabs;
            }

            return AssetUsageFinderSearchScopeUtility.DefaultProjectScope |
                   AssetUsageFinderSearchScope.OpenScene |
                   AssetUsageFinderSearchScope.OpenPrefab;
        }

        private static string GetObjectContextLabel(Object target)
        {
            switch (target)
            {
                case GameObject gameObject:
                    return $"GameObject {gameObject.name}";
                case Component component:
                    return $"{component.GetType().Name} on {component.gameObject.name}";
                default:
                    return target != null ? target.name : "Unknown";
            }
        }

        private static IReadOnlyList<AssetUsageFinderObjectSnapshot> CaptureRelatedComponentSnapshots(GameObject gameObject)
        {
            if (gameObject == null)
                return Array.Empty<AssetUsageFinderObjectSnapshot>();

            return gameObject.GetComponents<Component>()
                .Where(component => component != null)
                .Select(AssetUsageFinderObjectSnapshot.Capture)
                .Where(snapshot => snapshot != null)
                .ToArray();
        }

        private static bool IsSceneHierarchyObject(Object target)
        {
            switch (target)
            {
                case GameObject gameObject:
                    return !EditorUtility.IsPersistent(gameObject) && !IsOpenPrefabStageObject(gameObject);
                case Component component:
                    return !EditorUtility.IsPersistent(component) && !IsOpenPrefabStageObject(component.gameObject);
                default:
                    return false;
            }
        }

        private static bool IsOpenPrefabStageObject(Object target)
        {
            PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage == null || stage.prefabContentsRoot == null)
                return false;

            return target switch
            {
                GameObject gameObject => gameObject.scene == stage.scene,
                Component component => component.gameObject.scene == stage.scene,
                _ => false
            };
        }

        private static SerializedFieldValueBox BuildValueSnapshot(
            SerializedProperty property,
            out Type valueType,
            out bool matchValue)
        {
            valueType = null;
            matchValue = false;

            if (property == null)
                return null;

            SerializedFieldValueBox box = new();

            try
            {
                switch (property.propertyType)
                {
                    case SerializedPropertyType.ObjectReference:
                        valueType = property.objectReferenceValue != null
                            ? property.objectReferenceValue.GetType()
                            : typeof(Object);
                        box.ObjectValue = property.objectReferenceValue;
                        matchValue = true;
                        return box;

                    case SerializedPropertyType.Enum:
                        valueType = typeof(Enum);
                        box.EnumIndex = property.enumValueIndex;
                        box.EnumName = property.enumDisplayNames != null &&
                                       property.enumValueIndex >= 0 &&
                                       property.enumValueIndex < property.enumDisplayNames.Length
                            ? property.enumDisplayNames[property.enumValueIndex]
                            : property.enumValueIndex.ToString();
                        matchValue = true;
                        return box;

                    case SerializedPropertyType.Boolean:
                        valueType = typeof(bool);
                        box.BoolValue = property.boolValue;
                        matchValue = true;
                        return box;

                    case SerializedPropertyType.String:
                        valueType = typeof(string);
                        box.StringValue = property.stringValue ?? string.Empty;
                        matchValue = true;
                        return box;

                    case SerializedPropertyType.Integer:
                        valueType = typeof(long);
                        box.LongValue = property.longValue;
                        matchValue = true;
                        return box;

                    case SerializedPropertyType.Float:
                        valueType = typeof(double);
                        box.DoubleValue = property.doubleValue;
                        matchValue = true;
                        return box;

                    case SerializedPropertyType.Vector2:
                        valueType = typeof(Vector2);
                        box.Vector2Value = property.vector2Value;
                        matchValue = true;
                        return box;

                    case SerializedPropertyType.Vector3:
                        valueType = typeof(Vector3);
                        box.Vector3Value = property.vector3Value;
                        matchValue = true;
                        return box;

                    case SerializedPropertyType.Vector4:
                        valueType = typeof(Vector4);
                        box.Vector4Value = property.vector4Value;
                        matchValue = true;
                        return box;

                    case SerializedPropertyType.Color:
                        valueType = typeof(Color);
                        box.ColorValue = property.colorValue;
                        matchValue = true;
                        return box;

                    case SerializedPropertyType.Rect:
                        valueType = typeof(Rect);
                        box.RectValue = property.rectValue;
                        matchValue = true;
                        return box;

                    case SerializedPropertyType.Bounds:
                        valueType = typeof(Bounds);
                        box.BoundsValue = property.boundsValue;
                        matchValue = true;
                        return box;

                    case SerializedPropertyType.AnimationCurve:
                        valueType = typeof(AnimationCurve);
                        box.CurveValue = property.animationCurveValue;
                        matchValue = true;
                        return box;

                    case SerializedPropertyType.Quaternion:
                        valueType = typeof(Quaternion);
                        box.QuaternionValue = property.quaternionValue;
                        matchValue = true;
                        return box;
                }
            }
            catch
            {
                // Unsupported property type; fall through to property-path-only query.
            }

            return box;
        }

        private static bool CurvesRoughlyEqual(AnimationCurve left, AnimationCurve right)
        {
            if (left == null && right == null) return true;
            if (left == null || right == null) return false;
            if (left.length != right.length) return false;

            for (int i = 0; i < left.length; i++)
            {
                Keyframe a = left.keys[i];
                Keyframe b = right.keys[i];
                if (Math.Abs(a.time - b.time) > 0.0001f) return false;
                if (Math.Abs(a.value - b.value) > 0.0001f) return false;
                if (Math.Abs(a.inTangent - b.inTangent) > 0.0001f) return false;
                if (Math.Abs(a.outTangent - b.outTangent) > 0.0001f) return false;
            }

            return true;
        }
    }

    public sealed class AssetUsageFinderContextualResult
    {
        public string FileAssetPath { get; }
        public string ObjectPath { get; }
        public string ObjectTypeName { get; }
        public string PropertyPath { get; }
        public string CurrentValue { get; }
        public string MatchDescription { get; }
        public AssetUsageFinderUsageType UsageType { get; }
        public AssetUsageFinderContextualReferenceKind ReferenceKind { get; }
        public string ReferenceLabel { get; }
        public string ReferenceDetails { get; }
        public AssetUsageFinderPrefabProvenanceKind PrefabProvenanceKind { get; }
        public string PrefabProvenanceLabel { get; }
        public string PrefabProvenanceDetails { get; }
        public string PrefabSourceAssetPath { get; }

        public AssetUsageFinderContextualResult(
            string fileAssetPath,
            string objectPath,
            string objectTypeName,
            string propertyPath,
            string currentValue,
            string matchDescription,
            AssetUsageFinderUsageType usageType,
            AssetUsageFinderContextualReferenceInfo referenceInfo,
            AssetUsageFinderPrefabProvenanceInfo provenanceInfo)
        {
            FileAssetPath = fileAssetPath ?? string.Empty;
            ObjectPath = objectPath ?? string.Empty;
            ObjectTypeName = objectTypeName ?? string.Empty;
            PropertyPath = propertyPath ?? string.Empty;
            CurrentValue = currentValue ?? string.Empty;
            MatchDescription = matchDescription ?? string.Empty;
            UsageType = usageType;
            ReferenceKind = referenceInfo?.Kind ?? AssetUsageFinderContextualReferenceKind.None;
            ReferenceLabel = referenceInfo?.Label ?? string.Empty;
            ReferenceDetails = referenceInfo?.Details ?? string.Empty;
            PrefabProvenanceKind = provenanceInfo?.Kind ?? AssetUsageFinderPrefabProvenanceKind.None;
            PrefabProvenanceLabel = provenanceInfo?.Label ?? string.Empty;
            PrefabProvenanceDetails = provenanceInfo?.Details ?? string.Empty;
            PrefabSourceAssetPath = provenanceInfo?.SourceAssetPath ?? string.Empty;
        }
    }
}
