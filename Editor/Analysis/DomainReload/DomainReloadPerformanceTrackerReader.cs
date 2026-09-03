using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Compilation;

namespace LegendaryTools.Editor.DomainReload
{
    /// <summary>
    /// Reads Unity's native Editor performance trackers through reflection. The API is internal, so every access is
    /// guarded and the analyzer continues to work when Unity changes or removes it.
    /// </summary>
    internal static class DomainReloadPerformanceTrackerReader
    {
        private sealed class CurrentTracker
        {
            public string Name;
            public double LastSeconds;
            public double TotalSeconds;
            public double TimestampSeconds;
            public int DeltaCount;
            public double DeltaTotalSeconds;
        }

        private static readonly BindingFlags StaticFlags =
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        public static List<PerformanceTrackerSnapshot> CaptureBaseline(out double timestamp, out string diagnostic)
        {
            timestamp = EditorApplication.timeSinceStartup;
            diagnostic = null;
            try
            {
                if (!TryGetApi(out MethodInfo available, out _, out MethodInfo total, out MethodInfo count,
                        out _, out diagnostic))
                    return new List<PerformanceTrackerSnapshot>();

                string[] names = (string[])available.Invoke(null, null) ?? Array.Empty<string>();
                return names.Select(name => new PerformanceTrackerSnapshot
                {
                    Name = name,
                    TotalSeconds = InvokeDouble(total, name),
                    SampleCount = InvokeInt(count, name)
                }).ToList();
            }
            catch (Exception ex)
            {
                diagnostic = "Unity Editor performance tracker baseline failed: " + GetBaseMessage(ex);
                return new List<PerformanceTrackerSnapshot>();
            }
        }

        public static List<DomainReloadTimelineEvent> ReadTimeline(
            IReadOnlyList<PerformanceTrackerSnapshot> baseline,
            double captureStartTimestamp,
            out List<DomainReloadOwnerTiming> assemblyTimings,
            out List<DomainReloadOwnerTiming> scriptTimings,
            out string diagnostic)
        {
            assemblyTimings = new List<DomainReloadOwnerTiming>();
            scriptTimings = new List<DomainReloadOwnerTiming>();
            diagnostic = null;
            try
            {
                if (!TryGetApi(out MethodInfo available, out MethodInfo last, out MethodInfo total,
                        out MethodInfo count, out MethodInfo trackerTimestamp, out diagnostic))
                    return new List<DomainReloadTimelineEvent>();

                Dictionary<string, PerformanceTrackerSnapshot> previous = (baseline ?? Array.Empty<PerformanceTrackerSnapshot>())
                    .Where(item => item != null && !string.IsNullOrEmpty(item.Name))
                    .GroupBy(item => item.Name, StringComparer.Ordinal)
                    .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);

                List<CurrentTracker> changed = new();
                foreach (string name in (string[])available.Invoke(null, null) ?? Array.Empty<string>())
                {
                    int currentCount = InvokeInt(count, name);
                    double currentTotal = InvokeDouble(total, name);
                    previous.TryGetValue(name, out PerformanceTrackerSnapshot old);
                    int deltaCount = currentCount - (old?.SampleCount ?? 0);
                    double deltaTotal = currentTotal - (old?.TotalSeconds ?? 0.0);
                    if (deltaCount <= 0 || deltaTotal < -0.000001)
                        continue;

                    changed.Add(new CurrentTracker
                    {
                        Name = name,
                        LastSeconds = Math.Max(0.0, InvokeDouble(last, name)),
                        TotalSeconds = currentTotal,
                        TimestampSeconds = InvokeDouble(trackerTimestamp, name),
                        DeltaCount = deltaCount,
                        DeltaTotalSeconds = Math.Max(0.0, deltaTotal)
                    });
                }

                if (changed.Count == 0)
                {
                    diagnostic = "Unity exposed performance trackers, but none changed during this capture.";
                    return new List<DomainReloadTimelineEvent>();
                }

                CurrentTracker reload = changed
                    .Where(item => string.Equals(item.Name, "Application.Reload", StringComparison.Ordinal))
                    .OrderByDescending(item => item.TimestampSeconds)
                    .FirstOrDefault();
                double windowStart = reload != null
                    ? reload.TimestampSeconds - reload.LastSeconds
                    : captureStartTimestamp;
                double windowEnd = reload?.TimestampSeconds ?? changed.Max(item => item.TimestampSeconds);

                OwnerResolver owners = new();
                List<DomainReloadTimelineEvent> timeline = changed
                    .Where(item => item.TimestampSeconds >= windowStart - 0.010 &&
                                   item.TimestampSeconds <= windowEnd + 0.100)
                    .Select(item => CreateEvent(item, windowStart, owners))
                    .OrderBy(item => item.StartMs)
                    .ThenByDescending(item => item.DurationMs)
                    .ThenBy(item => item.Tracker, StringComparer.Ordinal)
                    .Take(2000)
                    .ToList();

                for (int i = 0; i < timeline.Count; i++)
                    timeline[i].Order = i + 1;

                assemblyTimings = AggregateOwners(timeline.Where(item => !string.IsNullOrEmpty(item.AssemblyName)), false);
                scriptTimings = AggregateOwners(timeline.Where(item => !string.IsNullOrEmpty(item.ScriptPath)), true);
                diagnostic = reload == null
                    ? "Order is based on tracker completion timestamps; Application.Reload was unavailable, so zero is the capture request."
                    : "Order is based on Unity Editor tracker timestamps. Nested timings are inclusive and can overlap; only instrumented callbacks expose script-level timing.";
                return timeline;
            }
            catch (Exception ex)
            {
                diagnostic = "Unity Editor performance tracker timeline failed: " + GetBaseMessage(ex);
                return new List<DomainReloadTimelineEvent>();
            }
        }

