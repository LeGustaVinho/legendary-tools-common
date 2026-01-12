using System;

namespace AiClipboardPipeline.Editor
{
    /// <summary>
    /// Represents the result of classifying clipboard content.
    /// </summary>
    [Serializable]
    public sealed class ClipboardClassification
    {
        public string TypeId;
        public string DisplayName;

        /// <summary>
        /// A stable key that identifies the "same logical file" across copies.
        /// For C#, typically Namespace.TypeName.
        /// For git patches, typically the first affected path or a synthetic key.
        /// </summary>
        public string LogicalKey;

        public static ClipboardClassification Create(string typeId, string displayName, string logicalKey)
        {
            return new ClipboardClassification
            {
                TypeId = typeId ?? string.Empty,
                DisplayName = displayName ?? string.Empty,
                LogicalKey = logicalKey ?? string.Empty
            };
        }
    }
}