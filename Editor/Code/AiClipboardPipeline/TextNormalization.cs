#if UNITY_EDITOR_WIN
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

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

        private static readonly Regex FileHeaderRegex =
            new(@"^\s*//\s*File\s*:\s*(.+?)\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>
        /// Removes the optional clipboard "file header" line from the content before writing to disk.
        /// Header format: // File: Assets/.../Name.cs
        /// Only scans the first 50 lines. If found, removes the header line and one following blank line (if present).
        /// </summary>
        public string StripFileHeaderIfPresent(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            string t = text.Replace("\r\n", "\n").Replace("\r", "\n");
            string[] lines = t.Split('\n');
            int max = Math.Min(lines.Length, 50);

            int headerIndex = -1;
            for (int i = 0; i < max; i++)
            {
                if (FileHeaderRegex.IsMatch(lines[i]))
                {
                    headerIndex = i;
                    break;
                }
            }

            if (headerIndex < 0)
                return t;

            List<string> kept = new(lines.Length);
            for (int i = 0; i < lines.Length; i++)
            {
                if (i == headerIndex)
                    continue;

                if (i == headerIndex + 1 && string.IsNullOrWhiteSpace(lines[i]))
                    continue;

                kept.Add(lines[i]);
            }

            return string.Join("\n", kept);
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