// Assets/legendary-tools-common/Editor/Code/CSFilesAggregator/DependencyScan/DependencyGraphWalker.cs
using System;
using System.Collections.Generic;
using System.IO;
using LegendaryTools.CSFilesAggregator.TypeIndex;
using Microsoft.CodeAnalysis.CSharp;
using UnityEditor;

namespace LegendaryTools.CSFilesAggregator.DependencyScan
{
    /// <summary>
    /// Walks the dependency graph from one or more roots, using Roslyn syntax extraction and TypeIndex resolution.
    /// </summary>
    internal sealed class DependencyGraphWalker
    {
        private readonly RoslynSourceDependencyAnalyzer _analyzer = new RoslynSourceDependencyAnalyzer();
        private readonly TypeReferenceResolver _resolver = new TypeReferenceResolver();
        private readonly DependencyPathFilter _pathFilter = new DependencyPathFilter();

        /// <summary>
        /// Walks dependencies up to <see cref="DependencyScanSettings.MaxDepth"/> and returns dependent file paths.
        /// </summary>
        public DependencyScanResult Walk(
            ITypeIndexLookup typeIndex,
            DependencyScanRequest request,
            DependencyScanSettings settings,
            CSharpParseOptions parseOptions)
        {
            var result = new DependencyScanResult();

            if (typeIndex == null)
            {
                result.Notes.Add("Type index is null. No dependencies resolved.");
                return result;
            }

            if (request == null)
            {
                result.Notes.Add("Request is null.");
                return result;
            }

            if (settings == null)
            {
                settings = new DependencyScanSettings();
            }

            if (settings.MaxDepth < 0)
            {
                settings.MaxDepth = 0;
            }

            string projectRoot = GetProjectRootAbsoluteSafe();

            // Track inputs so we can exclude them from output if desired.
            var inputPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var workQueue = new Queue<WorkItem>(256);
            EnqueueInitialWorkItems(request, projectRoot, inputPaths, workQueue);

            var visitedSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var outputSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Candidate buffers (reused to reduce allocations).
            var candidates = new List<TypeReferenceCandidate>(512);
            var context = new SourceFileContext();

            while (workQueue.Count > 0)
            {
                WorkItem item = workQueue.Dequeue();

                if (string.IsNullOrEmpty(item.SourceIdentity))
                {
                    continue;
                }

                if (!visitedSources.Add(item.SourceIdentity))
                {
                    continue;
                }

                if (!TryGetCode(item, out string code))
                {
                    continue;
                }

                candidates.Clear();
                context.Namespace = string.Empty;
                context.Usings.Clear();
                context.UsingAliases.Clear();
                context.SourcePath = item.ProjectRelativeOrVirtualPath;

                _analyzer.CollectTypeReferenceCandidates(code, parseOptions, item.ProjectRelativeOrVirtualPath, context, candidates);

                // Depth 0 is roots. We only expand if currentDepth < MaxDepth.
                int nextDepth = item.Depth + 1;
                bool canExpand = nextDepth <= settings.MaxDepth;

                for (int i = 0; i < candidates.Count; i++)
                {
                    TypeReferenceCandidate c = candidates[i];
                    if (c == null || string.IsNullOrEmpty(c.NormalizedName))
                    {
                        continue;
                    }

                    if (!_resolver.TryResolve(typeIndex, context, c, out IReadOnlyList<TypeIndexEntry> entries))
                    {
                        if (!settings.IgnoreUnresolvedTypes)
                        {
                            result.Notes.Add($"Unresolved type '{c.NormalizedName}' in {item.ProjectRelativeOrVirtualPath}:{c.Line}:{c.Column}");
                        }

                        continue;
                    }

                    for (int e = 0; e < entries.Count; e++)
                    {
                        TypeIndexEntry entry = entries[e];
                        if (entry == null || string.IsNullOrEmpty(entry.FilePath))
                        {
                            continue;
                        }

                        string depPath = NormalizeProjectRelativePath(entry.FilePath);

                        bool isVirtualInMemory = IsVirtualInMemoryPath(request, depPath);
                        if (isVirtualInMemory && !settings.IncludeInMemoryVirtualPathsInResult)
                        {
                            continue;
                        }

                        if (!_pathFilter.ShouldInclude(depPath, settings))
                        {
                            continue;
                        }

                        bool isInput = inputPaths.Contains(depPath);
                        if (isInput && !settings.IncludeInputFilesInResult)
                        {
                            // Do not add input files to output list.
                        }
                        else if (outputSet.Add(depPath))
                        {
                            result.DependentFilePaths.Add(depPath);
                        }

                        if (canExpand)
                        {
                            // Only enqueue actual readable files from disk OR declared virtual in-memory sources.
                            if (isVirtualInMemory)
                            {
                                // Enqueue the in-memory source by identity to continue traversal.
                                WorkItem vm = WorkItem.ForInMemory(depPath, depPath, nextDepth);
                                if (!visitedSources.Contains(vm.SourceIdentity))
                                {
                                    workQueue.Enqueue(vm);
                                }
                            }
                            else
                            {
                                string abs = TryMakeAbsolutePath(projectRoot, depPath);
                                if (!string.IsNullOrEmpty(abs) && File.Exists(abs))
                                {
                                    WorkItem wf = WorkItem.ForFile(abs, depPath, nextDepth);
                                    if (!visitedSources.Contains(wf.SourceIdentity))
                                    {
                                        workQueue.Enqueue(wf);
                                    }
                                }
                            }
                        }
                    }
                }
            }

            return result;
        }

