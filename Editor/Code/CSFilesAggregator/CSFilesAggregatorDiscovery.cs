using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LegendaryTools.Editor
{
    public static class CSFilesAggregatorDiscovery
    {
        public readonly struct RootDependencyInfo
        {
            public RootDependencyInfo(
                string rootFile,
                List<string> dependencyFiles,
                HashSet<string> unresolvedCandidates,
                HashSet<string> ambiguousCandidates)
            {
                RootFile = rootFile;
                DependencyFiles = dependencyFiles ?? new List<string>();
                UnresolvedCandidates = unresolvedCandidates ?? new HashSet<string>();
                AmbiguousCandidates = ambiguousCandidates ?? new HashSet<string>();
            }

            public string RootFile { get; }
            public List<string> DependencyFiles { get; }
            public HashSet<string> UnresolvedCandidates { get; }
            public HashSet<string> AmbiguousCandidates { get; }
        }

        public readonly struct DependencyScanResult
        {
            public DependencyScanResult(List<RootDependencyInfo> perRoot)
            {
                PerRoot = perRoot ?? new List<RootDependencyInfo>();
            }

            public List<RootDependencyInfo> PerRoot { get; }
        }

        public static List<string> CollectRootFiles(IReadOnlyList<string> selectedPaths, bool includeSubfolders)
        {
            HashSet<string> result = new(System.StringComparer.OrdinalIgnoreCase);

            SearchOption searchOption = includeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

            foreach (string path in selectedPaths)
            {
                string abs = CSFilesAggregatorUtils.ToAbsolutePath(path);

                if (File.Exists(abs) && abs.EndsWith(".cs"))
                {
                    result.Add(CSFilesAggregatorUtils.NormalizePath(abs));
                    continue;
                }

                if (Directory.Exists(abs))
                    foreach (string f in Directory.GetFiles(abs, "*.cs", searchOption))
                    {
                        result.Add(CSFilesAggregatorUtils.NormalizePath(f));
                    }
            }

            return result.OrderBy(x => x, System.StringComparer.OrdinalIgnoreCase).ToList();
        }

        public static DependencyScanResult ResolveDependencies(
            IReadOnlyList<string> rootFiles,
            bool resolveDependencies,
            int maxDepth,
            CSFilesAggregatorTypeIndex typeIndex,
            CSFilesAggregatorCache cache)
        {
            List<RootDependencyInfo> perRoot = new();

            foreach (string root in rootFiles)
            {
                if (!resolveDependencies || maxDepth <= 0 || typeIndex == null)
                {
                    perRoot.Add(new RootDependencyInfo(root, new List<string>(), new HashSet<string>(),
                        new HashSet<string>()));
                    continue;
                }

                RootDependencyInfo info = ResolveDependenciesForRoot(root, maxDepth, typeIndex, cache);
                perRoot.Add(info);
            }

            return new DependencyScanResult(perRoot);
        }

        private static RootDependencyInfo ResolveDependenciesForRoot(
            string rootFile,
            int maxDepth,
            CSFilesAggregatorTypeIndex typeIndex,
            CSFilesAggregatorCache cache)
        {
            HashSet<string> dependencyFiles = new(System.StringComparer.OrdinalIgnoreCase);
            HashSet<string> visited = new(System.StringComparer.OrdinalIgnoreCase);

            HashSet<string> unresolved = new(System.StringComparer.Ordinal);
            HashSet<string> ambiguous = new(System.StringComparer.Ordinal);

            Queue<(string file, int depth)> queue = new();

            string root = CSFilesAggregatorUtils.NormalizePath(rootFile);
            queue.Enqueue((root, 0));
            visited.Add(root);

            while (queue.Count > 0)
            {
                (string current, int depth) = queue.Dequeue();
                if (depth >= maxDepth)
                    continue;

                if (!File.Exists(current))
                    continue;

                CSFilesAggregatorCache.FileAnalysis analysis = cache.GetOrBuildFileAnalysis(current);

                foreach (string candidate in analysis.Candidates)
                {
                    CSFilesAggregatorTypeIndex.ResolveOutcome outcome =
                        typeIndex.ResolveCandidate(candidate, analysis.Context);

                    if (outcome.Kind == CSFilesAggregatorTypeIndex.ResolveKind.NotFound)
                    {
                        unresolved.Add(candidate);
                        continue;
                    }

                    if (outcome.Kind == CSFilesAggregatorTypeIndex.ResolveKind.Ambiguous)
                    {
                        ambiguous.Add(candidate);
                        continue;
                    }

                    foreach (string resolvedFile in outcome.Files)
                    {
                        string dep = CSFilesAggregatorUtils.NormalizePath(resolvedFile);

                        if (!CSFilesAggregatorUtils.IsProjectScriptInAssets(dep))
                            continue;

                        if (string.Equals(dep, current, System.StringComparison.OrdinalIgnoreCase))
                            continue;

                        if (string.Equals(dep, root, System.StringComparison.OrdinalIgnoreCase))
                            continue;

                        if (dependencyFiles.Add(dep))
                            if (visited.Add(dep))
                                queue.Enqueue((dep, depth + 1));
                    }
                }
            }

            List<string> ordered = dependencyFiles.OrderBy(x => x, System.StringComparer.OrdinalIgnoreCase).ToList();

            return new RootDependencyInfo(root, ordered, unresolved, ambiguous);
        }
    }
}