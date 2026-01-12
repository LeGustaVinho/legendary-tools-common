#if UNITY_EDITOR_WIN
using System;
using System.Text.RegularExpressions;

namespace AiClipboardPipeline.Editor
{
    /// <summary>
    /// Classifies clipboard content as a "Full C# file" using heuristics only (no Roslyn).
    /// Validation rules:
    /// - Must contain a type declaration: class/struct/interface/enum/record + identifier.
    /// - Must contain '{' and '}' braces.
    /// - Braces must be balanced, ignoring braces inside strings and comments.
    /// - Must have an opening brace '{' after the first declared type.
    /// Safety:
    /// - Rejects content that looks like a unified git patch to avoid false classification.
    /// </summary>
    public sealed class CSharpFileClipboardClassifier : IClipboardClassifier
    {
        public string TypeId => "csharp_file";
        public string DisplayName => "C# Full File";

        // Supports common modifiers before the type keyword.
        // Note: This is heuristic-based and intentionally permissive.
        private static readonly Regex TypeDeclRegex =
            new(
                @"\b(?:(?:public|private|protected|internal|static|sealed|abstract|partial|readonly|ref|unsafe|new)\s+)*(?<kind>class|struct|interface|enum)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\b",
                RegexOptions.Compiled);

        // Supports modifiers before 'record', plus: record Foo / record class Foo / record struct Foo
        private static readonly Regex RecordDeclRegex =
            new(
                @"\b(?:(?:public|private|protected|internal|static|sealed|abstract|partial|readonly|ref|unsafe|new)\s+)*record(\s+(class|struct))?\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\b",
                RegexOptions.Compiled);

        // Git patch signatures (multiline).
        private static readonly Regex GitPatchDiffHeaderRegex =
            new(@"(?m)^diff --git\s+a\/.+\s+b\/.+\s*$", RegexOptions.Compiled);

        private static readonly Regex GitPatchUnifiedHeadersRegex =
            new(@"(?m)^\-\-\-\s+(a\/|\/dev\/null).*$\n^\+\+\+\s+(b\/|\/dev\/null).*$",
                RegexOptions.Compiled);

        private static readonly Regex GitPatchHunkRegex =
            new(@"(?m)^\@\@.*\@\@\s*$", RegexOptions.Compiled);

        public bool TryClassify(string text, out ClipboardClassification classification)
        {
            classification = null;

            if (!IsValidCSharpCode(text, out string primaryTypeName, out _))
                return false;

            // LogicalKey: use type name, consistent with "find file by type name" workflow.
            string logicalKey = string.IsNullOrEmpty(primaryTypeName) ? "csharp:unknown" : primaryTypeName;

            classification = ClipboardClassification.Create(
                TypeId,
                DisplayName,
                logicalKey);

            return true;
        }

        public static bool IsValidCSharpCode(string text, out string primaryTypeName)
        {
            return IsValidCSharpCode(text, out primaryTypeName, out _);
        }

        public static bool IsValidCSharpCode(string text, out string primaryTypeName, out string reason)
        {
            primaryTypeName = string.Empty;
            reason = string.Empty;

            if (string.IsNullOrWhiteSpace(text))
            {
                reason = "Text is empty or whitespace.";
                return false;
            }

            // Safety: avoid misclassifying unified git patches as C# files.
            if (LooksLikeUnifiedGitPatch(text))
            {
                reason = "Looks like a unified git patch (diff headers/hunks present).";
                return false;
            }

            string t = text.Trim();
            if (t.Length < 20)
            {
                reason = "Text is too short to be a full file.";
                return false;
            }

            // Must have a type declaration (including record).
            Match mRecord = RecordDeclRegex.Match(t);
            Match mType = TypeDeclRegex.Match(t);

            Match m;
            if (mRecord.Success && (!mType.Success || mRecord.Index <= mType.Index))
            {
                m = mRecord;
            }
            else
            {
                m = mType;
                if (!m.Success)
                {
                    reason = "No type declaration found (class/struct/interface/enum/record).";
                    return false;
                }
            }

            primaryTypeName = m.Groups["name"].Value;

            if (string.IsNullOrEmpty(primaryTypeName))
            {
                reason = "Type declaration matched but type name could not be extracted.";
                return false;
            }

            // Must have braces at all.
            if (t.IndexOf('{') < 0 || t.IndexOf('}') < 0)
            {
                reason = "Missing '{' or '}' in text.";
                return false;
            }

            if (!AreBracesBalancedIgnoringStringsAndComments(t, out string braceReason))
            {
                reason = "Brace balance failed: " + braceReason;
                return false;
            }

            int declIndex = m.Index;
            if (!HasRealOpeningBraceAfterIndex(t, declIndex))
            {
                reason = "No opening '{' found after the first type declaration (outside strings/comments).";
                return false;
            }

            return true;
        }

