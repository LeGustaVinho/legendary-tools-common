namespace LegendaryTools.Editor.Code.CSFilesAggregator.Pipeline
{
    /// <summary>
    /// Executes discovery, reading, transforms, and formatting to aggregate C# files.
    /// </summary>
    public interface IAggregationPipeline
    {
        /// <summary>
        /// Executes the pipeline.
        /// </summary>
        CSFilesAggregationResult Execute(CSFilesAggregationRequest request);
    }
}
