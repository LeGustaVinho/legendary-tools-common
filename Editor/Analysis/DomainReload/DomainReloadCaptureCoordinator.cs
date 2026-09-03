using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Profiling;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Profiling;

namespace LegendaryTools.Editor.DomainReload
{
    /// <summary>
    /// Persists its state under Library so a capture survives the very domain destruction it measures.
    /// If the Editor is killed during a hang, the pending state can be recovered on the next launch.
    /// </summary>
    [InitializeOnLoad]
    public static class DomainReloadCaptureCoordinator
    {
        private const string FolderName = "DomainReloadAnalyzer";
        private const string PendingFileName = "pending-capture.json";
        private static int _settleFrames;
        private static int _attempts;

        public static event Action CaptureCompleted;

        static DomainReloadCaptureCoordinator()
        {
            if (File.Exists(PendingPath))
            {
                _settleFrames = 8;
                _attempts = 0;
                EditorApplication.update -= TryFinalizePending;
                EditorApplication.update += TryFinalizePending;
            }
        }

        public static string OutputDirectory => Path.Combine(ProjectRoot, "Library", FolderName);
        public static bool HasPendingCapture => File.Exists(PendingPath);

        private static string ProjectRoot => Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
        private static string PendingPath => Path.Combine(OutputDirectory, PendingFileName);

        public static void BeginCapture(bool useProfiler, bool deepProfile, bool saveProfilerCapture)
        {
            if (HasPendingCapture)
                throw new InvalidOperationException("A capture is already pending. Complete or cancel it before starting another one.");

            Directory.CreateDirectory(OutputDirectory);
            string logPath = GetEditorLogPath();
            FileInfo log = new(logPath);
            PendingDomainReloadCapture pending = new()
            {
                CaptureId = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"),
                StartedAtUtc = DateTime.UtcNow.ToString("O"),
                StartedUtcTicks = DateTime.UtcNow.Ticks,
                LogPath = logPath,
                LogOffset = log.Exists ? log.Length : 0,
                UseProfiler = useProfiler,
                UseDeepProfiler = useProfiler && deepProfile,
                SaveProfilerCapture = useProfiler && saveProfilerCapture,
                ProcessId = Process.GetCurrentProcess().Id,
                StartProfilerFrame = useProfiler ? ProfilerDriver.lastFrameIndex : -1,
                PreviousProfilerEnabled = ProfilerDriver.enabled,
                PreviousProfileEditor = ProfilerDriver.profileEditor,
                PreviousDeepProfiling = ProfilerDriver.deepProfiling
            };

            pending.PerformanceTrackerBaseline = DomainReloadPerformanceTrackerReader.CaptureBaseline(
                out double trackerTimestamp, out string trackerDiagnostic);
            pending.PerformanceTrackerStartTimestamp = trackerTimestamp;
            pending.PerformanceTrackerDiagnostic = trackerDiagnostic;

            File.WriteAllText(PendingPath, DomainReloadJson.Serialize(pending));

            if (useProfiler)
            {
                ProfilerDriver.SetAreaEnabled(ProfilerArea.CPU, true);
                ProfilerDriver.profileEditor = true;
                ProfilerDriver.enabled = true;
                if (deepProfile)
                    ProfilerDriver.deepProfiling = true;
            }

            UnityEngine.Debug.Log($"[Domain Reload Analyzer] Capture {pending.CaptureId} armed at log offset {pending.LogOffset}.");
            EditorUtility.RequestScriptReload();
        }

        public static void CancelPendingCapture()
        {
            PendingDomainReloadCapture pending = LoadPending();
            if (pending != null)
                RestoreProfilerState(pending);
            if (File.Exists(PendingPath))
                File.Delete(PendingPath);
            EditorApplication.update -= TryFinalizePending;
            CaptureCompleted?.Invoke();
        }

        public static List<DomainReloadReport> LoadSavedReports()
        {
            List<DomainReloadReport> reports = new();
            if (!Directory.Exists(OutputDirectory))
                return reports;
            foreach (string path in Directory.EnumerateFiles(OutputDirectory, "report-*.json")
                         .OrderByDescending(File.GetLastWriteTimeUtc))
            {
                try
                {
                    DomainReloadReport report = DomainReloadJson.Deserialize<DomainReloadReport>(File.ReadAllText(path));
                    if (report != null)
                        reports.Add(report);
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogWarning($"Domain Reload Analyzer could not read {path}: {ex.Message}");
                }
            }
            return reports;
        }

        public static List<DomainReloadReport> LoadReportsFromEditorLog(int maximum = 30)
        {
            string path = GetEditorLogPath();
            if (!File.Exists(path))
                return new List<DomainReloadReport>();
            string text = ReadLogText(path, 0);
            List<DomainReloadReport> reports = DomainReloadLogParser.Parse(text, path);
            return reports.TakeLast(Math.Max(1, maximum)).Reverse().ToList();
        }