        private static bool LooksLikeUnifiedGitPatch(string text)
        {
            if (string.IsNullOrEmpty(text))
                return false;

            // Require a hunk marker, and either a diff header or unified ---/+++ headers.
            bool hasHunk = GitPatchHunkRegex.IsMatch(text);
            if (!hasHunk)
                return false;

            bool hasDiffHeader = GitPatchDiffHeaderRegex.IsMatch(text);
            bool hasUnifiedHeaders = GitPatchUnifiedHeadersRegex.IsMatch(text);

            return hasDiffHeader || hasUnifiedHeaders;
        }

        private static bool AreBracesBalancedIgnoringStringsAndComments(string text, out string reason)
        {
            reason = string.Empty;

            int depth = 0;
            bool sawBrace = false;

            int i = 0;
            int n = text.Length;

            while (i < n)
            {
                char c = text[i];
                char next = i + 1 < n ? text[i + 1] : '\0';

                // Line comment: // ... \n
                if (c == '/' && next == '/')
                {
                    i = SkipLineComment(text, i);
                    continue;
                }

                // Block comment: /* ... */
                if (c == '/' && next == '*')
                {
                    i = SkipBlockComment(text, i);
                    continue;
                }

                // Raw string start: """ or $""" (and more quotes).
                // Do not treat verbatim @" as raw.
                if (IsRawStringStart(text, i, out int rawQuoteCount))
                {
                    i = SkipRawString(text, i, rawQuoteCount);
                    continue;
                }

                // Verbatim string start:
                // 1) @"..."
                // 2) $@"..."
                // 3) @$"..."
                if (IsVerbatimStringStart(text, i))
                {
                    i = SkipVerbatimString(text, i);
                    continue;
                }

                // Regular string / char literal start.
                if (c == '"' || c == '\'')
                {
                    i = SkipRegularStringOrChar(text, i, c);
                    continue;
                }

                if (c == '{')
                {
                    sawBrace = true;
                    depth++;
                }
                else if (c == '}')
                {
                    sawBrace = true;
                    depth--;
                    if (depth < 0)
                    {
                        reason = "Found '}' before matching '{'.";
                        return false;
                    }
                }

                i++;
            }

            if (!sawBrace)
            {
                reason = "No braces found outside strings/comments.";
                return false;
            }

            if (depth != 0)
            {
                reason = "Unbalanced braces, depth=" + depth;
                return false;
            }

            return true;
        }

        private static bool HasRealOpeningBraceAfterIndex(string text, int startIndex)
        {
            if (startIndex < 0)
                startIndex = 0;

            int i = Math.Min(startIndex, text.Length);
            int n = text.Length;

            while (i < n)
            {
                char c = text[i];
                char next = i + 1 < n ? text[i + 1] : '\0';

                // Line comment
                if (c == '/' && next == '/')
                {
                    i = SkipLineComment(text, i);
                    continue;
                }

                // Block comment
                if (c == '/' && next == '*')
                {
                    i = SkipBlockComment(text, i);
                    continue;
                }

                // Raw string
                if (IsRawStringStart(text, i, out int rawQuoteCount))
                {
                    i = SkipRawString(text, i, rawQuoteCount);
                    continue;
                }

                // Verbatim string
                if (IsVerbatimStringStart(text, i))
                {
                    i = SkipVerbatimString(text, i);
                    continue;
                }

                // Regular string / char literal
                if (c == '"' || c == '\'')
                {
                    i = SkipRegularStringOrChar(text, i, c);
                    continue;
                }

                if (c == '{')
                    return true;

                i++;
            }

            return false;
        }

