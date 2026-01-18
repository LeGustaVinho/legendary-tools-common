namespace LegendaryTools.Editor.Code.CSFilesAggregator.Services
{
    /// <summary>
    /// Unity implementation using <see cref="CSFilesAggregatorPersistedStore"/>.
    /// </summary>
    public sealed class UnityCSFilesAggregatorPersistence : ICSFilesAggregatorPersistence
    {
        /// <inheritdoc />
        public CSFilesAggregatorPersistedData Load()
        {
            return CSFilesAggregatorPersistedStore.instance.ToData();
        }

        /// <inheritdoc />
        public void Save(CSFilesAggregatorPersistedData data)
        {
            CSFilesAggregatorPersistedStore.instance.Apply(data);
            CSFilesAggregatorPersistedStore.instance.SaveToDisk();
        }
    }
}
