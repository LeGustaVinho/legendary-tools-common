// Assets/legendary-tools-common/Editor/Code/CSFilesAggregator/DependencyScan/SourceFileContext.cs
using System;
using System.Collections.Generic;

namespace LegendaryTools.CSFilesAggregator.DependencyScan
{
    /// <summary>
    /// Captures minimal syntactic context needed for name resolution (namespace, using directives, aliases).
    /// </summary>
    [Serializable]
    public sealed class SourceFileContext
    {
        /// <summary>
        /// Gets or sets the declared namespace for the file (empty for global namespace).
        /// </summary>
        public string Namespace;

        /// <summary>
        /// Gets using namespaces declared in the file (excluding alias usings and static usings).
        /// </summary>
        public List<string> Usings = new List<string>();

        /// <summary>
        /// Gets alias mappings declared in the file (e.g. "Foo" =&gt; "My.Namespace.Bar").
        /// Values are stored as text as written in the code (best-effort normalized later).
        /// </summary>
        public Dictionary<string, string> UsingAliases = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>
        /// Gets or sets the project-relative path (or virtual path) for this source.
        /// </summary>
        public string SourcePath;
    }
}
