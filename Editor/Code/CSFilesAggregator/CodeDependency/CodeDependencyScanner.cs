// Assets/legendary-tools-common/Editor/Code/CSFilesAggregator/DependencyScan/CodeDependencyScanner.cs
using System;
using LegendaryTools.CSFilesAggregator.TypeIndex;
using Microsoft.CodeAnalysis.CSharp;

namespace LegendaryTools.CSFilesAggregator.DependencyScan
{
    /// <summary>
    /// High-level orchestrator that scans C# sources and returns dependent source file paths.
    /// </summary>
    public static class CodeDependencyScanner
    {
        /// <summary>
        /// Scans dependencies for the given request and returns a result object.
        /// </summary>
        /// <param name="typeIndex">A loaded type index used to resolve types to files.</param>
        /// <param name="request">Scan request.</param>
        /// <param name="settings">Scan settings.</param>
        /// <returns>The scan result containing dependent project-relative file paths.</returns>
        public static DependencyScanResult Scan(TypeIndex.TypeIndex typeIndex, DependencyScanRequest request, DependencyScanSettings settings)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (settings == null)
            {
                settings = new DependencyScanSettings();
            }

            // Use conservative parse option.
            CSharpParseOptions parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);

            // Build an in-memory type index for any in-memory sources so dependencies can resolve to those declarations too.
            InMemoryTypeIndex inMemoryIndex = InMemoryTypeIndex.Build(request.InMemorySources, parseOptions);

            // Compose lookup: in-memory first, then persisted type index.
            ITypeIndexLookup persisted = new TypeIndexLookupAdapter(typeIndex);
            ITypeIndexLookup composite = new CompositeTypeIndexLookup(inMemoryIndex, persisted);

            var walker = new DependencyGraphWalker();
            return walker.Walk(composite, request, settings, parseOptions);
        }

        /// <summary>
        /// Scans dependencies and returns an array of dependent file paths.
        /// </summary>
        /// <param name="typeIndex">A loaded type index used to resolve types to files.</param>
        /// <param name="request">Scan request.</param>
        /// <param name="settings">Scan settings.</param>
        /// <returns>Project-relative dependent file paths.</returns>
        public static string[] ScanDependentFilePaths(TypeIndex.TypeIndex typeIndex, DependencyScanRequest request, DependencyScanSettings settings)
        {
            DependencyScanResult result = Scan(typeIndex, request, settings);
            if (result == null || result.DependentFilePaths == null || result.DependentFilePaths.Count == 0)
            {
                return Array.Empty<string>();
            }

            return result.DependentFilePaths.ToArray();
        }
    }
}
