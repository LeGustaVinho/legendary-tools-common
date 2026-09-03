# Domain Reload Analyzer

Open it from `Tools > Legendary Tools > Analysis > Domain Reload Analyzer`.

## What the tool measures

- Reads the `Domain Reload Profiling` block from `Editor.log` and preserves the step tree emitted by the current Unity version without relying on a hard-coded list of step names.
- Calculates inclusive time, approximate self time and percentage of total reload time for every step.
- Associates the first `Asset Pipeline Refresh` block after the reload, separating compilation, import and reload.
- Highlights warnings, errors, duplicate assemblies and stack traces found inside the reload interval.
- Optionally enables the Profiler with `profileEditor = true`, collects relevant markers and saves a `.data` capture.
- Snapshots Unity Editor performance trackers before the reload and compares them after it. This produces an ordered timeline of Unity phases, `beforeAssemblyReload`, `DidReloadScripts`, `afterAssemblyReload`, and other instrumented Editor tasks.
- Resolves tracker owners back to their managed assembly and source script when symbol/type information is available, then builds measured per-script and per-assembly summaries.
- Persists captures under `Library/DomainReloadAnalyzer` with Newtonsoft.Json. Pending state survives Domain Reload and can help recover an interrupted reload after restarting the Editor.

## What the audit searches for

The audit scans `Assets` and every package returned by `PackageInfo.GetAllRegisteredPackages()` (Registry, Git, Local, Embedded and Built-in). It detects reload callbacks, serialization, object lifecycle methods, importers, background work, blocking waits and expensive APIs inside files that have reload context. `TypeCache` confirms callbacks found in assemblies that are actually loaded. The tool groups findings by script and assembly, lists assemblies, and counts live object types restored by the Editor.

A static finding is a lead, not a measurement. Code present in a package can be excluded by an `asmdef`, define or platform constraint; callbacks can return immediately; and a simple-looking method can delegate expensive work. Confirm the cause with log timings and, when needed, the Profiler.

## Timeline and ownership limitations

The official `Editor.log` timing tree remains the authoritative complete phase breakdown. Script-level timing is available only when Unity creates an Editor performance tracker for a callback or when code emits its own profiler marker. Static constructors processed inside `ProcessInitializeOnLoadAttributes`, object restoration, native work, and uninstrumented third-party code can remain aggregate-only.

Tracker timings are inclusive and nested tasks overlap, so script/assembly totals must not be added together or compared as percentages of total reload time. The tracker reader uses an internal Unity Editor API through guarded reflection; unsupported Unity versions keep the log parser, audit, and Profiler capture working and display a diagnostic instead of failing the capture.

## Performance characteristics

The Project Impact UI caches filtered results and displays only 50 findings per page. Finding details are collapsed by default. Assembly and live-object tables are paginated as well, so repaint cost remains bounded even on projects with thousands of findings.

The scanner builds source line indexes once per file and uses binary search to resolve match locations. The full scan still runs only when requested because walking all package sources and TypeCache is intentionally comprehensive.

## Recommended workflow

1. Make three isolated captures without the Profiler.
2. Identify the dominant step in the history.
3. Run the audit and filter by the corresponding category.
4. If the cost is in managed initializers, capture once with Deep Profiling to locate methods.
5. Fix the suspect and validate again without Deep Profiling.

Deep Profiling changes Domain Reload cost because it instruments managed calls when the new domain loads. Never use a Deep Profiling measurement as the baseline.

## Official references

- [Enter Play Mode details](https://docs.unity3d.com/Manual/ConfigurableEnterPlayModeDetails.html)
- [InitializeOnLoad](https://docs.unity3d.com/ScriptReference/InitializeOnLoadAttribute.html)
- [Profiling the Unity Editor](https://docs.unity3d.com/Manual/profiler-profiling-applications.html)
- [RawFrameDataView](https://docs.unity3d.com/ScriptReference/Profiling.RawFrameDataView.html)
- [Log files](https://docs.unity3d.com/Manual/log-files.html)
