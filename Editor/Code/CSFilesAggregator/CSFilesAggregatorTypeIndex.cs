using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

namespace LegendaryTools.Editor
{
    public sealed class CSFilesAggregatorTypeIndex
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

        public readonly struct FileContext
        {
            public FileContext(string @namespace, HashSet<string> usingNamespaces,
                Dictionary<string, string> usingAliases)
            {
                Namespace = @namespace ?? string.Empty;
                UsingNamespaces = usingNamespaces ?? new HashSet<string>(System.StringComparer.Ordinal);
                UsingAliases = usingAliases ?? new Dictionary<string, string>(System.StringComparer.Ordinal);
            }

            public string Namespace { get; }
            public HashSet<string> UsingNamespaces { get; }
            public Dictionary<string, string> UsingAliases { get; }
        }

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

        private readonly Dictionary<string, List<TypeDefinition>> shortNameToDefs = new(System.StringComparer.Ordinal);

        private readonly Dictionary<string, List<TypeDefinition>> qualifiedNameToDefs =
            new(System.StringComparer.Ordinal);

        private static readonly Regex NamespaceRegex =
            new(@"\bnamespace\s+([A-Za-z_][A-Za-z0-9_\.]*)\b", RegexOptions.Compiled);

        private static readonly Regex TypeDeclRegex =
            new(@"\b(class|struct|interface|enum)\s+([A-Za-z_][A-Za-z0-9_]*)\b", RegexOptions.Compiled);

        // Header-only using parsing
        private static readonly Regex UsingNamespaceRegex =
            new(@"^\s*using\s+(?:static\s+)?([A-Za-z_][A-Za-z0-9_\.]*)\s*;\s*$",
                RegexOptions.Compiled | RegexOptions.Multiline);

        private static readonly Regex UsingAliasRegex =
            new(@"^\s*using\s+([A-Za-z_][A-Za-z0-9_]*)\s*=\s*([A-Za-z_][A-Za-z0-9_\.]*)\s*;\s*$",
                RegexOptions.Compiled | RegexOptions.Multiline);

        private static readonly Regex FirstTypeKeywordRegex =
            new(@"\b(class|struct|interface|enum|delegate)\b", RegexOptions.Compiled);

        public static CSFilesAggregatorTypeIndex BuildFromAssets(CSFilesAggregatorCache cache)
        {
            CSFilesAggregatorTypeIndex index = new();

            string assetsRoot = CSFilesAggregatorUtils.NormalizePath(Application.dataPath);
            if (!Directory.Exists(assetsRoot))
                return index;

            string[] files = Directory.GetFiles(assetsRoot, "*.cs", SearchOption.AllDirectories);

            foreach (string file in files)
            {
                string normalized = CSFilesAggregatorUtils.NormalizePath(file);

                if (!CSFilesAggregatorUtils.IsProjectScriptInAssets(normalized))
                    continue;

                if (normalized.EndsWith(".g.cs", System.StringComparison.OrdinalIgnoreCase))
                    continue;

                string sanitized = cache.GetOrBuildSanitized(normalized);

                string ns = ExtractNamespace(sanitized);

                foreach (Match m in TypeDeclRegex.Matches(sanitized))
                {
                    string typeName = m.Groups[2].Value;

                    index.AddShort(typeName, ns, normalized);

                    if (!string.IsNullOrEmpty(ns))
                        index.AddQualified(ns + "." + typeName, ns, normalized);
                }
            }

            return index;
        }

        public ResolveOutcome ResolveCandidate(string candidate, FileContext context)
        {
            if (string.IsNullOrEmpty(candidate))
                return new ResolveOutcome(ResolveKind.NotFound, new List<string>());

            // Alias: using Foo = Some.Namespace.Type;
            if (context.UsingAliases.TryGetValue(candidate, out string aliasTarget))
            {
                List<string> aliasFiles = ResolveQualified(aliasTarget);
                if (aliasFiles.Count > 0)
                    return new ResolveOutcome(ResolveKind.Resolved, aliasFiles);
            }

            // Qualified already
            if (candidate.Contains('.', System.StringComparison.Ordinal))
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

            // Prefer same namespace
            if (!string.IsNullOrEmpty(context.Namespace))
            {
                List<TypeDefinition> sameNs = defs.Where(d =>
                    string.Equals(d.Namespace, context.Namespace, System.StringComparison.Ordinal)).ToList();
                if (sameNs.Count == 1)
                    return new ResolveOutcome(ResolveKind.Resolved, new List<string> { sameNs[0].File });
            }

            // Prefer via using namespaces
            foreach (string usingNs in context.UsingNamespaces)
            {
                List<string> viaUsing = ResolveQualified(usingNs + "." + candidate);
                if (viaUsing.Count == 1)
                    return new ResolveOutcome(ResolveKind.Resolved, viaUsing);
            }

            // Prefer global
            List<TypeDefinition> global = defs.Where(d => string.IsNullOrEmpty(d.Namespace)).ToList();
            if (global.Count == 1)
                return new ResolveOutcome(ResolveKind.Resolved, new List<string> { global[0].File });

            return new ResolveOutcome(ResolveKind.Ambiguous, new List<string>());
        }

