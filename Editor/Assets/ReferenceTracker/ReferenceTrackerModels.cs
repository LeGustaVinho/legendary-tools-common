using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LegendaryTools.Editor
{
    [Serializable]
    internal sealed class ReferenceTrackerWindowState
    {
        public UnityEngine.Object Target;
        public bool UseSelectionAsTarget;
        public ReferenceTrackerSearchScope SearchScopes =
            ReferenceTrackerSearchScope.CurrentScene |
            ReferenceTrackerSearchScope.ScenesInProject |
            ReferenceTrackerSearchScope.PrefabMode |
            ReferenceTrackerSearchScope.Prefabs |
            ReferenceTrackerSearchScope.Materials |
            ReferenceTrackerSearchScope.ScriptableObjects |
            ReferenceTrackerSearchScope.Others |
            ReferenceTrackerSearchScope.AnimatorControllersAndAnimationClips |
            ReferenceTrackerSearchScope.TimelineAssets |
            ReferenceTrackerSearchScope.AddressablesGroups |
            ReferenceTrackerSearchScope.ResourcesFolders |
            ReferenceTrackerSearchScope.AssetBundles;
        public ReferenceTrackerGroupMode GroupMode = ReferenceTrackerGroupMode.GameObject;
        public string Status = "Select an asset, script, GameObject, or Component and run the search.";
        public double LastSearchDurationMs;
        public List<ReferenceTrackerUsageResult> Results = new List<ReferenceTrackerUsageResult>();
        public List<ReferenceTrackerGroupBucket> Groups = new List<ReferenceTrackerGroupBucket>();
        public bool IsSearching;
        public ReferenceTrackerSortColumn SortColumn = ReferenceTrackerSortColumn.Asset;
        public bool SortAscending = true;
    }

    internal enum ReferenceTrackerSortColumn
    {
        Kind,
        Asset,
        GameObject,
        Component,
        Property,
        Reference,
    }

    [Serializable]
    internal sealed class ReferenceTrackerSearchTargetContext
    {
        public UnityEngine.Object OriginalTarget;
        public UnityEngine.Object TargetAsset;
        public GameObject TargetGameObject;
        public Component TargetComponent;
        public string TargetAssetPath;
        public string TargetGuid;
        public long TargetLocalFileId;
        public bool IsAssetTarget;
        public bool IsMonoScriptTarget;
    }

    [Serializable]
    internal sealed class ReferenceTrackerUsageResult
    {
        public string AssetPath;
        public string AssetLabel;
        public string AssetKindLabel;
        public UnityEngine.Object AssetObject;
        public UnityEngine.Object HostObject;
        public GameObject HostGameObject;
        public Component HostComponent;
        public string HostGameObjectPath;
        public string HostComponentLabel;
        public string PropertyPath;
        public string PropertyDisplayName;
        public string ReferenceTypeLabel;
        public UnityEngine.Object ReferencedObject;
        public ReferenceTrackerSearchScope SourceScope;
        public bool IsLiveContext;
        public bool IsFallback;
    }

    [Serializable]
    internal sealed class ReferenceTrackerGroupBucket
    {
        public string Key;
        public readonly List<ReferenceTrackerUsageResult> Items = new List<ReferenceTrackerUsageResult>();
    }

    internal sealed class ReferenceTrackerScopeDescriptor
    {
        public ReferenceTrackerSearchScope Scope;
        public Scene Scene;
        public string Label;
    }

    internal sealed class ReferenceTrackerSearchResult
    {
        public readonly List<ReferenceTrackerUsageResult> Usages = new List<ReferenceTrackerUsageResult>();
        public string Status;
        public double DurationMs;
    }
}
