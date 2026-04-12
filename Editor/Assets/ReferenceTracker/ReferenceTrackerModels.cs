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
        public ReferenceTrackerSearchScope SearchScopes = ReferenceTrackerSearchScope.CurrentScene;
        public ReferenceTrackerGroupMode GroupMode = ReferenceTrackerGroupMode.GameObject;
        public string Status = "Select a GameObject or Component and run the search.";
        public double LastSearchDurationMs;
        public List<ReferenceTrackerUsageResult> Results = new List<ReferenceTrackerUsageResult>();
        public List<ReferenceTrackerGroupBucket> Groups = new List<ReferenceTrackerGroupBucket>();
    }

    [Serializable]
    internal sealed class ReferenceTrackerSearchTargetContext
    {
        public UnityEngine.Object OriginalTarget;
        public GameObject TargetGameObject;
        public Component TargetComponent;
    }

    [Serializable]
    internal sealed class ReferenceTrackerUsageResult
    {
        public GameObject HostGameObject;
        public Component HostComponent;
        public string HostGameObjectPath;
        public string HostComponentLabel;
        public string PropertyPath;
        public string PropertyDisplayName;
        public string ReferenceTypeLabel;
        public UnityEngine.Object ReferencedObject;
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
