namespace LegendaryTools.Editor.Code.CSFilesAggregator.Services
{
    /// <summary>
    /// Persists settings and selection between assembly reloads.
    /// </summary>
    public interface ICSFilesAggregatorPersistence
    {
        /// <summary>
        /// Loads persisted data.
        /// </summary>
        CSFilesAggregatorPersistedData Load();

        /// <summary>
        /// Saves persisted data.
        /// </summary>
        void Save(CSFilesAggregatorPersistedData data);
    }
}