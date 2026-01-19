namespace LegendaryTools.Editor.Code.CSFilesAggregator.Services
{
    /// <summary>
    /// Writes text to the system clipboard.
    /// </summary>
    public interface IClipboardService
    {
        /// <summary>
        /// Sets clipboard text.
        /// </summary>
        void SetClipboardText(string text);
    }
}