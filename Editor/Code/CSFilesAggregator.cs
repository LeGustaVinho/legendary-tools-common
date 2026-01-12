using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace LegendaryTools.Editor
{
    public class CSFilesAggregator : EditorWindow
    {
        // List of selected folder or file paths
        private List<string> paths = new();

        // Toggle to include subfolders
        private bool includeSubfolders = false;

        // Toggle to remove 'using' declarations
        private bool removeUsings = false;

        // Toggle to resolve project dependencies (Assets-only)
        private bool resolveDependencies = false;

        // Max dependency depth (0 = only root file)
        private int dependencyDepth = 1;

        // Toggle to show dependency debug report in a prompt dialog after aggregation
        private bool showDependencyDebugReport = true;

        // Debug report limit (shown in dialog)
        private int debugReportMaxItems = 40;

        // Aggregated text from .cs files (NO metadata, only file contents)
        private string aggregatedText = "";

        // Cached type index built from Assets/**/*.cs
        private TypeIndex typeIndexCache;

        // Per-run caches (performance + consistency)
        private DependencyAnalysisCache analysisCache;

        [MenuItem("Tools/LegendaryTools/Code/C# File Aggregator")]
        public static void ShowWindow()
        {
            GetWindow<CSFilesAggregator>("C# File Aggregator");
        }

        private void OnGUI()
        {
            GUILayout.Label("Settings", EditorStyles.boldLabel);

            if (GUILayout.Button("Add Folder or File"))
            {
                string selectedPath = EditorUtility.OpenFilePanel("Select folder or .cs file", "", "cs");
                if (!string.IsNullOrEmpty(selectedPath))
                {
                    if (selectedPath.StartsWith(Application.dataPath, StringComparison.OrdinalIgnoreCase))
                        selectedPath = "Assets" + selectedPath.Substring(Application.dataPath.Length);

                    if (!paths.Contains(selectedPath))
                        paths.Add(selectedPath);
                }
            }

            Rect dropArea = GUILayoutUtility.GetRect(0.0f, 50.0f, GUILayout.ExpandWidth(true));
            GUI.Box(dropArea, "Drag and Drop Folders or .cs Files Here");

            Event evt = Event.current;
            if (evt.type == EventType.DragUpdated && dropArea.Contains(evt.mousePosition))
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                Event.current.Use();
            }
            else if (evt.type == EventType.DragPerform && dropArea.Contains(evt.mousePosition))
            {
                DragAndDrop.AcceptDrag();
                foreach (string path in DragAndDrop.paths)
                {
                    string relativePath = path;
                    if (path.StartsWith(Application.dataPath, StringComparison.OrdinalIgnoreCase))
                        relativePath = "Assets" + path.Substring(Application.dataPath.Length);

                    if (Directory.Exists(path) || (File.Exists(path) && path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)))
                    {
                        if (!paths.Contains(relativePath))
                            paths.Add(relativePath);
                    }
                }
                Event.current.Use();
            }

            GUILayout.Label("Selected Folders and Files:");
            for (int i = 0; i < paths.Count; i++)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(paths[i]);
                if (GUILayout.Button("Remove", GUILayout.MaxWidth(70)))
                {
                    paths.RemoveAt(i);
                    i--;
                }
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(10);

            includeSubfolders = EditorGUILayout.Toggle("Include subfolders", includeSubfolders);
            removeUsings = EditorGUILayout.Toggle("Remove 'using' declarations", removeUsings);

            GUILayout.Space(6);

            resolveDependencies = EditorGUILayout.Toggle("Resolve dependencies (Assets-only)", resolveDependencies);
            using (new EditorGUI.DisabledScope(!resolveDependencies))
            {
                dependencyDepth = EditorGUILayout.IntField("Dependency depth", Mathf.Max(0, dependencyDepth));
                showDependencyDebugReport = EditorGUILayout.Toggle("Show dependency debug report", showDependencyDebugReport);
                using (new EditorGUI.DisabledScope(!showDependencyDebugReport))
                {
                    debugReportMaxItems = EditorGUILayout.IntField("Debug max items", Mathf.Clamp(debugReportMaxItems, 5, 500));
                }

                EditorGUILayout.HelpBox(
                    "When enabled, dependency contents are always included. Dependencies are resolved only for types defined in Assets (project scripts).",
                    MessageType.Info);
            }

            GUILayout.Space(10);

            if (GUILayout.Button("Aggregate .cs Files"))
                AggregateCSFiles();

            GUILayout.Space(10);
            GUILayout.Label("Aggregated Content", EditorStyles.boldLabel);

            // Text area must contain only aggregated file contents, no metadata.
            aggregatedText = EditorGUILayout.TextArea(aggregatedText, GUILayout.ExpandHeight(true));
        }

        private void AggregateCSFiles()
        {
            if (paths.Count == 0)
            {
                EditorUtility.DisplayDialog("Error", "Please add at least one folder or .cs file.", "OK");
                return;
            }

            try
            {
                List<string> rootFiles = CollectRootFiles();
                if (rootFiles.Count == 0)
                {
                    EditorUtility.DisplayDialog("Error", "No .cs files found from the selected paths.", "OK");
                    return;
                }

                var fileContentCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                if (resolveDependencies)
                {
                    typeIndexCache = TypeIndex.BuildFromAssets();
                    analysisCache = new DependencyAnalysisCache();
                }
                else
                {
                    typeIndexCache = null;
                    analysisCache = null;
                }

                // Aggregated output: ONLY code contents, no comments/metadata.
                StringBuilder sbAggregated = new();

                // Debug report accumulated and shown in a dialog (NOT in aggregated text).
                StringBuilder sbDebugReport = new();

                bool hasAnyReport = false;

                foreach (string rootFile in rootFiles)
                {
                    if (!File.Exists(rootFile))
                        continue;

                    DependencyResolutionResult depResult = resolveDependencies
                        ? ResolveDependencyFiles(rootFile, dependencyDepth, typeIndexCache, analysisCache)
                        : DependencyResolutionResult.Empty;

                    if (resolveDependencies && showDependencyDebugReport)
                    {
                        string rootRel = ToProjectRelativePath(rootFile);
                        string reportBlock = BuildDebugReportBlock(rootRel, depResult, debugReportMaxItems);

                        if (!string.IsNullOrEmpty(reportBlock))
                        {
                            hasAnyReport = true;
                            sbDebugReport.AppendLine(reportBlock);
                            sbDebugReport.AppendLine();
                        }
                    }

                    // Root content
                    sbAggregated.AppendLine(ReadAndProcessFile(rootFile, fileContentCache));
                    sbAggregated.AppendLine();

                    // Dependency contents are ALWAYS included when resolveDependencies is on.
                    if (resolveDependencies && depResult.DependencyFiles.Count > 0)
                    {
                        foreach (string depFile in depResult.DependencyFiles)
                        {
                            if (!File.Exists(depFile))
                                continue;

                            sbAggregated.AppendLine(ReadAndProcessFile(depFile, fileContentCache));
                            sbAggregated.AppendLine();
                        }
                    }
                }

                aggregatedText = sbAggregated.ToString();
                EditorGUIUtility.systemCopyBuffer = aggregatedText;

                // Show debug report in a prompt (dialog), not in the text area.
                if (resolveDependencies && showDependencyDebugReport && hasAnyReport)
                {
                    ShowDebugReportDialog(sbDebugReport.ToString());
                }

                EditorUtility.DisplayDialog("Success", "The .cs files have been aggregated successfully!", "OK");
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog("Error", $"An error occurred: {ex.Message}", "OK");
            }
        }

        private static void ShowDebugReportDialog(string report)
        {
            if (string.IsNullOrEmpty(report))
                return;

            // Unity dialogs are not scroll-friendly; keep it readable and allow copying the full report.
            string title = "Dependency Debug Report";
            string message = report;

            // Some Unity versions truncate very long dialog messages; still useful.
            int choice = EditorUtility.DisplayDialogComplex(
                title,
                message,
                "OK",
                "Copy report to clipboard",
                "Cancel");

            if (choice == 1)
                EditorGUIUtility.systemCopyBuffer = report;
        }

        private static string BuildDebugReportBlock(string rootRel, DependencyResolutionResult result, int maxItems)
        {
            // Filter only for report display to reduce noise (does not affect resolution).
            List<string> unresolvedFiltered = result.UnresolvedCandidates
                .Where(ShouldShowInDebugReport)
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToList();

            List<string> ambiguousFiltered = result.AmbiguousCandidates
                .Where(ShouldShowInDebugReport)
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToList();

            // If nothing meaningful to show, return empty.
            if (unresolvedFiltered.Count == 0 && ambiguousFiltered.Count == 0)
                return string.Empty;

            var sb = new StringBuilder();

            sb.AppendLine($"Root: {rootRel}");
            sb.AppendLine($"Unresolved candidates: {result.UnresolvedCandidates.Count} (showing {Math.Min(maxItems, unresolvedFiltered.Count)})");
            sb.AppendLine($"Ambiguous candidates: {result.AmbiguousCandidates.Count} (showing {Math.Min(maxItems, ambiguousFiltered.Count)})");

            if (unresolvedFiltered.Count > 0)
            {
                sb.AppendLine("Unresolved (not found in project index):");
                int shown = 0;
                foreach (string c in unresolvedFiltered)
                {
                    sb.AppendLine($" - {c}");
                    shown++;
                    if (shown >= maxItems) break;
                }

                if (unresolvedFiltered.Count > shown)
                    sb.AppendLine($" ... ({unresolvedFiltered.Count - shown} more)");
            }

            if (ambiguousFiltered.Count > 0)
            {
                sb.AppendLine("Ambiguous (multiple matches; skipped):");
                int shown = 0;
                foreach (string c in ambiguousFiltered)
                {
                    sb.AppendLine($" - {c}");
                    shown++;
                    if (shown >= maxItems) break;
                }

                if (ambiguousFiltered.Count > shown)
                    sb.AppendLine($" ... ({ambiguousFiltered.Count - shown} more)");
            }

            return sb.ToString();
        }

        private static bool ShouldShowInDebugReport(string candidate)
        {
            if (string.IsNullOrEmpty(candidate))
                return false;

            if (candidate.StartsWith("System", StringComparison.Ordinal) ||
                candidate.StartsWith("Unity", StringComparison.Ordinal) ||
                candidate.StartsWith("Microsoft", StringComparison.Ordinal))
            {
                return false;
            }

            if (candidate.Length == 1)
                return false;

            return true;
        }

        private List<string> CollectRootFiles()
        {
            SearchOption searchOption = includeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            var result = new List<string>();

            foreach (string path in paths)
            {
                string absolutePath = ToAbsolutePath(path);

                if (File.Exists(absolutePath) && absolutePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(NormalizePath(absolutePath));
                }
                else if (Directory.Exists(absolutePath))
                {
                    string[] csFiles = Directory.GetFiles(absolutePath, "*.cs", searchOption);
                    foreach (string f in csFiles)
                        result.Add(NormalizePath(f));
                }
                else
                {
                    Debug.LogWarning($"CSFilesAggregator: Invalid path ignored: {path}");
                }
            }

            return result
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private string ReadAndProcessFile(string absolutePath, Dictionary<string, string> cache)
        {
            absolutePath = NormalizePath(absolutePath);

            if (cache.TryGetValue(absolutePath, out string cached))
                return cached;

            string fileContent = File.ReadAllText(absolutePath);

            if (removeUsings)
            {
                string[] lines = fileContent.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
                IEnumerable<string> filteredLines = lines.Where(line => !line.TrimStart().StartsWith("using ", StringComparison.Ordinal));
                fileContent = string.Join("\n", filteredLines);
            }

            cache[absolutePath] = fileContent;
            return fileContent;
        }

        private DependencyResolutionResult ResolveDependencyFiles(
            string rootFile,
            int maxDepth,
            TypeIndex typeIndex,
            DependencyAnalysisCache cache)
        {
            if (maxDepth <= 0 || typeIndex == null || cache == null)
                return DependencyResolutionResult.Empty;

            var dependencyFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var unresolved = new HashSet<string>(StringComparer.Ordinal);
            var ambiguous = new HashSet<string>(StringComparer.Ordinal);

            var queue = new Queue<(string file, int depth)>();
            queue.Enqueue((NormalizePath(rootFile), 0));
            visited.Add(NormalizePath(rootFile));

            while (queue.Count > 0)
            {
                (string current, int depth) = queue.Dequeue();
                if (depth >= maxDepth)
                    continue;

                if (!File.Exists(current))
                    continue;

                FileAnalysis analysis = cache.GetOrAnalyzeFile(current);

                foreach (string candidate in analysis.Candidates)
                {
                    TypeIndex.ResolveOutcome outcome = typeIndex.ResolveCandidate(candidate, analysis.Context);

                    if (outcome.Kind == TypeIndex.ResolveKind.NotFound)
                    {
                        unresolved.Add(candidate);
                        continue;
                    }

                    if (outcome.Kind == TypeIndex.ResolveKind.Ambiguous)
                    {
                        ambiguous.Add(candidate);
                        continue;
                    }

                    foreach (string f in outcome.Files)
                    {
                        string normalized = NormalizePath(f);

                        if (!PathPolicy.IsProjectScriptInAssets(normalized))
                            continue;

                        if (string.Equals(normalized, current, StringComparison.OrdinalIgnoreCase))
                            continue;

                        if (string.Equals(normalized, rootFile, StringComparison.OrdinalIgnoreCase))
                            continue;

                        if (dependencyFiles.Add(normalized))
                        {
                            if (visited.Add(normalized))
                                queue.Enqueue((normalized, depth + 1));
                        }
                    }
                }
            }

            return new DependencyResolutionResult(
                dependencyFiles.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList(),
                unresolved,
                ambiguous);
        }

        private string ToAbsolutePath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return path;

            if (path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("Assets\\", StringComparison.OrdinalIgnoreCase))
            {
                return NormalizePath(Path.Combine(Application.dataPath, path.Substring("Assets".Length + 1)));
            }

            return NormalizePath(path);
        }

        private static string ToProjectRelativePath(string absolutePath)
        {
            absolutePath = NormalizePath(absolutePath);
            string assetsPath = NormalizePath(Application.dataPath);

            if (absolutePath.StartsWith(assetsPath, StringComparison.OrdinalIgnoreCase))
            {
                string suffix = absolutePath.Substring(assetsPath.Length).TrimStart('/');
                return "Assets/" + suffix;
            }

            return absolutePath;
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return path;

            return path.Replace('\\', '/');
        }

        private static class PathPolicy
        {
            public static bool IsProjectScriptInAssets(string absolutePath)
            {
                if (string.IsNullOrEmpty(absolutePath))
                    return false;

                absolutePath = NormalizePath(absolutePath);

                string assetsRoot = NormalizePath(Application.dataPath);
                if (!absolutePath.StartsWith(assetsRoot, StringComparison.OrdinalIgnoreCase))
                    return false;

                if (absolutePath.Contains("/Packages/", StringComparison.OrdinalIgnoreCase))
                    return false;

                return absolutePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase);
            }
        }

        private static class CodeSanitizer
        {
            /// <summary>
            /// Removes comments and string/char literals. Preserves newlines to keep basic structure.
            /// This prevents resolving types inside comments/strings and avoids reading "using" from comments.
            /// </summary>
            public static string StripCommentsAndStrings(string code)
            {
                if (string.IsNullOrEmpty(code))
                    return string.Empty;

                var sb = new StringBuilder(code.Length);

                bool inLineComment = false;
                bool inBlockComment = false;
                bool inString = false;
                bool inVerbatimString = false;
                bool inChar = false;

                for (int i = 0; i < code.Length; i++)
                {
                    char c = code[i];
                    char next = (i + 1 < code.Length) ? code[i + 1] : '\0';

                    if (inLineComment)
                    {
                        if (c == '\n')
                        {
                            inLineComment = false;
                            sb.Append('\n');
                        }
                        else
                        {
                            sb.Append(' ');
                        }
                        continue;
                    }

                    if (inBlockComment)
                    {
                        if (c == '*' && next == '/')
                        {
                            inBlockComment = false;
                            sb.Append("  ");
                            i++;
                        }
                        else
                        {
                            sb.Append(c == '\n' ? '\n' : ' ');
                        }
                        continue;
                    }

                    if (inString)
                    {
                        if (inVerbatimString)
                        {
                            if (c == '"' && next == '"')
                            {
                                sb.Append("  ");
                                i++;
                                continue;
                            }

                            if (c == '"')
                            {
                                inString = false;
                                inVerbatimString = false;
                                sb.Append(' ');
                                continue;
                            }

                            sb.Append(c == '\n' ? '\n' : ' ');
                            continue;
                        }

                        if (c == '\\')
                        {
                            sb.Append("  ");
                            if (i + 1 < code.Length)
                            {
                                i++;
                                sb.Append(' ');
                            }
                            continue;
                        }

                        if (c == '"')
                        {
                            inString = false;
                            sb.Append(' ');
                            continue;
                        }

                        sb.Append(c == '\n' ? '\n' : ' ');
                        continue;
                    }

                    if (inChar)
                    {
                        if (c == '\\')
                        {
                            sb.Append("  ");
                            if (i + 1 < code.Length)
                            {
                                i++;
                                sb.Append(' ');
                            }
                            continue;
                        }

                        if (c == '\'')
                        {
                            inChar = false;
                            sb.Append(' ');
                            continue;
                        }

                        sb.Append(c == '\n' ? '\n' : ' ');
                        continue;
                    }

                    if (c == '/' && next == '/')
                    {
                        inLineComment = true;
                        sb.Append("  ");
                        i++;
                        continue;
                    }

                    if (c == '/' && next == '*')
                    {
                        inBlockComment = true;
                        sb.Append("  ");
                        i++;
                        continue;
                    }

                    if (c == '"')
                    {
                        inString = true;
                        inVerbatimString = (i > 0 && code[i - 1] == '@');
                        sb.Append(' ');
                        continue;
                    }

                    if (c == '\'')
                    {
                        inChar = true;
                        sb.Append(' ');
                        continue;
                    }

                    sb.Append(c);
                }

                return sb.ToString();
            }
        }

        // ----------------------------
        // Dependency results
        // ----------------------------
        private readonly struct DependencyResolutionResult
        {
            public static DependencyResolutionResult Empty =>
                new(new List<string>(), new HashSet<string>(StringComparer.Ordinal), new HashSet<string>(StringComparer.Ordinal));

            public DependencyResolutionResult(List<string> dependencyFiles, HashSet<string> unresolvedCandidates, HashSet<string> ambiguousCandidates)
            {
                DependencyFiles = dependencyFiles ?? new List<string>();
                UnresolvedCandidates = unresolvedCandidates ?? new HashSet<string>(StringComparer.Ordinal);
                AmbiguousCandidates = ambiguousCandidates ?? new HashSet<string>(StringComparer.Ordinal);
            }

            public List<string> DependencyFiles { get; }
            public HashSet<string> UnresolvedCandidates { get; }
            public HashSet<string> AmbiguousCandidates { get; }
        }

        // ----------------------------
        // Per-run file analysis cache
        // ----------------------------
        private sealed class DependencyAnalysisCache
        {
            private readonly Dictionary<string, FileAnalysis> fileAnalysisCache = new(StringComparer.OrdinalIgnoreCase);
            private readonly Dictionary<string, string> sanitizedCache = new(StringComparer.OrdinalIgnoreCase);
            private readonly Dictionary<string, string> rawCache = new(StringComparer.OrdinalIgnoreCase);

            public FileAnalysis GetOrAnalyzeFile(string absolutePath)
            {
                absolutePath = NormalizePath(absolutePath);

                if (fileAnalysisCache.TryGetValue(absolutePath, out FileAnalysis analysis))
                    return analysis;

                string raw = GetOrReadRaw(absolutePath);
                string sanitized = GetOrBuildSanitized(absolutePath, raw);

                FileContext context = FileContextParser.Parse(sanitized);
                HashSet<string> candidates = TypeReferenceExtractor.ExtractCandidateTypeNames(sanitized);

                analysis = new FileAnalysis(context, candidates);

                fileAnalysisCache[absolutePath] = analysis;
                return analysis;
            }

            private string GetOrReadRaw(string absolutePath)
            {
                if (rawCache.TryGetValue(absolutePath, out string raw))
                    return raw;

                raw = File.ReadAllText(absolutePath);
                rawCache[absolutePath] = raw;
                return raw;
            }

            private string GetOrBuildSanitized(string absolutePath, string raw)
            {
                if (sanitizedCache.TryGetValue(absolutePath, out string sanitized))
                    return sanitized;

                sanitized = CodeSanitizer.StripCommentsAndStrings(raw);
                sanitizedCache[absolutePath] = sanitized;
                return sanitized;
            }
        }

        private readonly struct FileAnalysis
        {
            public FileAnalysis(FileContext context, HashSet<string> candidates)
            {
                Context = context;
                Candidates = candidates ?? new HashSet<string>(StringComparer.Ordinal);
            }

            public FileContext Context { get; }
            public HashSet<string> Candidates { get; }
        }

        // ----------------------------
        // File context (namespace + usings) - parse header only
        // ----------------------------
        private readonly struct FileContext
        {
            public FileContext(string @namespace, HashSet<string> usingNamespaces, Dictionary<string, string> usingAliases)
            {
                Namespace = @namespace ?? string.Empty;
                UsingNamespaces = usingNamespaces ?? new HashSet<string>(StringComparer.Ordinal);
                UsingAliases = usingAliases ?? new Dictionary<string, string>(StringComparer.Ordinal);
            }

            public string Namespace { get; }
            public HashSet<string> UsingNamespaces { get; }
            public Dictionary<string, string> UsingAliases { get; }
        }

        private static class FileContextParser
        {
            private static readonly Regex UsingNamespaceRegex =
                new(@"^\s*using\s+(?:static\s+)?([A-Za-z_][A-Za-z0-9_\.]*)\s*;\s*$", RegexOptions.Compiled | RegexOptions.Multiline);

            private static readonly Regex UsingAliasRegex =
                new(@"^\s*using\s+([A-Za-z_][A-Za-z0-9_]*)\s*=\s*([A-Za-z_][A-Za-z0-9_\.]*)\s*;\s*$", RegexOptions.Compiled | RegexOptions.Multiline);

            private static readonly Regex NamespaceRegex =
                new(@"\bnamespace\s+([A-Za-z_][A-Za-z0-9_\.]*)\b", RegexOptions.Compiled);

            private static readonly Regex FirstTypeKeywordRegex =
                new(@"\b(class|struct|interface|enum|delegate)\b", RegexOptions.Compiled);

            public static FileContext Parse(string sanitizedCode)
            {
                if (string.IsNullOrEmpty(sanitizedCode))
                    return new FileContext(string.Empty, new HashSet<string>(StringComparer.Ordinal), new Dictionary<string, string>(StringComparer.Ordinal));

                int headerEnd = FindHeaderEndIndex(sanitizedCode);
                string header = sanitizedCode.Substring(0, headerEnd);

                var usingNamespaces = new HashSet<string>(StringComparer.Ordinal);
                var usingAliases = new Dictionary<string, string>(StringComparer.Ordinal);

                foreach (Match m in UsingNamespaceRegex.Matches(header))
                {
                    if (!m.Success) continue;
                    string ns = m.Groups[1].Value;
                    if (!string.IsNullOrEmpty(ns))
                        usingNamespaces.Add(ns);
                }

                foreach (Match m in UsingAliasRegex.Matches(header))
                {
                    if (!m.Success) continue;
                    string alias = m.Groups[1].Value;
                    string target = m.Groups[2].Value;
                    if (!string.IsNullOrEmpty(alias) && !string.IsNullOrEmpty(target))
                        usingAliases[alias] = target;
                }

                string fileNs = string.Empty;
                Match nsMatch = NamespaceRegex.Match(sanitizedCode);
                if (nsMatch.Success)
                    fileNs = nsMatch.Groups[1].Value;

                return new FileContext(fileNs, usingNamespaces, usingAliases);
            }

            private static int FindHeaderEndIndex(string code)
            {
                int typeIdx = IndexOfRegex(code, FirstTypeKeywordRegex);
                int nsIdx = IndexOfRegex(code, NamespaceRegex);

                int idx = -1;
                if (typeIdx >= 0 && nsIdx >= 0) idx = Math.Min(typeIdx, nsIdx);
                else if (typeIdx >= 0) idx = typeIdx;
                else if (nsIdx >= 0) idx = nsIdx;

                return idx < 0 ? code.Length : Math.Max(0, idx);
            }

            private static int IndexOfRegex(string text, Regex regex)
            {
                Match m = regex.Match(text);
                return m.Success ? m.Index : -1;
            }
        }

        // ----------------------------
        // Type index
        // ----------------------------
        private sealed class TypeIndex
        {
            public enum ResolveKind
            {
                Resolved,
                NotFound,
                Ambiguous
            }

            public readonly struct ResolveOutcome
            {
                public ResolveOutcome(ResolveKind kind, List<string> files)
                {
                    Kind = kind;
                    Files = files ?? new List<string>();
                }

                public ResolveKind Kind { get; }
                public List<string> Files { get; }
            }

            private readonly Dictionary<string, List<TypeDefinition>> shortNameToDefs =
                new(StringComparer.Ordinal);

            private readonly Dictionary<string, List<TypeDefinition>> qualifiedNameToDefs =
                new(StringComparer.Ordinal);

            private readonly struct TypeDefinition
            {
                public TypeDefinition(string @namespace, string file)
                {
                    Namespace = @namespace ?? string.Empty;
                    File = file ?? string.Empty;
                }

                public string Namespace { get; }
                public string File { get; }
            }

            private static readonly Regex NamespaceRegex =
                new(@"\bnamespace\s+([A-Za-z_][A-Za-z0-9_\.]*)\b", RegexOptions.Compiled);

            private static readonly Regex TypeDeclRegex =
                new(@"\b(class|struct|interface|enum)\s+([A-Za-z_][A-Za-z0-9_]*)\b", RegexOptions.Compiled);

            public static TypeIndex BuildFromAssets()
            {
                var index = new TypeIndex();

                string assetsRoot = NormalizePath(Application.dataPath);
                if (!Directory.Exists(assetsRoot))
                    return index;

                string[] files = Directory.GetFiles(assetsRoot, "*.cs", SearchOption.AllDirectories);
                foreach (string f in files)
                {
                    string normalized = NormalizePath(f);

                    if (!PathPolicy.IsProjectScriptInAssets(normalized))
                        continue;

                    if (normalized.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase))
                        continue;

                    try
                    {
                        string raw = File.ReadAllText(normalized);
                        string sanitized = CodeSanitizer.StripCommentsAndStrings(raw);

                        string ns = ExtractNamespace(sanitized);

                        foreach (Match m in TypeDeclRegex.Matches(sanitized))
                        {
                            string typeName = m.Groups[2].Value;
                            index.AddShort(typeName, ns, normalized);

                            if (!string.IsNullOrEmpty(ns))
                                index.AddQualified(ns + "." + typeName, ns, normalized);
                        }
                    }
                    catch
                    {
                        // Ignore unreadable files.
                    }
                }

                return index;
            }

            public ResolveOutcome ResolveCandidate(string candidate, FileContext context)
            {
                if (string.IsNullOrEmpty(candidate))
                    return new ResolveOutcome(ResolveKind.NotFound, new List<string>());

                if (context.UsingAliases.TryGetValue(candidate, out string aliasTarget))
                {
                    List<string> aliasFiles = ResolveQualified(aliasTarget);
                    if (aliasFiles.Count > 0)
                        return new ResolveOutcome(ResolveKind.Resolved, aliasFiles);
                }

                if (candidate.Contains('.', StringComparison.Ordinal))
                {
                    List<string> qualifiedFiles = ResolveQualified(candidate);
                    return qualifiedFiles.Count > 0
                        ? new ResolveOutcome(ResolveKind.Resolved, qualifiedFiles)
                        : new ResolveOutcome(ResolveKind.NotFound, new List<string>());
                }

                if (!shortNameToDefs.TryGetValue(candidate, out List<TypeDefinition> defs) || defs.Count == 0)
                    return new ResolveOutcome(ResolveKind.NotFound, new List<string>());

                if (defs.Count == 1)
                    return new ResolveOutcome(ResolveKind.Resolved, new List<string> { defs[0].File });

                if (!string.IsNullOrEmpty(context.Namespace))
                {
                    var sameNs = defs.Where(d => string.Equals(d.Namespace, context.Namespace, StringComparison.Ordinal)).ToList();
                    if (sameNs.Count == 1)
                        return new ResolveOutcome(ResolveKind.Resolved, new List<string> { sameNs[0].File });
                }

                foreach (string usingNs in context.UsingNamespaces)
                {
                    List<string> viaUsing = ResolveQualified(usingNs + "." + candidate);
                    if (viaUsing.Count == 1)
                        return new ResolveOutcome(ResolveKind.Resolved, viaUsing);
                }

                var global = defs.Where(d => string.IsNullOrEmpty(d.Namespace)).ToList();
                if (global.Count == 1)
                    return new ResolveOutcome(ResolveKind.Resolved, new List<string> { global[0].File });

                return new ResolveOutcome(ResolveKind.Ambiguous, new List<string>());
            }

            private List<string> ResolveQualified(string qualified)
            {
                if (!qualifiedNameToDefs.TryGetValue(qualified, out List<TypeDefinition> defs) || defs.Count == 0)
                    return new List<string>();

                return defs.Select(d => d.File)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            private void AddShort(string shortName, string ns, string file)
            {
                if (!shortNameToDefs.TryGetValue(shortName, out var list))
                {
                    list = new List<TypeDefinition>();
                    shortNameToDefs[shortName] = list;
                }

                if (!list.Any(d => string.Equals(d.File, file, StringComparison.OrdinalIgnoreCase)))
                    list.Add(new TypeDefinition(ns, file));
            }

            private void AddQualified(string qualifiedName, string ns, string file)
            {
                if (!qualifiedNameToDefs.TryGetValue(qualifiedName, out var list))
                {
                    list = new List<TypeDefinition>();
                    qualifiedNameToDefs[qualifiedName] = list;
                }

                if (!list.Any(d => string.Equals(d.File, file, StringComparison.OrdinalIgnoreCase)))
                    list.Add(new TypeDefinition(ns, file));
            }

            private static string ExtractNamespace(string sanitizedCode)
            {
                Match m = NamespaceRegex.Match(sanitizedCode);
                return m.Success ? m.Groups[1].Value : string.Empty;
            }
        }

        // ----------------------------
        // Type reference extraction (same as your corrected version)
        // ----------------------------
        private static class TypeReferenceExtractor
        {
            private static readonly Regex QualifiedIdentifierRegex =
                new(@"\b[A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)*\b", RegexOptions.Compiled);

            private static readonly Regex NewTypeRegex =
                new(@"\bnew\s+([A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)*)\b", RegexOptions.Compiled);

            private static readonly Regex TypeofRegex =
                new(@"\btypeof\s*\(\s*([A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)*)", RegexOptions.Compiled);

            private static readonly Regex NameofRegex =
                new(@"\bnameof\s*\(\s*([A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)*)", RegexOptions.Compiled);

            private static readonly Regex CastRegex =
                new(@"\(\s*([A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)*)\s*(?:<|\)|\s)", RegexOptions.Compiled);

            private static readonly Regex AsRegex =
                new(@"\bas\s+([A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)*)\b", RegexOptions.Compiled);

            private static readonly Regex IsRegex =
                new(@"\bis\s+([A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)*)\b", RegexOptions.Compiled);

            private static readonly Regex CatchTypeRegex =
                new(@"\bcatch\s*\(\s*([A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)*)\s+[A-Za-z_][A-Za-z0-9_]*\s*\)",
                    RegexOptions.Compiled);

            private static readonly Regex ForeachTypeRegex =
                new(@"\bforeach\s*\(\s*([A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)*)\s+[A-Za-z_][A-Za-z0-9_]*\s+in\b",
                    RegexOptions.Compiled);

            private static readonly Regex UsingVarTypeRegex =
                new(@"\busing\s*\(\s*([A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)*)\s+[A-Za-z_][A-Za-z0-9_]*\s*=",
                    RegexOptions.Compiled);

            private static readonly Regex EventTypeRegex =
                new(@"\bevent\s+([A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)*)\s+[A-Za-z_][A-Za-z0-9_]*\b",
                    RegexOptions.Compiled);

            private static readonly Regex AttributeBracketRegex =
                new(@"\[\s*([^\]]+)\]", RegexOptions.Compiled);

            private static readonly Regex TypeDeclWithBasesRegex =
                new(@"\b(class|struct|interface)\s+[A-Za-z_][A-Za-z0-9_]*\s*:\s*([^{\n\r]+)", RegexOptions.Compiled);

            private static readonly Regex WhereConstraintRegex =
                new(@"\bwhere\s+[A-Za-z_][A-Za-z0-9_]*\s*:\s*([^{\n\r]+)", RegexOptions.Compiled);

            private static readonly Regex DeclarationTypeRegex =
                new(@"\b([A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)*)\s+[A-Za-z_][A-Za-z0-9_]*\s*(?:[;=,{(]|=>)",
                    RegexOptions.Compiled);

            private static readonly Regex MethodSignatureLineRegex =
                new(@"^\s*(?:public|private|protected|internal|static|virtual|override|abstract|sealed|async|extern|new|\s)+\s*([A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)*)\s+[A-Za-z_][A-Za-z0-9_]*\s*\(([^)]*)\)\s*(?:\{|=>|where|\r?$)",
                    RegexOptions.Compiled | RegexOptions.Multiline);

            private static readonly Regex DelegateSignatureLineRegex =
                new(@"^\s*delegate\s+([A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)*)\s+[A-Za-z_][A-Za-z0-9_]*\s*\(([^)]*)\)\s*;",
                    RegexOptions.Compiled | RegexOptions.Multiline);

            public static HashSet<string> ExtractCandidateTypeNames(string sanitizedCode)
            {
                var result = new HashSet<string>(StringComparer.Ordinal);

                if (string.IsNullOrEmpty(sanitizedCode))
                    return result;

                AddTypeMatches(result, NewTypeRegex, sanitizedCode);
                AddTypeMatches(result, TypeofRegex, sanitizedCode);
                AddTypeMatches(result, NameofRegex, sanitizedCode);
                AddTypeMatches(result, AsRegex, sanitizedCode);
                AddTypeMatches(result, IsRegex, sanitizedCode);
                AddTypeMatches(result, CastRegex, sanitizedCode);

                AddTypeMatches(result, CatchTypeRegex, sanitizedCode);
                AddTypeMatches(result, ForeachTypeRegex, sanitizedCode);
                AddTypeMatches(result, UsingVarTypeRegex, sanitizedCode);
                AddTypeMatches(result, EventTypeRegex, sanitizedCode);

                ExtractBaseLists(result, sanitizedCode);
                ExtractWhereConstraints(result, sanitizedCode);
                ExtractAttributes(result, sanitizedCode);

                ExtractGenericFromContextMatches(result, sanitizedCode);

                ExtractMethodLikeSignatures(result, sanitizedCode);

                ExtractDeclarationTypes(result, sanitizedCode);

                RemoveNoise(result);
                return result;
            }

            private static void AddTypeMatches(HashSet<string> result, Regex regex, string code)
            {
                foreach (Match m in regex.Matches(code))
                {
                    if (!m.Success || m.Groups.Count < 2)
                        continue;

                    AddType(result, m.Groups[1].Value);
                }
            }

            private static void ExtractDeclarationTypes(HashSet<string> result, string code)
            {
                foreach (Match m in DeclarationTypeRegex.Matches(code))
                {
                    if (!m.Success || m.Groups.Count < 2)
                        continue;

                    AddType(result, m.Groups[1].Value);
                }
            }

            private static void ExtractMethodLikeSignatures(HashSet<string> result, string code)
            {
                foreach (Match m in MethodSignatureLineRegex.Matches(code))
                {
                    if (!m.Success || m.Groups.Count < 3)
                        continue;

                    AddType(result, m.Groups[1].Value);
                    ExtractParameterTypes(result, m.Groups[2].Value);
                }

                foreach (Match m in DelegateSignatureLineRegex.Matches(code))
                {
                    if (!m.Success || m.Groups.Count < 3)
                        continue;

                    AddType(result, m.Groups[1].Value);
                    ExtractParameterTypes(result, m.Groups[2].Value);
                }
            }

            private static void ExtractParameterTypes(HashSet<string> result, string paramList)
            {
                if (string.IsNullOrWhiteSpace(paramList))
                    return;

                foreach (string part in SplitTopLevelCommaList(paramList))
                {
                    string p = part.Trim();
                    if (p.Length == 0)
                        continue;

                    p = RemoveLeadingToken(p, "this");
                    p = RemoveLeadingToken(p, "in");
                    p = RemoveLeadingToken(p, "ref");
                    p = RemoveLeadingToken(p, "out");
                    p = RemoveLeadingToken(p, "params");

                    p = StripLeadingAttributes(p);

                    string typeName = ExtractLeadingTypeName(p);
                    if (!string.IsNullOrEmpty(typeName))
                        AddType(result, typeName);

                    foreach (string argType in GenericParsing.ExtractTypesFromGenericText(p))
                        AddType(result, argType);
                }
            }

            private static string StripLeadingAttributes(string text)
            {
                while (true)
                {
                    text = text.TrimStart();
                    if (!text.StartsWith("[", StringComparison.Ordinal))
                        return text;

                    int end = FindMatchingBracket(text, 0, '[', ']');
                    if (end < 0)
                        return text;

                    text = text.Substring(end + 1);
                }
            }

            private static int FindMatchingBracket(string text, int openIndex, char open, char close)
            {
                int depth = 0;
                for (int i = openIndex; i < text.Length; i++)
                {
                    if (text[i] == open) depth++;
                    else if (text[i] == close)
                    {
                        depth--;
                        if (depth == 0) return i;
                    }
                }
                return -1;
            }

            private static string RemoveLeadingToken(string text, string token)
            {
                text = text.TrimStart();
                if (text.StartsWith(token + " ", StringComparison.Ordinal))
                    return text.Substring(token.Length + 1).TrimStart();
                return text;
            }

            private static void ExtractBaseLists(HashSet<string> result, string code)
            {
                foreach (Match m in TypeDeclWithBasesRegex.Matches(code))
                {
                    if (m.Groups.Count < 3)
                        continue;

                    string baseList = m.Groups[2].Value;
                    foreach (string part in SplitTopLevelCommaList(baseList))
                    {
                        string candidate = part.Trim();
                        if (candidate.Length == 0)
                            continue;

                        string typeName = ExtractLeadingTypeName(candidate);
                        if (!string.IsNullOrEmpty(typeName))
                            AddType(result, typeName);

                        foreach (string argType in GenericParsing.ExtractTypesFromGenericText(candidate))
                            AddType(result, argType);
                    }
                }
            }

            private static void ExtractWhereConstraints(HashSet<string> result, string code)
            {
                foreach (Match m in WhereConstraintRegex.Matches(code))
                {
                    if (m.Groups.Count < 2)
                        continue;

                    string list = m.Groups[1].Value;

                    foreach (string part in SplitTopLevelCommaList(list))
                    {
                        string candidate = part.Trim();
                        if (candidate.Length == 0)
                            continue;

                        if (candidate.Equals("new()", StringComparison.Ordinal) ||
                            candidate.Equals("struct", StringComparison.Ordinal) ||
                            candidate.Equals("class", StringComparison.Ordinal) ||
                            candidate.Equals("notnull", StringComparison.Ordinal) ||
                            candidate.Equals("unmanaged", StringComparison.Ordinal))
                        {
                            continue;
                        }

                        string typeName = ExtractLeadingTypeName(candidate);
                        if (!string.IsNullOrEmpty(typeName))
                            AddType(result, typeName);

                        foreach (string argType in GenericParsing.ExtractTypesFromGenericText(candidate))
                            AddType(result, argType);
                    }
                }
            }

            private static void ExtractAttributes(HashSet<string> result, string code)
            {
                foreach (Match m in AttributeBracketRegex.Matches(code))
                {
                    if (!m.Success || m.Groups.Count < 2)
                        continue;

                    string inside = m.Groups[1].Value;

                    foreach (string part in SplitTopLevelCommaList(inside))
                    {
                        string trimmed = part.Trim();
                        if (trimmed.Length == 0)
                            continue;

                        string attrName = ExtractLeadingTypeName(trimmed);
                        if (attrName.Length == 0)
                            continue;

                        AddAttributeVariants(result, attrName);

                        foreach (string argType in GenericParsing.ExtractTypesFromGenericText(trimmed))
                            AddType(result, argType);
                    }
                }
            }

            private static void ExtractGenericFromContextMatches(HashSet<string> result, string code)
            {
                ExtractGenericsAfterKeyword(result, code, "new");
                ExtractGenericsAfterKeyword(result, code, "typeof");
                ExtractGenericsAfterKeyword(result, code, "nameof");
                ExtractGenericsAfterKeyword(result, code, "as");
                ExtractGenericsAfterKeyword(result, code, "is");
                ExtractGenericsAfterKeyword(result, code, "catch");
                ExtractGenericsAfterKeyword(result, code, "foreach");
                ExtractGenericsAfterKeyword(result, code, "using");
                ExtractGenericsAfterKeyword(result, code, "event");
                ExtractGenericsInAttributeBrackets(result, code);
                ExtractGenericsInBaseLists(result, code);
                ExtractGenericsInWhereClauses(result, code);
            }

            private static void ExtractGenericsAfterKeyword(HashSet<string> result, string code, string keyword)
            {
                int idx = 0;
                while (idx < code.Length)
                {
                    idx = code.IndexOf(keyword, idx, StringComparison.Ordinal);
                    if (idx < 0)
                        break;

                    int windowStart = idx + keyword.Length;
                    int windowLen = Math.Min(256, code.Length - windowStart);
                    if (windowLen > 0)
                    {
                        string window = code.Substring(windowStart, windowLen);
                        foreach (var seg in GenericParsing.EnumerateGenericSegments(window))
                        {
                            AddType(result, seg.OuterTypeName);
                            foreach (string t in seg.InnerTypeNames)
                                AddType(result, t);
                        }
                    }

                    idx += keyword.Length;
                }
            }

            private static void ExtractGenericsInAttributeBrackets(HashSet<string> result, string code)
            {
                foreach (Match m in AttributeBracketRegex.Matches(code))
                {
                    if (!m.Success || m.Groups.Count < 2)
                        continue;

                    string inside = m.Groups[1].Value;
                    foreach (var seg in GenericParsing.EnumerateGenericSegments(inside))
                    {
                        AddType(result, seg.OuterTypeName);
                        foreach (string t in seg.InnerTypeNames)
                            AddType(result, t);
                    }
                }
            }

            private static void ExtractGenericsInBaseLists(HashSet<string> result, string code)
            {
                foreach (Match m in TypeDeclWithBasesRegex.Matches(code))
                {
                    if (!m.Success || m.Groups.Count < 3)
                        continue;

                    string baseList = m.Groups[2].Value;
                    foreach (var seg in GenericParsing.EnumerateGenericSegments(baseList))
                    {
                        AddType(result, seg.OuterTypeName);
                        foreach (string t in seg.InnerTypeNames)
                            AddType(result, t);
                    }
                }
            }

            private static void ExtractGenericsInWhereClauses(HashSet<string> result, string code)
            {
                foreach (Match m in WhereConstraintRegex.Matches(code))
                {
                    if (!m.Success || m.Groups.Count < 2)
                        continue;

                    string list = m.Groups[1].Value;
                    foreach (var seg in GenericParsing.EnumerateGenericSegments(list))
                    {
                        AddType(result, seg.OuterTypeName);
                        foreach (string t in seg.InnerTypeNames)
                            AddType(result, t);
                    }
                }
            }

            private static void AddType(HashSet<string> result, string typeName)
            {
                if (string.IsNullOrWhiteSpace(typeName))
                    return;

                string cleaned = CleanupTypeToken(typeName);
                if (cleaned.Length == 0)
                    return;

                result.Add(cleaned);
            }

            private static void AddAttributeVariants(HashSet<string> result, string attrName)
            {
                string cleaned = CleanupTypeToken(attrName);
                if (cleaned.Length == 0)
                    return;

                result.Add(cleaned);

                if (cleaned.EndsWith("Attribute", StringComparison.Ordinal))
                {
                    string withoutSuffix = cleaned.Substring(0, cleaned.Length - "Attribute".Length);
                    if (withoutSuffix.Length > 0)
                        result.Add(withoutSuffix);
                }
                else
                {
                    result.Add(cleaned + "Attribute");
                }
            }

            private static string CleanupTypeToken(string token)
            {
                token = token.Trim();
                token = token.TrimEnd('?');

                Match m = QualifiedIdentifierRegex.Match(token);
                return m.Success ? m.Value : string.Empty;
            }

            private static void RemoveNoise(HashSet<string> set)
            {
                set.RemoveWhere(IsNoiseToken);
            }

            private static bool IsNoiseToken(string token)
            {
                if (string.IsNullOrEmpty(token))
                    return true;

                if (token.Length < 2)
                    return true;

                switch (token)
                {
                    case "void":
                    case "bool":
                    case "byte":
                    case "sbyte":
                    case "short":
                    case "ushort":
                    case "int":
                    case "uint":
                    case "long":
                    case "ulong":
                    case "float":
                    case "double":
                    case "decimal":
                    case "char":
                    case "string":
                    case "object":
                    case "var":
                    case "dynamic":
                    case "nint":
                    case "nuint":
                    case "class":
                    case "struct":
                    case "interface":
                    case "enum":
                    case "namespace":
                    case "public":
                    case "private":
                    case "protected":
                    case "internal":
                    case "static":
                    case "readonly":
                    case "const":
                    case "sealed":
                    case "partial":
                    case "virtual":
                    case "override":
                    case "abstract":
                    case "new":
                    case "return":
                    case "this":
                    case "base":
                    case "if":
                    case "else":
                    case "for":
                    case "foreach":
                    case "while":
                    case "do":
                    case "switch":
                    case "case":
                    case "default":
                    case "break":
                    case "continue":
                    case "try":
                    case "catch":
                    case "finally":
                    case "throw":
                    case "using":
                    case "typeof":
                    case "nameof":
                    case "is":
                    case "as":
                    case "get":
                    case "set":
                    case "add":
                    case "remove":
                    case "null":
                    case "true":
                    case "false":
                    case "where":
                    case "event":
                    case "delegate":
                        return true;

                    default:
                        return false;
                }
            }

            private static string ExtractLeadingTypeName(string text)
            {
                if (string.IsNullOrWhiteSpace(text))
                    return string.Empty;

                text = text.Trim();

                if (text.StartsWith("in ", StringComparison.Ordinal))
                    text = text.Substring(3).Trim();
                else if (text.StartsWith("ref ", StringComparison.Ordinal))
                    text = text.Substring(4).Trim();
                else if (text.StartsWith("out ", StringComparison.Ordinal))
                    text = text.Substring(4).Trim();

                Match m = QualifiedIdentifierRegex.Match(text);
                return m.Success ? m.Value : string.Empty;
            }

            private static IEnumerable<string> SplitTopLevelCommaList(string text)
            {
                if (string.IsNullOrEmpty(text))
                    yield break;

                int depth = 0;
                int start = 0;

                for (int i = 0; i < text.Length; i++)
                {
                    char c = text[i];
                    if (c == '<')
                        depth++;
                    else if (c == '>')
                        depth = Math.Max(0, depth - 1);
                    else if (c == ',' && depth == 0)
                    {
                        yield return text.Substring(start, i - start);
                        start = i + 1;
                    }
                }

                if (start < text.Length)
                    yield return text.Substring(start);
            }

            private static class GenericParsing
            {
                internal readonly struct GenericSegment
                {
                    public GenericSegment(string outerTypeName, List<string> innerTypeNames)
                    {
                        OuterTypeName = outerTypeName;
                        InnerTypeNames = innerTypeNames;
                    }

                    public string OuterTypeName { get; }
                    public List<string> InnerTypeNames { get; }
                }

                public static IEnumerable<GenericSegment> EnumerateGenericSegments(string code)
                {
                    if (string.IsNullOrEmpty(code))
                        yield break;

                    for (int i = 0; i < code.Length; i++)
                    {
                        if (!IsIdentifierStart(code[i]))
                            continue;

                        int idStart = i;
                        int idEnd = i;

                        while (idEnd < code.Length && IsQualifiedIdentifierChar(code[idEnd]))
                            idEnd++;

                        string outer = code.Substring(idStart, idEnd - idStart);

                        int j = idEnd;
                        while (j < code.Length && char.IsWhiteSpace(code[j]))
                            j++;

                        if (j >= code.Length || code[j] != '<')
                        {
                            i = idEnd - 1;
                            continue;
                        }

                        int ltIndex = j;
                        if (!TryFindMatchingAngleBracket(code, ltIndex, out int gtIndex))
                        {
                            i = idEnd - 1;
                            continue;
                        }

                        string inside = code.Substring(ltIndex + 1, gtIndex - ltIndex - 1);
                        List<string> innerTypes = ExtractTypeNamesFromGenericInside(inside);

                        yield return new GenericSegment(outer, innerTypes);

                        i = gtIndex;
                    }
                }

                public static IEnumerable<string> ExtractTypesFromGenericText(string text)
                {
                    foreach (var seg in EnumerateGenericSegments(text))
                    {
                        if (!string.IsNullOrEmpty(seg.OuterTypeName))
                            yield return seg.OuterTypeName;

                        foreach (string t in seg.InnerTypeNames)
                            yield return t;
                    }
                }

                private static List<string> ExtractTypeNamesFromGenericInside(string inside)
                {
                    var result = new List<string>();
                    if (string.IsNullOrWhiteSpace(inside))
                        return result;

                    foreach (string part in SplitTopLevelCommaList(inside))
                    {
                        string trimmed = part.Trim();
                        if (trimmed.Length == 0)
                            continue;

                        if (trimmed.StartsWith("in ", StringComparison.Ordinal))
                            trimmed = trimmed.Substring(3).Trim();
                        else if (trimmed.StartsWith("out ", StringComparison.Ordinal))
                            trimmed = trimmed.Substring(4).Trim();

                        string type = ExtractLeadingQualified(trimmed);
                        if (!string.IsNullOrEmpty(type))
                            result.Add(type);

                        foreach (string nested in ExtractTypesFromGenericText(trimmed))
                            result.Add(nested);
                    }

                    return result;
                }

                private static string ExtractLeadingQualified(string text)
                {
                    Match m = QualifiedIdentifierRegex.Match(text);
                    return m.Success ? m.Value : string.Empty;
                }

                private static bool TryFindMatchingAngleBracket(string text, int ltIndex, out int gtIndex)
                {
                    gtIndex = -1;

                    int depth = 0;
                    for (int i = ltIndex; i < text.Length; i++)
                    {
                        char c = text[i];

                        if (c == '<')
                        {
                            depth++;
                            continue;
                        }

                        if (c == '>')
                        {
                            depth--;
                            if (depth == 0)
                            {
                                gtIndex = i;
                                return true;
                            }
                        }
                    }

                    return false;
                }

                private static bool IsIdentifierStart(char c)
                {
                    return char.IsLetter(c) || c == '_';
                }

                private static bool IsQualifiedIdentifierChar(char c)
                {
                    return char.IsLetterOrDigit(c) || c == '_' || c == '.';
                }
            }
        }
    }
}
