using System;

namespace LegendaryTools.CSFilesAggregator.TypeIndex
{
    /// <summary>
    /// Represents a single declared type occurrence in a source file.
    /// </summary>
    [Serializable]
    public sealed class TypeIndexEntry
    {
        /// <summary>
        /// Gets or sets the fully qualified type name (namespace + containing types + name + arity).
        /// Example: <c>MyCompany.Gameplay.Inventory.Item`1</c>.
        /// </summary>
        public string FullName;

        /// <summary>
        /// Gets or sets the type declaration kind.
        /// </summary>
        public TypeDeclarationKind Kind;

        /// <summary>
        /// Gets or sets the project-relative file path where the type is declared (e.g. <c>Assets/...</c> or <c>Packages/...</c>).
        /// </summary>
        public string FilePath;

        /// <summary>
        /// Gets or sets the 1-based line number of the declaration identifier.
        /// </summary>
        public int Line;

        /// <summary>
        /// Gets or sets the 1-based column number of the declaration identifier.
        /// </summary>
        public int Column;
    }
}