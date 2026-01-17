using System.Collections.Generic;

namespace LegendaryTools.Editor
{
    public sealed class CSFilesAggregatorController
    {
        public readonly struct Options
        {
            public Options(
                bool includeSubfolders,
                bool removeUsings,
                bool stripImplementations,
                bool resolveDependencies,
                int dependencyDepth,
                int reportMaxItems)
            {
                IncludeSubfolders = includeSubfolders;
                RemoveUsings = removeUsings;
                StripImplementations = stripImplementations;
                ResolveDependencies = resolveDependencies;
                DependencyDepth = dependencyDepth;
                ReportMaxItems = reportMaxItems;
            }

            public bool IncludeSubfolders { get; }
            public bool RemoveUsings { get; }
            public bool StripImplementations { get; }
            public bool ResolveDependencies { get; }
            public int DependencyDepth { get; }
            public int ReportMaxItems { get; }
        }

        public readonly struct AggregationResult
        {
            public AggregationResult(string aggregatedText, string reportText)
            {
                AggregatedText = aggregatedText ?? string.Empty;
                ReportText = reportText ?? string.Empty;
            }

            public string AggregatedText { get; }
            public string ReportText { get; }
        }

        public AggregationResult Aggregate(IReadOnlyList<string> selectedPaths, Options options)
        {
            CSFilesAggregatorCache cache = new();

            // 1) Collect root files.
            List<string> rootFiles =
                CSFilesAggregatorDiscovery.CollectRootFiles(selectedPaths, options.IncludeSubfolders);

            if (rootFiles.Count == 0)
            {
                string emptyReport = "No .cs files found from the selected paths.";
                return new AggregationResult(string.Empty, emptyReport);
            }

            // 2) Resolve dependencies (optional).
            CSFilesAggregatorTypeIndex typeIndex = null;

            if (options.ResolveDependencies && options.DependencyDepth > 0)
                typeIndex = CSFilesAggregatorTypeIndex.BuildFromAssets(cache);

            CSFilesAggregatorDiscovery.DependencyScanResult depScan = CSFilesAggregatorDiscovery.ResolveDependencies(
                rootFiles,
                options.ResolveDependencies,
                options.DependencyDepth,
                typeIndex,
                cache);

            // 3) Build aggregated text (only code contents).
            string aggregated = CSFilesAggregatorTextBuilder.BuildAggregatedText(
                rootFiles,
                depScan,
                options.RemoveUsings,
                options.StripImplementations,
                options.ResolveDependencies,
                cache);

            // 4) Build single report prompt text.
            string report = CSFilesAggregatorUtils.BuildSinglePromptReport(
                rootFiles,
                depScan,
                options.ResolveDependencies,
                options.DependencyDepth,
                options.ReportMaxItems);

            return new AggregationResult(aggregated, report);
        }
    }
}
