using System.Collections.Generic;

namespace LegendaryTools.Editor.Code.CSFilesAggregator.Pipeline
{
    /// <summary>
    /// Builds the list of text transforms based on the current UI state.
    /// </summary>
    public interface ITextTransformsProvider
    {
        /// <summary>
        /// Builds transforms for the current state.
        /// </summary>
        IReadOnlyList<ITextTransform> BuildTransforms(CSFilesAggregatorState state);
    }
}
