// Assets/legendary-tools-common/Editor/Code/CSFilesAggregator/DependencyScan/DependencyPathFilter.cs
using System;

namespace LegendaryTools.CSFilesAggregator.DependencyScan
{
    /// <summary>
    /// Applies project-specific filtering rules to dependency file paths.
    /// </summary>
    internal sealed class DependencyPathFilter
    {
        /// <summary>
        /// Returns true if the given project-relative path should be included in results.
        /// </summary>
        /// <param name="projectRelativePath">Project-relative path (e.g. "Assets/...").</param>
        /// <param name="settings">Filter settings.</param>
        public bool ShouldInclude(string projectRelativePath, DependencyScanSettings settings)
        {
            if (string.IsNullOrEmpty(projectRelativePath))
            {
                return false;
            }

            if (settings == null)
            {
                return true;
            }

            string p = projectRelativePath.Replace('\\', '/');

            if (settings.IgnorePackagesFolder && p.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (settings.IgnorePackageCache && p.StartsWith("Library/PackageCache/", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (settings.IgnoredPathPrefixes != null)
            {
                for (int i = 0; i < settings.IgnoredPathPrefixes.Count; i++)
                {
                    string prefix = settings.IgnoredPathPrefixes[i];
                    if (string.IsNullOrEmpty(prefix))
                    {
                        continue;
                    }

                    string normalizedPrefix = prefix.Replace('\\', '/');
                    if (!normalizedPrefix.EndsWith("/", StringComparison.Ordinal))
                    {
                        normalizedPrefix += "/";
                    }

                    if (p.StartsWith(normalizedPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }
                }
            }

            return true;
        }
    }
}
