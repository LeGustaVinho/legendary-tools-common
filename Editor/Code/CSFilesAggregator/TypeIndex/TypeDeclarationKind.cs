namespace LegendaryTools.CSFilesAggregator.TypeIndex
{
    /// <summary>
    /// Declared type kinds supported by the index.
    /// </summary>
    public enum TypeDeclarationKind
    {
        /// <summary>
        /// A <c>class</c> declaration.
        /// </summary>
        Class,

        /// <summary>
        /// A <c>struct</c> declaration.
        /// </summary>
        Struct,

        /// <summary>
        /// An <c>interface</c> declaration.
        /// </summary>
        Interface,

        /// <summary>
        /// An <c>enum</c> declaration.
        /// </summary>
        Enum,

        /// <summary>
        /// A <c>record</c> (class or struct) declaration.
        /// </summary>
        Record
    }
}