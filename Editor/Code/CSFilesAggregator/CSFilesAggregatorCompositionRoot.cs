using LegendaryTools.Editor.Code.CSFilesAggregator.Pipeline;
using LegendaryTools.Editor.Code.CSFilesAggregator.Services;

namespace LegendaryTools.Editor.Code.CSFilesAggregator
{
    /// <summary>
    /// Wires dependencies for the CS files aggregator feature.
    /// </summary>
    public static class CSFilesAggregatorCompositionRoot
    {
        /// <summary>
        /// Creates a fully-wired controller with default implementations.
        /// </summary>
        public static CSFilesAggregatorController CreateController()
        {
            IPathService pathService = new UnityPathService();
            IClipboardService clipboardService = new UnityClipboardService();
            IEditorDialogService dialogService = new UnityEditorDialogService();
            IFilePickerService filePickerService = new UnityFilePickerService();

            ICSFilesAggregatorPersistence persistence = new UnityCSFilesAggregatorPersistence();

            IFileDiscovery discovery = new FileSystemFileDiscovery(pathService);
            IFileReader reader = new FileSystemFileReader();
            IAggregationFormatter formatter = new DefaultAggregationFormatter();

            IAggregationPipeline pipeline = new DefaultAggregationPipeline(
                pathService,
                discovery,
                reader,
                formatter);

            ITextTransformsProvider transformsProvider = new DefaultTextTransformsProvider();

            IAggregationPlanBuilder planBuilder = new DefaultAggregationPlanBuilder(
                pathService,
                discovery);

            return new CSFilesAggregatorController(
                filePickerService,
                pathService,
                clipboardService,
                dialogService,
                pipeline,
                transformsProvider,
                persistence,
                planBuilder);
        }
    }
}