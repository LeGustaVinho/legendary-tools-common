namespace LegendaryTools.Editor.Code.CSFilesAggregator.Pipeline
{
    /// <summary>
    /// Builds a preview plan for which files will be aggregated.
    /// </summary>
    public interface IAggregationPlanBuilder
    {
        /// <summary>
        /// Builds an <see cref="AggregationPlan"/> from the aggregation request.
        /// </summary>
        AggregationPlan Build(CSFilesAggregationRequest request);
    }
}