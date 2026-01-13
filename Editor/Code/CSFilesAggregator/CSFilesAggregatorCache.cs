using System.Collections.Generic;
using System.IO;

namespace LegendaryTools.Editor
{
    public sealed class CSFilesAggregatorCache
    {
        public readonly struct FileAnalysis
        {
            public FileAnalysis(CSFilesAggregatorTypeIndex.FileContext context, HashSet<string> candidates)
            {
                Context = context;
                Candidates = candidates ?? new HashSet<string>(System.StringComparer.Ordinal);
            }

            public CSFilesAggregatorTypeIndex.FileContext Context { get; }
            public HashSet<string> Candidates { get; }
        }

        private readonly Dictionary<string, string> rawCache = new(System.StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> sanitizedCache = new(System.StringComparer.OrdinalIgnoreCase);

        // Processed output depends on removeUsings flag, so cache by key.
        private readonly Dictionary<(string path, bool removeUsings), string> processedOutputCache = new();

        private readonly Dictionary<string, FileAnalysis> analysisCache = new(System.StringComparer.OrdinalIgnoreCase);

        public string GetOrBuildRaw(string absolutePath)
        {
            string path = CSFilesAggregatorUtils.NormalizePath(absolutePath);

            if (rawCache.TryGetValue(path, out string raw))
                return raw;

            raw = File.Exists(path) ? File.ReadAllText(path) : string.Empty;
            rawCache[path] = raw;

            return raw;
        }

        public string GetOrBuildSanitized(string absolutePath)
        {
            string path = CSFilesAggregatorUtils.NormalizePath(absolutePath);

            if (sanitizedCache.TryGetValue(path, out string sanitized))
                return sanitized;

            string raw = GetOrBuildRaw(path);
            sanitized = CSFilesAggregatorUtils.StripCommentsAndStrings(raw);

            sanitizedCache[path] = sanitized;
            return sanitized;
        }

        public string GetOrBuildProcessedForOutput(string absolutePath, bool removeUsings)
        {
            string path = CSFilesAggregatorUtils.NormalizePath(absolutePath);

            (string path, bool removeUsings) key = (path, removeUsings);
            if (processedOutputCache.TryGetValue(key, out string processed))
                return processed;

            string raw = GetOrBuildRaw(path);

            processed = removeUsings
                ? CSFilesAggregatorUtils.RemoveUsingDirectives(raw)
                : raw;

            processedOutputCache[key] = processed;
            return processed;
        }

        public FileAnalysis GetOrBuildFileAnalysis(string absolutePath)
        {
            string path = CSFilesAggregatorUtils.NormalizePath(absolutePath);

            if (analysisCache.TryGetValue(path, out FileAnalysis analysis))
                return analysis;

            string sanitized = GetOrBuildSanitized(path);

            CSFilesAggregatorTypeIndex.FileContext context =
                CSFilesAggregatorTypeIndex.ParseFileContextFromSanitized(sanitized);
            HashSet<string> candidates = CSFilesAggregatorUtils.ExtractCandidateTypeNames(sanitized);

            analysis = new FileAnalysis(context, candidates);
            analysisCache[path] = analysis;

            return analysis;
        }
    }
}