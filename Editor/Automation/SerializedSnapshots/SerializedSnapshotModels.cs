using System;
using System.Collections.Generic;
using UnityEngine;

namespace LegendaryTools.Editor
{
    public enum SerializedSnapshotScope
    {
        Component = 0,
        GameObject = 1
    }

    public enum SerializedSnapshotReferenceKind
    {
        Null = 0,
        Asset = 1,
        GameObject = 2,
        Component = 3,
        GlobalObjectId = 4,
        Unsupported = 5
    }

    [Serializable]
    public sealed class SerializedObjectReferenceSnapshot
    {
        public string PropertyPath;
        public SerializedSnapshotReferenceKind Kind;
        public string AssetGuid;
        public long AssetLocalId;
        public string RelativeGameObjectPath;
        public string ComponentTypeName;
        public int ComponentOccurrenceIndex;
        public string GlobalObjectId;
        public string DebugLabel;
    }

    [Serializable]
    public sealed class SerializedCapturedPropertySnapshot
    {
        public string PropertyPath;
        public string DisplayName;
        public string PropertyTypeName;
        public string ValuePreview;
        public int Depth;
    }

    [Serializable]
    public sealed class SerializedComponentSnapshot
    {
        public string ComponentTypeName;
        public string ComponentDisplayName;
        public int ComponentOccurrenceIndex;
        public string JsonData;
        public int TopLevelPropertyCount;
        public int ObjectReferenceCount;
        public List<SerializedCapturedPropertySnapshot> CapturedProperties = new();
        public List<SerializedObjectReferenceSnapshot> ObjectReferences = new();
    }

    [Serializable]
    public sealed class SerializedSnapshotRecord
    {
        public string Id;
        public string Name;
        public SerializedSnapshotScope Scope;
        public string SourceScenePath;
        public string SourceHierarchyPath;
        public string SourceComponentTypeName;
        public string SourcePrefabAssetPath;
        public long CapturedAtUtcTicks;
        public int UnsupportedReferenceCount;
        public List<SerializedComponentSnapshot> Components = new();

        public DateTime CapturedAtUtc => new(CapturedAtUtcTicks, DateTimeKind.Utc);
    }

    public sealed class SerializedSnapshotPreview
    {
        public GameObject TargetRoot;
        public int MatchedComponents;
        public int MissingComponents;
        public int ResolvableReferences;
        public int UnresolvableReferences;
        public readonly List<string> Messages = new();

        public bool HasTarget => TargetRoot != null;
        public bool IsFullyCompatible => HasTarget && MissingComponents == 0 && UnresolvableReferences == 0;
    }

    public sealed class SerializedSnapshotApplyReport
    {
        public int AppliedComponents;
        public int SkippedComponents;
        public int AppliedObjectReferences;
        public int FailedObjectReferences;
        public readonly List<string> Messages = new();

        public bool HasWarnings => SkippedComponents > 0 || FailedObjectReferences > 0 || Messages.Count > 0;
    }
}