        public static FileContext ParseFileContextFromSanitized(string sanitizedCode)
        {
            if (string.IsNullOrEmpty(sanitizedCode))
                return new FileContext(string.Empty, new HashSet<string>(System.StringComparer.Ordinal),
                    new Dictionary<string, string>(System.StringComparer.Ordinal));

            int headerEnd = FindHeaderEndIndex(sanitizedCode);
            string header = sanitizedCode.Substring(0, headerEnd);

            HashSet<string> usingNamespaces = new(System.StringComparer.Ordinal);
            Dictionary<string, string> usingAliases = new(System.StringComparer.Ordinal);

            foreach (Match m in UsingNamespaceRegex.Matches(header))
            {
                string ns = m.Groups[1].Value;
                if (!string.IsNullOrEmpty(ns))
                    usingNamespaces.Add(ns);
            }

            foreach (Match m in UsingAliasRegex.Matches(header))
            {
                string alias = m.Groups[1].Value;
                string target = m.Groups[2].Value;

                if (!string.IsNullOrEmpty(alias) && !string.IsNullOrEmpty(target))
                    usingAliases[alias] = target;
            }

            string fileNs = ExtractNamespace(sanitizedCode);

            return new FileContext(fileNs, usingNamespaces, usingAliases);
        }

        private static int FindHeaderEndIndex(string code)
        {
            int typeIdx = IndexOfRegex(code, FirstTypeKeywordRegex);
            int nsIdx = IndexOfRegex(code, NamespaceRegex);

            int idx = -1;
            if (typeIdx >= 0 && nsIdx >= 0) idx = System.Math.Min(typeIdx, nsIdx);
            else if (typeIdx >= 0) idx = typeIdx;
            else if (nsIdx >= 0) idx = nsIdx;

            return idx < 0 ? code.Length : System.Math.Max(0, idx);
        }

        private static int IndexOfRegex(string text, Regex regex)
        {
            Match m = regex.Match(text);
            return m.Success ? m.Index : -1;
        }

        private List<string> ResolveQualified(string qualified)
        {
            if (!qualifiedNameToDefs.TryGetValue(qualified, out List<TypeDefinition> defs) || defs.Count == 0)
                return new List<string>();

            return defs.Select(d => d.File)
                .Distinct(System.StringComparer.OrdinalIgnoreCase)
                .OrderBy(p => p, System.StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private void AddShort(string shortName, string ns, string file)
        {
            if (!shortNameToDefs.TryGetValue(shortName, out List<TypeDefinition> list))
            {
                list = new List<TypeDefinition>();
                shortNameToDefs[shortName] = list;
            }

            if (!list.Any(d => string.Equals(d.File, file, System.StringComparison.OrdinalIgnoreCase)))
                list.Add(new TypeDefinition(ns, file));
        }

        private void AddQualified(string qualifiedName, string ns, string file)
        {
            if (!qualifiedNameToDefs.TryGetValue(qualifiedName, out List<TypeDefinition> list))
            {
                list = new List<TypeDefinition>();
                qualifiedNameToDefs[qualifiedName] = list;
            }

            if (!list.Any(d => string.Equals(d.File, file, System.StringComparison.OrdinalIgnoreCase)))
                list.Add(new TypeDefinition(ns, file));
        }

        private static string ExtractNamespace(string sanitizedCode)
        {
            Match m = NamespaceRegex.Match(sanitizedCode);
            return m.Success ? m.Groups[1].Value : string.Empty;
        }
    }
}