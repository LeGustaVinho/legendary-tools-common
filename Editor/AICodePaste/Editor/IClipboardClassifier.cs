namespace AiClipboardPipeline.Editor
{
    /// <summary>
    /// Contract for clipboard classifiers. Add new classifications by implementing this interface.
    /// </summary>
    public interface IClipboardClassifier
    {
        /// <summary>
        /// Stable identifier for this classifier.
        /// </summary>
        string TypeId { get; }

        /// <summary>
        /// User-facing label.
        /// </summary>
        string DisplayName { get; }

        /// <summary>
        /// Returns true when the text matches this classifier and outputs a classification.
        /// </summary>
        bool TryClassify(string text, out ClipboardClassification classification);
    }
}