        private static void EnqueueInitialWorkItems(
            DependencyScanRequest request,
            string projectRoot,
            HashSet<string> inputPaths,
            Queue<WorkItem> workQueue)
        {
            if (request.AbsoluteFilePaths != null)
            {
                for (int i = 0; i < request.AbsoluteFilePaths.Length; i++)
                {
                    string abs = request.AbsoluteFilePaths[i];
                    if (string.IsNullOrEmpty(abs))
                    {
                        continue;
                    }

                    string absFull;
                    try
                    {
                        absFull = Path.GetFullPath(abs);
                    }
                    catch
                    {
                        continue;
                    }

                    string rel = TryGetProjectRelativeFromRequest(request, i);
                    if (string.IsNullOrEmpty(rel))
                    {
                        rel = TryToProjectRelativePath(projectRoot, absFull);
                    }

                    rel = NormalizeProjectRelativePath(rel);

                    if (!string.IsNullOrEmpty(rel))
                    {
                        inputPaths.Add(rel);
                    }

                    workQueue.Enqueue(WorkItem.ForFile(absFull, rel, depth: 0));
                }
            }

            if (request.InMemorySources != null)
            {
                for (int i = 0; i < request.InMemorySources.Length; i++)
                {
                    InMemorySource src = request.InMemorySources[i];
                    if (src == null || string.IsNullOrEmpty(src.Code))
                    {
                        continue;
                    }

                    string virtualPath = string.IsNullOrEmpty(src.VirtualProjectRelativePath)
                        ? (string.IsNullOrEmpty(src.InMemorySourceId) ? "InMemorySource" : src.InMemorySourceId)
                        : src.VirtualProjectRelativePath;

                    virtualPath = NormalizeProjectRelativePath(virtualPath);

                    if (!string.IsNullOrEmpty(virtualPath))
                    {
                        inputPaths.Add(virtualPath);
                    }

                    workQueue.Enqueue(WorkItem.ForInMemory(src.InMemorySourceId, virtualPath, depth: 0));
                }
            }
        }

        private static string TryGetProjectRelativeFromRequest(DependencyScanRequest request, int index)
        {
            if (request.ProjectRelativeFilePaths == null || index < 0 || index >= request.ProjectRelativeFilePaths.Length)
            {
                return null;
            }

            return request.ProjectRelativeFilePaths[index];
        }

        private static bool TryGetCode(WorkItem item, out string code)
        {
            code = null;

            if (item.IsInMemory)
            {
                // For virtual traversal we store the "path" as identity and later fetch from the request is not possible here.
                // The in-memory code is read by the top-level scanner and indexed into the type index;
                // for dependency traversal, virtual nodes are analyzed only if they are explicitly provided as in-memory roots.
                // Therefore: virtual nodes (dependencies) cannot be re-parsed unless they were also provided as in-memory roots.
                // Best-effort: do not parse virtual nodes unless their AbsolutePath contains code (not used here).
                if (!string.IsNullOrEmpty(item.InMemoryCode))
                {
                    code = item.InMemoryCode;
                    return true;
                }

                return false;
            }

            if (string.IsNullOrEmpty(item.AbsolutePath))
            {
                return false;
            }

            try
            {
                code = File.ReadAllText(item.AbsolutePath);
                return !string.IsNullOrEmpty(code);
            }
            catch
            {
                return false;
            }
        }