        private static int SkipLineComment(string text, int index)
        {
            int i = index + 2; // skip "//"
            int n = text.Length;

            while (i < n && text[i] != '\n')
            {
                i++;
            }

            return i;
        }

        private static int SkipBlockComment(string text, int index)
        {
            int i = index + 2; // skip "/*"
            int n = text.Length;

            while (i + 1 < n)
            {
                if (text[i] == '*' && text[i + 1] == '/')
                    return i + 2;

                i++;
            }

            return n;
        }

        private static bool IsVerbatimStringStart(string text, int quoteIndex)
        {
            if (quoteIndex < 0 || quoteIndex >= text.Length)
                return false;

            if (text[quoteIndex] != '"')
                return false;

            char prev = quoteIndex > 0 ? text[quoteIndex - 1] : '\0';
            char prev2 = quoteIndex > 1 ? text[quoteIndex - 2] : '\0';

            // @"...": prev == '@'
            // $@"...": prev == '@' and prev2 == '$' (the quote is after '@')
            // @$"...": prev == '$' and prev2 == '@' (the quote is after '$')
            return prev == '@' || (prev == '$' && prev2 == '@') || (prev == '@' && prev2 == '$');
        }

        private static int SkipVerbatimString(string text, int quoteIndex)
        {
            // Starts at the opening quote character (").
            int i = quoteIndex + 1;
            int n = text.Length;

            while (i < n)
            {
                if (text[i] == '"')
                {
                    // Escaped quote inside verbatim is "" (double quote).
                    if (i + 1 < n && text[i + 1] == '"')
                    {
                        i += 2;
                        continue;
                    }

                    return i + 1;
                }

                i++;
            }

            return n;
        }

        private static bool IsRawStringStart(string text, int quoteIndex, out int quoteCount)
        {
            quoteCount = 0;

            int n = text.Length;
            if (quoteIndex < 0 || quoteIndex + 2 >= n)
                return false;

            if (text[quoteIndex] != '"' || text[quoteIndex + 1] != '"' || text[quoteIndex + 2] != '"')
                return false;

            char prev = quoteIndex > 0 ? text[quoteIndex - 1] : '\0';
            char prev2 = quoteIndex > 1 ? text[quoteIndex - 2] : '\0';

            // Do not treat verbatim @" as raw. Also avoid forms where '@' is immediately before the quote.
            if (prev == '@' || prev2 == '@')
                return false;

            quoteCount = CountQuoteRunForward(text, quoteIndex);
            return quoteCount >= 3;
        }

        private static int SkipRawString(string text, int quoteIndex, int quoteCount)
        {
            // Skip the opening delimiter quotes.
            int i = quoteIndex + quoteCount;
            int n = text.Length;

            while (i < n)
            {
                if (text[i] != '"')
                {
                    i++;
                    continue;
                }

                int run = CountQuoteRunForward(text, i);
                if (run >= quoteCount)
                    // Treat the first matching run as the closing delimiter.
                    return i + quoteCount;

                i += run;
            }

            return n;
        }

        private static int SkipRegularStringOrChar(string text, int quoteIndex, char delimiter)
        {
            // Starts at the opening quote character (either " or ').
            int i = quoteIndex + 1;
            int n = text.Length;

            while (i < n)
            {
                char c = text[i];

                if (c == '\\')
                {
                    // Skip escaped character (best-effort).
                    i += i + 1 < n ? 2 : 1;
                    continue;
                }

                if (c == delimiter)
                    return i + 1;

                i++;
            }

            return n;
        }

        private static int CountQuoteRunForward(string text, int startIndex)
        {
            int n = text.Length;
            int count = 0;

            for (int i = startIndex; i < n; i++)
            {
                if (text[i] != '"')
                    break;

                count++;
            }

            return count;
        }
    }
}
#endif