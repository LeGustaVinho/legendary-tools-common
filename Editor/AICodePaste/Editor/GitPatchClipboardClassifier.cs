using System;
using System.Text.RegularExpressions;

namespace AiClipboardPipeline.Editor
{
    /// <summary>
    /// Classifier for unified git patches ("diff --git", hunks, ---/+++ headers).
    /// </summary>
    public sealed class GitPatchClipboardClassifier : IClipboardClassifier
    {
        public string TypeId => "git_patch";
        public string DisplayName => "Git Patch";

        private static readonly Regex DiffHeaderRegex =
            new(@"^diff --git a\/(.+?) b\/(.+?)\s*$", RegexOptions.Compiled | RegexOptions.Multiline);

        private static readonly Regex UnifiedHeaderRegex =
            new(@"^\-\-\-\s+(a\/|\/dev\/null).+\n\+\+\+\s+(b\/|\/dev\/null).+",
                RegexOptions.Compiled | RegexOptions.Multiline);

        private static readonly Regex HunkRegex = new(@"^\@\@.*\@\@\s*$",
            RegexOptions.Compiled | RegexOptions.Multiline);

        public bool TryClassify(string text, out ClipboardClassification classification)
        {
            classification = null;
            if (string.IsNullOrWhiteSpace(text))
                return false;

            bool hasDiffHeader = DiffHeaderRegex.IsMatch(text);
            bool hasUnifiedHeader = UnifiedHeaderRegex.IsMatch(text);
            bool hasHunk = HunkRegex.IsMatch(text);

            if (!(hasDiffHeader || hasUnifiedHeader) || !hasHunk)
                return false;

            string logicalKey = ExtractPrimaryPath(text);
            if (string.IsNullOrEmpty(logicalKey))
                logicalKey = "patch:unknown";

            classification = ClipboardClassification.Create(TypeId, DisplayName, logicalKey);
            return true;
        }

        private static string ExtractPrimaryPath(string text)
        {
            Match m = DiffHeaderRegex.Match(text);
            if (m.Success)
            {
                string aPath = m.Groups[1].Value.Trim();
                string bPath = m.Groups[2].Value.Trim();

                // Prefer the "b/" path for the logical key (new path).
                string path = !string.IsNullOrEmpty(bPath) ? bPath : aPath;
                return $"patch:{path}";
            }

            // Fallback: first "+++ b/..." line.
            int idx = text.IndexOf("\n+++ b/", StringComparison.Ordinal);
            if (idx >= 0)
            {
                int start = idx + "\n+++ b/".Length;
                int end = text.IndexOf('\n', start);
                if (end > start)
                {
                    string path = text.Substring(start, end - start).Trim();
                    return $"patch:{path}";
                }
            }

            return string.Empty;
        }
    }
}