        private static string NormalizeProjectRelativePath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return path;
            }

            return path.Replace('\\', '/').Trim();
        }

        private static string TryMakeAbsolutePath(string projectRoot, string projectRelativeOrAbsolute)
        {
            if (string.IsNullOrEmpty(projectRelativeOrAbsolute))
            {
                return null;
            }

            try
            {
                if (Path.IsPathRooted(projectRelativeOrAbsolute))
                {
                    return Path.GetFullPath(projectRelativeOrAbsolute);
                }

                if (string.IsNullOrEmpty(projectRoot))
                {
                    return null;
                }

                return Path.GetFullPath(Path.Combine(projectRoot, projectRelativeOrAbsolute));
            }
            catch
            {
                return null;
            }
        }

        private static string TryToProjectRelativePath(string projectRoot, string fileAbsolutePath)
        {
            if (string.IsNullOrEmpty(projectRoot) || string.IsNullOrEmpty(fileAbsolutePath))
            {
                return fileAbsolutePath;
            }

            string fullProjectRoot;
            string fullFile;
            try
            {
                fullProjectRoot = Path.GetFullPath(projectRoot).Replace('\\', '/').TrimEnd('/');
                fullFile = Path.GetFullPath(fileAbsolutePath).Replace('\\', '/');
            }
            catch
            {
                return fileAbsolutePath;
            }

            if (fullFile.StartsWith(fullProjectRoot + "/", StringComparison.OrdinalIgnoreCase))
            {
                return fullFile.Substring(fullProjectRoot.Length + 1);
            }

            return fileAbsolutePath;
        }

        private static string GetProjectRootAbsoluteSafe()
        {
            try
            {
                // Reuse existing settings to locate project root in a Unity-safe way.
                return LegendaryTools.CSFilesAggregator.TypeIndex.TypeIndexSettings.instance.GetProjectRootAbsolute();
            }
            catch
            {
                // Fallback: best-effort.
                return Directory.GetCurrentDirectory();
            }
        }

        private static bool IsVirtualInMemoryPath(DependencyScanRequest request, string path)
        {
            if (request?.InMemorySources == null || string.IsNullOrEmpty(path))
            {
                return false;
            }

            for (int i = 0; i < request.InMemorySources.Length; i++)
            {
                InMemorySource src = request.InMemorySources[i];
                if (src == null)
                {
                    continue;
                }

                string vp = string.IsNullOrEmpty(src.VirtualProjectRelativePath)
                    ? (string.IsNullOrEmpty(src.InMemorySourceId) ? "InMemorySource" : src.InMemorySourceId)
                    : src.VirtualProjectRelativePath;

                vp = NormalizeProjectRelativePath(vp);

                if (string.Equals(vp, path, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private readonly struct WorkItem
        {
            public readonly bool IsInMemory;
            public readonly string AbsolutePath;
            public readonly string ProjectRelativeOrVirtualPath;
            public readonly string SourceIdentity;
            public readonly int Depth;

            // Optional, only used if you decide to enqueue code directly.
            public readonly string InMemoryCode;

            private WorkItem(
                bool isInMemory,
                string absolutePath,
                string projectRelativeOrVirtualPath,
                string sourceIdentity,
                int depth,
                string inMemoryCode)
            {
                IsInMemory = isInMemory;
                AbsolutePath = absolutePath;
                ProjectRelativeOrVirtualPath = projectRelativeOrVirtualPath;
                SourceIdentity = sourceIdentity;
                Depth = depth;
                InMemoryCode = inMemoryCode;
            }

            public static WorkItem ForFile(string absolutePath, string projectRelativePath, int depth)
            {
                string identity = string.IsNullOrEmpty(projectRelativePath) ? absolutePath : projectRelativePath;
                return new WorkItem(
                    isInMemory: false,
                    absolutePath: absolutePath,
                    projectRelativeOrVirtualPath: projectRelativePath,
                    sourceIdentity: identity,
                    depth: depth,
                    inMemoryCode: null);
            }

            public static WorkItem ForInMemory(string inMemoryIdOrVirtualPath, string virtualPath, int depth)
            {
                string identity = string.IsNullOrEmpty(virtualPath) ? inMemoryIdOrVirtualPath : virtualPath;
                return new WorkItem(
                    isInMemory: true,
                    absolutePath: null,
                    projectRelativeOrVirtualPath: virtualPath,
                    sourceIdentity: identity,
                    depth: depth,
                    inMemoryCode: null);
            }
        }
    }
}
