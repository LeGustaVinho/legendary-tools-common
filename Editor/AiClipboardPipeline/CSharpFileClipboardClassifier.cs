#if UNITY_EDITOR_WIN
using System;
using System.Collections.Generic;
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

        private static readonly Regex TypeDeclRegex =
            new(@"\b(class|struct|interface|enum)\s+([A-Za-z_][A-Za-z0-9_]*)\b",
                RegexOptions.Compiled);

        // record Foo / record class Foo / record struct Foo
        private static readonly Regex RecordDeclRegex =
            new(@"\brecord(\s+(class|struct))?\s+([A-Za-z_][A-Za-z0-9_]*)\b",
                RegexOptions.Compiled);

        // Git patch signatures (multiline).
        private static readonly Regex GitPatchDiffHeaderRegex =
            new(@"(?m)^diff --git\s+a\/.+\s+b\/.+\s*$", RegexOptions.Compiled);

        private static readonly Regex GitPatchUnifiedHeadersRegex =
            new(@"(?m)^\-\-\-\s+(a\/|\/dev\/null).*$\n^\+\+\+\s+(b\/|\/dev\/null).*$",
                RegexOptions.Compiled);

        private static readonly Regex GitPatchHunkRegex = new(@"(?m)^\@\@.*\@\@\s*$", RegexOptions.Compiled);

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
                primaryTypeName = m.Groups[3].Value;
            }
            else
            {
                m = mType;
                if (!m.Success)
                {
                    reason = "No type declaration found (class/struct/interface/enum/record).";
                    return false;
                }

                primaryTypeName = m.Groups[2].Value;
            }

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

            Scanner s = new(text);

            while (s.MoveNext())
            {
                char c = s.Current;

                if (s.InLineComment || s.InBlockComment)
                    continue;

                if (s.InString)
                    continue;

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

            Scanner s = new(text);
            s.JumpTo(startIndex);

            while (s.MoveNext())
            {
                if (s.InLineComment || s.InBlockComment || s.InString)
                    continue;

                if (s.Current == '{')
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Minimal scanner that tracks strings and comments, so brace parsing ignores them.
        /// Handles:
        /// - // line comments
        /// - /* block comments * /
        /// - "string" with escapes (correctly handles \\\" patterns)
        /// - @"verbatim strings"
        /// - $@"interpolated verbatim strings"
        /// - @$"interpolated verbatim strings"
        /// - """raw strings""" and $"""interpolated raw strings"""
        /// - 'c' char literals with escapes
        /// </summary>
        private struct Scanner
        {
            private readonly string _text;
            private int _i;

            public char Current { get; private set; }

            public bool InLineComment { get; private set; }
            public bool InBlockComment { get; private set; }
            public bool InString { get; private set; }

            private bool _inVerbatimString;
            private bool _inRawString;
            private int _rawQuoteCount;
            private char _stringDelimiter;

            public Scanner(string text)
            {
                _text = text ?? string.Empty;
                _i = -1;

                Current = '\0';

                InLineComment = false;
                InBlockComment = false;
                InString = false;

                _inVerbatimString = false;
                _inRawString = false;
                _rawQuoteCount = 0;
                _stringDelimiter = '\0';
            }

            public void JumpTo(int index)
            {
                _i = Math.Clamp(index - 1, -1, _text.Length - 1);
                Current = '\0';

                // Reset parsing state on jump (safe heuristic).
                InLineComment = false;
                InBlockComment = false;
                InString = false;

                _inVerbatimString = false;
                _inRawString = false;
                _rawQuoteCount = 0;
                _stringDelimiter = '\0';
            }

            public bool MoveNext()
            {
                if (_i + 1 >= _text.Length)
                    return false;

                _i++;
                Current = _text[_i];

                char prev = _i > 0 ? _text[_i - 1] : '\0';
                char next = _i + 1 < _text.Length ? _text[_i + 1] : '\0';
                char next2 = _i + 2 < _text.Length ? _text[_i + 2] : '\0';
                char prev2 = _i > 1 ? _text[_i - 2] : '\0';

                if (InLineComment)
                {
                    if (Current == '\n')
                        InLineComment = false;

                    return true;
                }

                if (InBlockComment)
                {
                    if (prev == '*' && Current == '/')
                        InBlockComment = false;

                    return true;
                }

                if (InString)
                {
                    if (_inRawString)
                    {
                        if (Current == '"')
                        {
                            int run = CountQuoteRunForward(_i);
                            if (run >= _rawQuoteCount && !IsImmediatelyFollowedByQuote(_i, _rawQuoteCount))
                            {
                                // Consume the delimiter quotes.
                                _i += _rawQuoteCount - 1;
                                Current = '"';

                                InString = false;
                                _inRawString = false;
                                _rawQuoteCount = 0;
                                _stringDelimiter = '\0';
                            }
                        }

                        return true;
                    }

                    if (_inVerbatimString)
                    {
                        // Verbatim string ends on " not followed by "
                        // Escaped quote inside verbatim is "" (double quote).
                        if (Current == '"' && next != '"')
                        {
                            InString = false;
                            _inVerbatimString = false;
                            _stringDelimiter = '\0';
                        }
                        else if (Current == '"' && next == '"')
                        {
                            // Consume the escaped quote.
                            _i++;
                            Current = '"';
                        }

                        return true;
                    }

                    // Regular string / char literal with backslash escapes.
                    if (Current == _stringDelimiter && !IsEscapedByBackslashRun(_i))
                    {
                        InString = false;
                        _stringDelimiter = '\0';
                    }

                    return true;
                }

                // Comments (only when not in string)
                if (Current == '/' && next == '/')
                {
                    InLineComment = true;
                    return true;
                }

                if (Current == '/' && next == '*')
                {
                    InBlockComment = true;
                    return true;
                }

                // Raw string start: """ or $"""
                // Do NOT allow raw detection when the quote is preceded by '@' (verbatim forms like @"...").
                if (Current == '"' && next == '"' && next2 == '"' && prev != '@' && prev2 != '@')
                {
                    int run = CountQuoteRunForward(_i);
                    if (run >= 3)
                    {
                        InString = true;
                        _inRawString = true;
                        _rawQuoteCount = run;
                        _stringDelimiter = '"';

                        // Consume the starting delimiter quotes.
                        _i += run - 1;
                        Current = '"';
                        return true;
                    }
                }

                // Verbatim string start patterns:
                // 1) @"..."
                // 2) $@"..."
                // 3) @$"..."
                if (Current == '"' && (prev == '@' || (prev == '$' && prev2 == '@')))
                {
                    InString = true;
                    _inVerbatimString = true;
                    _inRawString = false;
                    _rawQuoteCount = 0;
                    _stringDelimiter = '"';
                    return true;
                }

                // Regular string / char literal start.
                if (Current == '"' || Current == '\'')
                {
                    InString = true;
                    _inVerbatimString = false;
                    _inRawString = false;
                    _rawQuoteCount = 0;
                    _stringDelimiter = Current;
                    return true;
                }

                return true;
            }

            private int CountQuoteRunForward(int startIndex)
            {
                int n = 0;
                for (int j = startIndex; j < _text.Length; j++)
                {
                    if (_text[j] != '"')
                        break;
                    n++;
                }

                return n;
            }

            private bool IsImmediatelyFollowedByQuote(int startIndex, int quoteCount)
            {
                int idx = startIndex + quoteCount;
                if (idx >= 0 && idx < _text.Length)
                    return _text[idx] == '"';
                return false;
            }

            private bool IsEscapedByBackslashRun(int quoteIndex)
            {
                // Returns true when the quote is escaped by an odd number of consecutive backslashes.
                int backslashes = 0;
                for (int j = quoteIndex - 1; j >= 0; j--)
                {
                    if (_text[j] != '\\')
                        break;
                    backslashes++;
                }

                return (backslashes & 1) == 1;
            }
        }
    }
}
#endif