#if UNITY_EDITOR_WIN
using System;

namespace AiClipboardPipeline.Editor
{
    internal sealed class TextNormalization
    {
        public string NormalizeToLF(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            return text.Replace("\r\n", "\n").Replace("\r", "\n");
        }

        public string EnsureTrailingNewline(string text)
        {
            if (string.IsNullOrEmpty(text))
                return "\n";

            return text.EndsWith("\n", StringComparison.Ordinal) ? text : text + "\n";
        }
    }
}
#endif