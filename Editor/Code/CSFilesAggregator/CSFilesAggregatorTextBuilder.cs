using System.Collections.Generic;
using System.Text;

namespace LegendaryTools.Editor
{
    public static class CSFilesAggregatorTextBuilder
    {
        public static string BuildAggregatedText(
            IReadOnlyList<string> rootFiles,
            CSFilesAggregatorDiscovery.DependencyScanResult depScan,
            bool removeUsings,
            bool stripImplementations,
            bool resolveDependencies,
            CSFilesAggregatorCache cache)
        {
            StringBuilder sb = new();

            // Prevent duplicate content across multiple roots:
            // - Roots are already distinct, but we still guard.
            // - Dependencies are included only once globally.
            HashSet<string> appended = new(System.StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < rootFiles.Count; i++)
            {
                string root = CSFilesAggregatorUtils.NormalizePath(rootFiles[i]);

                // Append root once.
                if (appended.Add(root))
                {
                    string rootContent = cache.GetOrBuildProcessedForOutput(root, removeUsings, stripImplementations);
                    sb.AppendLine(rootContent);
                    sb.AppendLine();
                }

                if (!resolveDependencies)
                    continue;

                CSFilesAggregatorDiscovery.RootDependencyInfo info = depScan.PerRoot[i];

                // Append each dependency only once globally.
                foreach (string depFile in info.DependencyFiles)
                {
                    string dep = CSFilesAggregatorUtils.NormalizePath(depFile);

                    if (!appended.Add(dep))
                        continue;

                    string depContent = cache.GetOrBuildProcessedForOutput(dep, removeUsings, stripImplementations);
                    sb.AppendLine(depContent);
                    sb.AppendLine();
                }
            }

            return sb.ToString();
        }
    }
}
