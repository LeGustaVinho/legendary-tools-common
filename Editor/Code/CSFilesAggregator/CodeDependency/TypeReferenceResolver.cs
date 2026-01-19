using System;
using System.Collections.Generic;
using LegendaryTools.CSFilesAggregator.TypeIndex;

namespace LegendaryTools.CSFilesAggregator.DependencyScan
{
    /// <summary>
    /// Resolves raw syntax candidates to type declarations using a type index lookup.
    /// </summary>
    internal sealed class TypeReferenceResolver
    {
        /// <summary>
        /// Tries to resolve a candidate to one or more type index entries (partial types can yield multiple).
        /// </summary>
        /// <param name="typeIndex">Type index lookup.</param>
        /// <param name="context">File context (namespace/usings/aliases) for resolution.</param>
        /// <param name="candidate">Type reference candidate.</param>
        /// <param name="entries">Resolved entries.</param>
        /// <returns>True if resolved to at least one entry; otherwise false.</returns>
        public bool TryResolve(
            ITypeIndexLookup typeIndex,
            SourceFileContext context,
            TypeReferenceCandidate candidate,
            out IReadOnlyList<TypeIndexEntry> entries)
        {
            entries = null;

            if (typeIndex == null || candidate == null) return false;

            string name = candidate.NormalizedName;
            if (string.IsNullOrEmpty(name)) return false;

            // Handle alias usings first for simple names (e.g. using Foo = My.Namespace.Bar; Foo ...)
            if (context != null && context.UsingAliases != null && !candidate.IsQualified)
                if (context.UsingAliases.TryGetValue(name, out string aliasTarget) &&
                    !string.IsNullOrEmpty(aliasTarget))
                {
                    // The alias target may be namespace-qualified or may point to a type.
                    // Normalize by stripping "global::" if present and by accepting as-is (best-effort).
                    string normalizedAliasTarget = StripGlobalPrefix(aliasTarget);
                    if (typeIndex.TryGet(normalizedAliasTarget, out entries)) return true;

                    // If the alias points to a namespace, try appending the alias name is not applicable.
                    // Best-effort: treat as unresolved if direct lookup fails.
                    return false;
                }

            // Qualified candidates: try direct lookup.
            if (candidate.IsQualified)
            {
                string normalizedQualified = StripGlobalPrefix(name);

                if (typeIndex.TryGet(normalizedQualified, out entries)) return true;

                return false;
            }

            // Simple candidates: try namespace, then usings, then global.
            if (context != null)
            {
                if (!string.IsNullOrEmpty(context.Namespace))
                {
                    string fq = context.Namespace + "." + name;
                    if (typeIndex.TryGet(fq, out entries)) return true;
                }

                if (context.Usings != null)
                    for (int i = 0; i < context.Usings.Count; i++)
                    {
                        string u = context.Usings[i];
                        if (string.IsNullOrEmpty(u)) continue;

                        string fq = u + "." + name;
                        if (typeIndex.TryGet(fq, out entries)) return true;
                    }
            }

            if (typeIndex.TryGet(name, out entries)) return true;

            return false;
        }

        private static string StripGlobalPrefix(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;

            // Roslyn string forms can include "global::".
            const string prefix = "global::";
            if (name.StartsWith(prefix, StringComparison.Ordinal)) return name.Substring(prefix.Length);

            return name;
        }
    }
}