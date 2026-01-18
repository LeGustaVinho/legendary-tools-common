using System.Collections.Generic;
using CSharpRegexStripper;

namespace LegendaryTools.Editor.Code.CSFilesAggregator.Pipeline
{
    /// <summary>
    /// Default transform builder for the aggregator.
    /// </summary>
    public sealed class DefaultTextTransformsProvider : ITextTransformsProvider
    {
        /// <inheritdoc />
        public IReadOnlyList<ITextTransform> BuildTransforms(CSFilesAggregatorState state)
        {
            List<ITextTransform> transforms = new List<ITextTransform>();

            if (state != null && state.RemoveUsings)
            {
                transforms.Add(new RemoveUsingsTransform());
            }

            if (state != null && state.UseImplementationStripper)
            {
                StripOptions options = state.BuildStripOptions();
                transforms.Add(new ImplementationStripperTransform(options));
            }

            return transforms;
        }
    }
}
