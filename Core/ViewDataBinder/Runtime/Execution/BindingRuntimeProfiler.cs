#if UNITY_2020_2_OR_NEWER
using Unity.Profiling;
#endif

namespace LegendaryTools.ViewBinding
{
    internal static class BindingRuntimeProfiler
    {
#if UNITY_2020_2_OR_NEWER
        internal static readonly ProfilerMarker ProcessTiming =
            new ProfilerMarker("ViewDataBinder.ProcessTiming");
        internal static readonly ProfilerMarker Synchronize =
            new ProfilerMarker("ViewDataBinder.Synchronize");
        internal static readonly ProfilerMarker ResolveEndpoint =
            new ProfilerMarker("ViewDataBinder.ResolveEndpoint");
        internal static readonly ProfilerMarker Read =
            new ProfilerMarker("ViewDataBinder.Read");
        internal static readonly ProfilerMarker Convert =
            new ProfilerMarker("ViewDataBinder.Convert");
        internal static readonly ProfilerMarker Format =
            new ProfilerMarker("ViewDataBinder.Format");
        internal static readonly ProfilerMarker Write =
            new ProfilerMarker("ViewDataBinder.Write");
        internal static readonly ProfilerMarker EvaluateConditions =
            new ProfilerMarker("ViewDataEventBinder.EvaluateConditions");
        internal static readonly ProfilerMarker ObserveTasks =
            new ProfilerMarker("ViewDataEventBinder.ObserveTasks");
#endif
    }
}
