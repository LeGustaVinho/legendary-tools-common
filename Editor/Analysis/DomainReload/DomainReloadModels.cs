using System;
using System.Collections.Generic;

namespace LegendaryTools.Editor.DomainReload
{
    [Serializable]
    public sealed class DomainReloadStep
    {
        public string Name;
        public double DurationMs;
        public int Depth;
        public double SelfMs;
        public List<DomainReloadStep> Children = new();
    }

    [Serializable]
    public sealed class AssetPipelineTiming
    {
        public string Id;
        public string Initiator;
        public double TotalMs;
        public int DomainReloadCount;
        public double DomainReloadMs;
        public double CompileMs;
        public double ManagedProcessMs;
        public double NativeProcessMs;
        public List<DomainReloadStep> Steps = new();
    }

    [Serializable]
    public sealed class DomainReloadReport
    {
        public string CaptureId;
        public string CapturedAtUtc;
        public string UnityVersion;
        public string LogPath;
        public long LogOffset;
        public double TotalMs;
        public double WallClockMs;
        public bool Completed;
        public bool ProfilerEnabled;
        public bool DeepProfilingEnabled;
        public string ProfilerCapturePath;
        public string Status;
        public List<DomainReloadStep> Steps = new();
        public AssetPipelineTiming AssetPipeline;
        public List<string> Evidence = new();
        public List<ProfilerSampleSummary> ProfilerSamples = new();
        public List<DomainReloadTimelineEvent> Timeline = new();
        public List<DomainReloadOwnerTiming> AssemblyTimings = new();
        public List<DomainReloadOwnerTiming> ScriptTimings = new();
        public string TimelineDiagnostic;
    }

    [Serializable]
    public sealed class DomainReloadTimelineEvent
    {
        public int Order;
        public string Tracker;
        public string Category;
        public double StartMs;
        public double DurationMs;
        public double TotalMs;
        public int Count;
        public string AssemblyName;
        public string ScriptPath;
    }

    [Serializable]
    public sealed class DomainReloadOwnerTiming
    {
        public string Name;
        public string AssemblyName;
        public string ScriptPath;
        public double TotalMs;
        public double MaxMs;
        public int EventCount;
    }

    [Serializable]
    public sealed class ProfilerSampleSummary
    {
        public string Marker;
        public string Thread;
        public double InclusiveMs;
        public double MaxMs;
        public int Count;
        public int FirstFrame;
        public int LastFrame;
    }

    public enum DomainReloadFindingKind
    {
        ReloadCallback,
        Serialization,
        ObjectLifecycle,
        BackgroundWork,
        ExpensiveOperation,
        StaticInitialization,
        ImportPipeline,
        Assembly
    }

    public enum DomainReloadRisk
    {
        Info,
        Low,
        Medium,
        High
    }

    [Serializable]
    public sealed class DomainReloadFinding
    {
        public DomainReloadFindingKind Kind;
        public DomainReloadRisk Risk;
        public string Symbol;
        public string Detail;
        public string AssetPath;
        public string FullPath;
        public int Line;
        public string Origin;
        public string PackageName;
        public string AssemblyName;
        public string Evidence;
    }

    [Serializable]
    public sealed class DomainReloadAssemblyInfo
    {
        public string Name;
        public string Origin;
        public string PackageName;
        public string OutputPath;
        public int SourceFileCount;
        public int ReferenceCount;
        public long BinaryBytes;
        public int FindingCount;
        public int HighRiskCount;
        public int ReloadCallbackCount;
    }

    [Serializable]
    public sealed class DomainReloadScriptInfo
    {
        public string AssetPath;
        public string FullPath;
        public string AssemblyName;
        public string Origin;
        public string PackageName;
        public int FindingCount;
        public int HighRiskCount;
        public int ReloadCallbackCount;
    }

    [Serializable]
    public sealed class DomainReloadObjectInfo
    {
        public string TypeName;
        public string AssemblyName;
        public string Kind;
        public string Origin;
        public int Count;
    }

    [Serializable]
    public sealed class DomainReloadAudit
    {
        public string ScannedAtUtc;
        public string UnityVersion;
        public int FilesScanned;
        public int PackagesScanned;
        public List<DomainReloadFinding> Findings = new();
        public List<DomainReloadScriptInfo> Scripts = new();
        public List<DomainReloadAssemblyInfo> Assemblies = new();
        public List<DomainReloadObjectInfo> LiveObjects = new();
        public List<string> Diagnostics = new();
    }

    [Serializable]
    internal sealed class PendingDomainReloadCapture
    {
        public string CaptureId;
        public string StartedAtUtc;
        public long StartedUtcTicks;
        public string LogPath;
        public long LogOffset;
        public int StartProfilerFrame;
        public bool UseProfiler;
        public bool UseDeepProfiler;
        public bool SaveProfilerCapture;
        public bool PreviousProfilerEnabled;
        public bool PreviousProfileEditor;
        public bool PreviousDeepProfiling;
        public int ProcessId;
        public double PerformanceTrackerStartTimestamp;
        public List<PerformanceTrackerSnapshot> PerformanceTrackerBaseline = new();
        public string PerformanceTrackerDiagnostic;
    }

    [Serializable]
    internal sealed class PerformanceTrackerSnapshot
    {
        public string Name;
        public double TotalSeconds;
        public int SampleCount;
    }
}