        public static string GetEditorLogPath()
        {
            switch (Application.platform)
            {
                case RuntimePlatform.WindowsEditor:
                    return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "Unity", "Editor", "Editor.log");
                case RuntimePlatform.OSXEditor:
                    return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal),
                        "Library", "Logs", "Unity", "Editor.log");
                default:
                    return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal),
                        ".config", "unity3d", "Editor.log");
            }
        }

        private static void TryFinalizePending()
        {
            if (_settleFrames-- > 0 || EditorApplication.isCompiling || EditorApplication.isUpdating)
                return;

            PendingDomainReloadCapture pending = LoadPending();
            if (pending == null)
            {
                EditorApplication.update -= TryFinalizePending;
                return;
            }

            string logPath = ResolveCaptureLogPath(pending);
            string segment = ReadCaptureSegment(logPath, pending);
            // The Editor can keep stale bytes after a preallocated NUL region. The first timing block
            // after our unique marker is the capture; a later physical block is not necessarily newer.
            DomainReloadReport report = DomainReloadLogParser.Parse(segment, logPath, pending.LogOffset).FirstOrDefault();
            if (report == null && _attempts++ < 180)
                return;

            // The reload itself is part of the current Editor frame. RawFrameDataView only exposes it
            // after that frame has closed, usually on the next update after the log block appears.
            if (report != null && pending.UseProfiler &&
                (ProfilerDriver.lastFrameIndex < 0 || ProfilerDriver.lastFrameIndex <= pending.StartProfilerFrame))
            {
                _settleFrames = 2;
                return;
            }

            if (report == null)
            {
                report = DomainReloadLogParser.ParseIncomplete(segment, logPath, pending.LogOffset) ??
                         new DomainReloadReport
                         {
                             Completed = false,
                             Status = "No 'Domain Reload Profiling' block was found. The log may have rotated, or reload may have stalled before writing timings.",
                             LogPath = logPath,
                             LogOffset = pending.LogOffset
                         };
            }

            report.CaptureId = pending.CaptureId;
            report.CapturedAtUtc = DateTime.UtcNow.ToString("O");
            report.UnityVersion = Application.unityVersion;
            report.WallClockMs = Math.Max(0, TimeSpan.FromTicks(DateTime.UtcNow.Ticks - pending.StartedUtcTicks).TotalMilliseconds);
            report.ProfilerEnabled = pending.UseProfiler;
            report.DeepProfilingEnabled = pending.UseDeepProfiler;
            report.Timeline = DomainReloadPerformanceTrackerReader.ReadTimeline(
                pending.PerformanceTrackerBaseline,
                pending.PerformanceTrackerStartTimestamp,
                out List<DomainReloadOwnerTiming> assemblyTimings,
                out List<DomainReloadOwnerTiming> scriptTimings,
                out string timelineDiagnostic);
            report.AssemblyTimings = assemblyTimings;
            report.ScriptTimings = scriptTimings;
            report.TimelineDiagnostic = timelineDiagnostic ?? pending.PerformanceTrackerDiagnostic;
            if (!string.IsNullOrEmpty(pending.PerformanceTrackerDiagnostic))
                report.Evidence.Add(pending.PerformanceTrackerDiagnostic);

            if (pending.UseProfiler)
            {
                report.ProfilerSamples = DomainReloadProfilerReader.Read(pending.StartProfilerFrame,
                    ProfilerDriver.lastFrameIndex, pending.UseDeepProfiler);
                if (pending.SaveProfilerCapture)
                {
                    string profilePath = Path.Combine(OutputDirectory, "profile-" + pending.CaptureId + ".data");
                    try
                    {
                        if (ProfilerDriver.SaveProfile(profilePath))
                            report.ProfilerCapturePath = profilePath;
                    }
                    catch (Exception ex)
                    {
                        report.Evidence.Add("Failed to save Profiler capture: " + ex.Message);
                    }
                }
            }

            string reportPath = Path.Combine(OutputDirectory, "report-" + pending.CaptureId + ".json");
            File.WriteAllText(reportPath, DomainReloadJson.Serialize(report));
            RestoreProfilerState(pending);
            File.Delete(PendingPath);
            EditorApplication.update -= TryFinalizePending;
            UnityEngine.Debug.Log($"[Domain Reload Analyzer] Capture {pending.CaptureId} completed: {report.TotalMs:0.###} ms. Report: {reportPath}");
            CaptureCompleted?.Invoke();
        }

        private static PendingDomainReloadCapture LoadPending()
        {
            try
            {
                return File.Exists(PendingPath)
                    ? DomainReloadJson.Deserialize<PendingDomainReloadCapture>(File.ReadAllText(PendingPath))
                    : null;
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError("Domain Reload Analyzer pending state is invalid: " + ex.Message);
                return null;
            }
        }

        private static void RestoreProfilerState(PendingDomainReloadCapture pending)
        {
            try
            {
                ProfilerDriver.deepProfiling = pending.PreviousDeepProfiling;
                ProfilerDriver.profileEditor = pending.PreviousProfileEditor;
                ProfilerDriver.enabled = pending.PreviousProfilerEnabled;
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning("Domain Reload Analyzer could not restore Profiler state: " + ex.Message);
            }
        }

        private static string ResolveCaptureLogPath(PendingDomainReloadCapture pending)
        {
            string marker = "[Domain Reload Analyzer] Capture " + pending.CaptureId + " armed";
            if (File.Exists(pending.LogPath) && ReadLogText(pending.LogPath, 0)
                    .IndexOf(marker, StringComparison.Ordinal) >= 0)
                return pending.LogPath;
            string directory = Path.GetDirectoryName(pending.LogPath);
            string previous = Path.Combine(directory ?? string.Empty, "Editor-prev.log");
            if (File.Exists(previous) && ReadLogText(previous, 0).IndexOf(marker, StringComparison.Ordinal) >= 0)
                return previous;
            return pending.LogPath;
        }

        private static string ReadCaptureSegment(string path, PendingDomainReloadCapture pending)
        {
            string text = ReadLogText(path, 0).Replace("\0", string.Empty);
            string marker = "[Domain Reload Analyzer] Capture " + pending.CaptureId + " armed";
            int markerIndex = text.LastIndexOf(marker, StringComparison.Ordinal);
            if (markerIndex >= 0)
                return text.Substring(markerIndex);

            // Conventional append-only logs can still use the byte offset as a fallback.
            return ReadLogText(path, pending.LogOffset);
        }

        private static string ReadLogText(string path, long offset)
        {
            if (!File.Exists(path))
                return string.Empty;
            try
            {
                using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                if (offset > 0 && offset < stream.Length)
                    stream.Seek(offset, SeekOrigin.Begin);
                else if (offset >= stream.Length)
                    return string.Empty;
                using StreamReader reader = new(stream, true);
                return reader.ReadToEnd();
            }
            catch (Exception ex)
            {
                return "Could not read Editor.log: " + ex.Message;
            }
        }
    }

    internal static class DomainReloadProfilerReader
    {
        private sealed class Accumulator
        {
            public string Marker;
            public string Thread;
            public double Total;
            public double Max;
            public int Count;
            public int First = int.MaxValue;
            public int Last = int.MinValue;
        }

        private static readonly string[] RelevantMarkerFragments =
        {
            "reload", "assembly", "domain", "script", "initializeonload", "backupinstance",
            "awakeinstances", "serialize", "deserialize", "typecache", "assetdatabase", "mono",
            "scripting", "gc."
        };

        public static List<ProfilerSampleSummary> Read(int startFrame, int endFrame, bool deepProfiling)
        {
            Dictionary<string, Accumulator> samples = new(StringComparer.Ordinal);
            int first = ProfilerDriver.firstFrameIndex;
            int last = Math.Min(endFrame, ProfilerDriver.lastFrameIndex);
            if (first < 0 || last < 0)
                return new List<ProfilerSampleSummary>();

            int frame = startFrame >= first && startFrame < last
                ? ProfilerDriver.GetNextFrameIndex(startFrame)
                : first;
            int guard = 0;
            while (frame >= 0 && frame <= last && guard++ < 1000)
            {
                for (int threadIndex = 0; ; threadIndex++)
                {
                    using RawFrameDataView data = ProfilerDriver.GetRawFrameDataView(frame, threadIndex);
                    if (!data.valid)
                        break;
                    string thread = string.IsNullOrEmpty(data.threadName) ? "Thread " + threadIndex : data.threadName;
                    for (int sampleIndex = 0; sampleIndex < data.sampleCount; sampleIndex++)
                    {
                        double ms = data.GetSampleTimeMs(sampleIndex);
                        if (ms < (deepProfiling ? 0.25 : 0.05))
                            continue;
                        string marker = data.GetSampleName(sampleIndex);
                        if (string.IsNullOrEmpty(marker) ||
                            !ShouldKeep(marker, deepProfiling))
                            continue;
                        string key = marker + "\n" + thread;
                        if (!samples.TryGetValue(key, out Accumulator acc))
                        {
                            acc = new Accumulator { Marker = marker, Thread = thread };
                            samples.Add(key, acc);
                        }
                        acc.Total += ms;
                        acc.Max = Math.Max(acc.Max, ms);
                        acc.Count++;
                        acc.First = Math.Min(acc.First, frame);
                        acc.Last = Math.Max(acc.Last, frame);
                    }
                }

                int next = ProfilerDriver.GetNextFrameIndex(frame);
                if (next <= frame)
                    break;
                frame = next;
            }

            return samples.Values.OrderByDescending(sample => sample.Max).Take(250)
                .Select(sample => new ProfilerSampleSummary
                {
                    Marker = sample.Marker,
                    Thread = sample.Thread,
                    InclusiveMs = sample.Total,
                    MaxMs = sample.Max,
                    Count = sample.Count,
                    FirstFrame = sample.First,
                    LastFrame = sample.Last
                }).ToList();
        }

        private static bool ShouldKeep(string marker, bool deepProfiling)
        {
            string lower = marker.ToLowerInvariant();
            if (lower == "editorloop" || lower == "playerloop" || lower.Contains("profilerwindow"))
                return false;
            return deepProfiling || RelevantMarkerFragments.Any(lower.Contains);
        }
    }
}
