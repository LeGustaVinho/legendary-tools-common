using System.Collections.Generic;

namespace LegendaryTools.Editor.Code.CSFilesAggregator.Pipeline
{
    /// <summary>
    /// Represents a preview plan of which files will be aggregated.
    /// </summary>
    public sealed class AggregationPlan
    {
        /// <summary>
        /// Files coming from the initial selection (expanded folders + selected files).
        /// </summary>
        public IReadOnlyList<AggregationPlanFile> InputFiles { get; }

        /// <summary>
        /// Files discovered by dependency scanning (when enabled).
        /// </summary>
        public IReadOnlyList<AggregationPlanFile> DependencyFiles { get; }

        /// <summary>
        /// All unique files in the final plan (inputs + dependencies, de-duplicated).
        /// </summary>
        public IReadOnlyList<AggregationPlanFile> AllFiles { get; }

        /// <summary>
        /// Diagnostics emitted while building the plan.
        /// </summary>
        public IReadOnlyList<Diagnostic> Diagnostics { get; }

        /// <summary>
        /// Creates a new plan instance.
        /// </summary>
        public AggregationPlan(
            IReadOnlyList<AggregationPlanFile> inputFiles,
            IReadOnlyList<AggregationPlanFile> dependencyFiles,
            IReadOnlyList<AggregationPlanFile> allFiles,
            IReadOnlyList<Diagnostic> diagnostics)
        {
            InputFiles = inputFiles ?? new List<AggregationPlanFile>();
            DependencyFiles = dependencyFiles ?? new List<AggregationPlanFile>();
            AllFiles = allFiles ?? new List<AggregationPlanFile>();
            Diagnostics = diagnostics ?? new List<Diagnostic>();
        }
    }

    /// <summary>
    /// Represents a file entry in an <see cref="AggregationPlan"/>.
    /// </summary>
    public sealed class AggregationPlanFile
    {
        /// <summary>
        /// Absolute path on disk.
        /// </summary>
        public string AbsolutePath { get; }

        /// <summary>
        /// Display path (preferably project-relative, e.g. Assets/...).
        /// </summary>
        public string DisplayPath { get; }

        /// <summary>
        /// Source of this file in the plan.
        /// </summary>
        public AggregationPlanFileSource Source { get; }

        /// <summary>
        /// Creates a new plan file.
        /// </summary>
        public AggregationPlanFile(string absolutePath, string displayPath, AggregationPlanFileSource source)
        {
            AbsolutePath = absolutePath ?? string.Empty;
            DisplayPath = displayPath ?? string.Empty;
            Source = source;
        }
    }

    /// <summary>
    /// Indicates whether a file came from the initial selection or dependency scanning.
    /// </summary>
    public enum AggregationPlanFileSource
    {
        /// <summary>
        /// The file was included from selected inputs (files or expanded folders).
        /// </summary>
        Input = 0,

        /// <summary>
        /// The file was included by dependency scanning.
        /// </summary>
        Dependency = 1
    }
}