        private static DomainReloadTimelineEvent CreateEvent(CurrentTracker tracker, double windowStart,
            OwnerResolver owners)
        {
            owners.Resolve(tracker.Name, out string assemblyName, out string scriptPath);
            double duration = tracker.LastSeconds * 1000.0;
            double completed = (tracker.TimestampSeconds - windowStart) * 1000.0;
            return new DomainReloadTimelineEvent
            {
                Tracker = tracker.Name,
                Category = Categorize(tracker.Name),
                StartMs = completed - duration,
                DurationMs = duration,
                TotalMs = tracker.DeltaTotalSeconds * 1000.0,
                Count = tracker.DeltaCount,
                AssemblyName = assemblyName,
                ScriptPath = scriptPath
            };
        }

        private static List<DomainReloadOwnerTiming> AggregateOwners(
            IEnumerable<DomainReloadTimelineEvent> source, bool byScript)
        {
            return source.GroupBy(item => byScript ? item.ScriptPath : item.AssemblyName,
                    StringComparer.OrdinalIgnoreCase)
                .Select(group => new DomainReloadOwnerTiming
                {
                    Name = group.Key,
                    AssemblyName = group.Select(item => item.AssemblyName).FirstOrDefault(value => !string.IsNullOrEmpty(value)),
                    ScriptPath = byScript ? group.Key : null,
                    TotalMs = group.Sum(item => item.TotalMs),
                    MaxMs = group.Max(item => item.DurationMs),
                    EventCount = group.Sum(item => item.Count)
                })
                .OrderByDescending(item => item.TotalMs)
                .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string Categorize(string name)
        {
            if (name.StartsWith("AssemblyReloadEvents.beforeAssemblyReload", StringComparison.Ordinal))
                return "Before reload callback";
            if (name.StartsWith("DidReloadScripts", StringComparison.Ordinal))
                return "DidReloadScripts callback";
            if (name.StartsWith("AssemblyReloadEvents.afterAssemblyReload", StringComparison.Ordinal))
                return "After reload callback";
            if (name.IndexOf("InitializeOnLoad", StringComparison.OrdinalIgnoreCase) >= 0)
                return "InitializeOnLoad";
            if (name.StartsWith("EditorApplication.delayCall", StringComparison.Ordinal))
                return "Deferred callback";
            if (name == "Application.Reload" || name == "DomainReload" || name == "ScriptingFinalize" ||
                name.IndexOf("ReloadAssembly", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Unity reload phase";
            return "Editor task/marker";
        }

        private static bool TryGetApi(out MethodInfo available, out MethodInfo last, out MethodInfo total,
            out MethodInfo count, out MethodInfo timestamp, out string diagnostic)
        {
            Type type = typeof(EditorApplication).Assembly.GetType("UnityEditor.Profiling.EditorPerformanceTracker");
            available = GetMethod(type, "GetAvailableTrackers", Type.EmptyTypes);
            last = GetMethod(type, "GetLastTime", new[] { typeof(string) });
            total = GetMethod(type, "GetTotalTime", new[] { typeof(string) });
            count = GetMethod(type, "GetSampleCount", new[] { typeof(string) });
            timestamp = GetMethod(type, "GetTimestamp", new[] { typeof(string) });
            if (available != null && last != null && total != null && count != null && timestamp != null)
            {
                diagnostic = null;
                return true;
            }

            diagnostic = "This Unity version does not expose the internal Editor performance tracker API required for an ordered callback timeline.";
            return false;
        }

        private static MethodInfo GetMethod(Type type, string name, Type[] parameterTypes)
        {
            return type?.GetMethod(name, StaticFlags, null, parameterTypes, null);
        }

        private static double InvokeDouble(MethodInfo method, string name)
        {
            return Convert.ToDouble(method.Invoke(null, new object[] { name }));
        }

        private static int InvokeInt(MethodInfo method, string name)
        {
            return Convert.ToInt32(method.Invoke(null, new object[] { name }));
        }

        private static string GetBaseMessage(Exception exception)
        {
            while (exception.InnerException != null)
                exception = exception.InnerException;
            return exception.Message;
        }

        private sealed class OwnerResolver
        {
            private readonly Dictionary<string, Type> _typesByFullName;
            private readonly Dictionary<string, Type> _typesBySimpleName;
            private readonly Dictionary<string, string[]> _sourcesByAssembly;
            private readonly Dictionary<string, Dictionary<string, string>> _declaredTypesByAssembly =
                new(StringComparer.OrdinalIgnoreCase);
            private readonly UnityEditor.PackageManager.PackageInfo[] _packages;

            public OwnerResolver()
            {
                IEnumerable<Type> types = AppDomain.CurrentDomain.GetAssemblies().SelectMany(GetLoadableTypes);
                _typesByFullName = types.Where(type => !string.IsNullOrEmpty(type.FullName))
                    .GroupBy(type => type.FullName, StringComparer.Ordinal)
                    .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
                _typesBySimpleName = _typesByFullName.Values
                    .GroupBy(type => type.Name, StringComparer.Ordinal)
                    .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
                _sourcesByAssembly = CompilationPipeline.GetAssemblies(AssembliesType.Editor)
                    .GroupBy(assembly => assembly.name, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(group => group.Key, group => group.First().sourceFiles ?? Array.Empty<string>(),
                        StringComparer.OrdinalIgnoreCase);
                _packages = UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages();
            }

            public void Resolve(string tracker, out string assemblyName, out string scriptPath)
            {
                assemblyName = ExtractAssemblyName(tracker);
                scriptPath = null;
                string typeName = ExtractTypeName(tracker);
                if (string.IsNullOrEmpty(typeName))
                    return;

                if (!_typesByFullName.TryGetValue(typeName, out Type type))
                {
                    string simpleName = typeName.Substring(typeName.LastIndexOf('.') + 1);
                    _typesBySimpleName.TryGetValue(simpleName, out type);
                }
                if (type == null)
                    return;

                assemblyName = type.Assembly.GetName().Name;
                if (!_sourcesByAssembly.TryGetValue(assemblyName, out string[] sources))
                    return;
                string source = sources.FirstOrDefault(path =>
                    string.Equals(Path.GetFileNameWithoutExtension(path), type.Name, StringComparison.OrdinalIgnoreCase));
                if (string.IsNullOrEmpty(source))
                    BuildDeclaredTypeMap(assemblyName, sources).TryGetValue(type.Name, out source);
                if (!string.IsNullOrEmpty(source))
                    scriptPath = NormalizeScriptPath(source);
            }

            private Dictionary<string, string> BuildDeclaredTypeMap(string assemblyName, IEnumerable<string> sources)
            {
                if (_declaredTypesByAssembly.TryGetValue(assemblyName, out Dictionary<string, string> cached))
                    return cached;
                cached = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (string sourcePath in sources)
                {
                    try
                    {
                        string source = File.ReadAllText(sourcePath);
                        foreach (Match match in Regex.Matches(source,
                                     @"\b(?:class|struct|interface|record)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)"))
                            cached.TryAdd(match.Groups["name"].Value, sourcePath);
                    }
                    catch { /* Source mapping is best effort and must never invalidate a capture. */ }
                }
                _declaredTypesByAssembly[assemblyName] = cached;
                return cached;
            }

            private string NormalizeScriptPath(string path)
            {
                string full;
                try { full = Path.GetFullPath(path); }
                catch { return path.Replace('\\', '/'); }

                foreach (UnityEditor.PackageManager.PackageInfo package in _packages)
                {
                    if (package == null || string.IsNullOrEmpty(package.resolvedPath) || !IsUnder(full, package.resolvedPath))
                        continue;
                    string relative = Path.GetRelativePath(package.resolvedPath, full).Replace('\\', '/');
                    return (string.IsNullOrEmpty(package.assetPath) ? "Packages/" + package.name : package.assetPath)
                           .TrimEnd('/') + "/" + relative;
                }
                string projectRoot = Directory.GetParent(UnityEngine.Application.dataPath)?.FullName;
                if (!string.IsNullOrEmpty(projectRoot) && IsUnder(full, projectRoot))
                    return Path.GetRelativePath(projectRoot, full).Replace('\\', '/');
                return path.Replace('\\', '/');
            }

            private static bool IsUnder(string path, string root)
            {
                string prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                                Path.DirectorySeparatorChar;
                return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
            }

            private static IEnumerable<Type> GetLoadableTypes(System.Reflection.Assembly assembly)
            {
                try { return assembly.GetTypes(); }
                catch (ReflectionTypeLoadException ex) { return ex.Types.Where(type => type != null); }
                catch { return Array.Empty<Type>(); }
            }

            private static string ExtractAssemblyName(string tracker)
            {
                int bang = tracker.IndexOf('!');
                if (bang <= 0)
                    return null;
                string token = tracker.Substring(0, bang);
                return token.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                    ? token.Substring(0, token.Length - 4)
                    : token;
            }

            private static string ExtractTypeName(string tracker)
            {
                string owner = tracker;
                int bang = owner.IndexOf('!');
                if (bang >= 0)
                    owner = owner.Substring(bang + 1).Replace("::", ".");
                else
                {
                    int colon = owner.IndexOf(": ", StringComparison.Ordinal);
                    if (colon >= 0)
                        owner = owner.Substring(colon + 2);
                    else if (owner.StartsWith("DidReloadScripts", StringComparison.Ordinal))
                        owner = owner.Substring("DidReloadScripts".Length);
                    else
                        return null;
                }

                int parameters = owner.IndexOf('(');
                if (parameters >= 0)
                    owner = owner.Substring(0, parameters);
                int methodSeparator = owner.LastIndexOf('.');
                return methodSeparator > 0 ? owner.Substring(0, methodSeparator) : null;
            }
        }
    }
}
