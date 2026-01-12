#if UNITY_EDITOR_WIN
using System;
using System.Text.RegularExpressions;

namespace AiClipboardPipeline.Editor
{
    /// <summary>
    /// Classifies clipboard content as a "Full C# file" using heuristics only (no Roslyn).
    /// Validation rules:
    /// - Must contain a type declaration: class/struct/interface/enum + identifier.
    /// - Must contain '{' and '}' braces.
    /// - Braces must be balanced, ignoring braces inside strings and comments.
    /// - Must have an opening brace '{' that belongs to the declared type (after the declaration).
    /// </summary>
    public sealed class CSharpFileClipboardClassifier : IClipboardClassifier
    {
        public string TypeId => "csharp_file";
        public string DisplayName => "C# Full File";

        private static readonly Regex TypeDeclRegex =
            new Regex(@"\b(class|struct|interface|enum)\s+([A-Za-z_][A-Za-z0-9_]*)\b",
                RegexOptions.Compiled);

        public bool TryClassify(string text, out ClipboardClassification classification)
        {
            classification = null;

            if (!IsValidCSharpCode(text, out string primaryTypeName))
                return false;

            // LogicalKey: use type name, consistent with "find file by type name" workflow.
            string logicalKey = string.IsNullOrEmpty(primaryTypeName) ? "csharp:unknown" : primaryTypeName;

            classification = ClipboardClassification.Create(
                typeId: TypeId,
                displayName: DisplayName,
                logicalKey: logicalKey);

            return true;
        }

        /// <summary>
        /// Public validation helper so appliers/controllers can re-check safety before writing files.
        /// </summary>
        public static bool IsValidCSharpCode(string text, out string primaryTypeName)
        {
            primaryTypeName = string.Empty;

            if (string.IsNullOrWhiteSpace(text))
                return false;

            // Quick "looks like code" cleanup.
            string t = text.Trim();
            if (t.Length < 20)
                return false;

            // Must have a type declaration.
            Match m = TypeDeclRegex.Match(t);
            if (!m.Success)
                return false;

            primaryTypeName = m.Groups[2].Value;

            // Must have braces at all (raw check).
            if (t.IndexOf('{') < 0 || t.IndexOf('}') < 0)
                return false;

            // Braces must be balanced ignoring strings/comments.
            if (!AreBracesBalancedIgnoringStringsAndComments(t))
                return false;

            // Ensure the type declaration actually has an opening brace '{' after it (real brace, not in comment/string).
            int declIndex = m.Index;
            if (!HasRealOpeningBraceAfterIndex(t, declIndex))
                return false;

            return true;
        }

        private static bool AreBracesBalancedIgnoringStringsAndComments(string text)
        {
            int depth = 0;
            bool sawBrace = false;

            var s = new Scanner(text);

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
                        return false;
                }
            }

            return sawBrace && depth == 0;
        }

        private static bool HasRealOpeningBraceAfterIndex(string text, int startIndex)
        {
            if (startIndex < 0)
                startIndex = 0;

            var s = new Scanner(text);
            s.JumpTo(startIndex);

            // Walk forward until we find a real '{' or hit a hard stop.
            // Hard stop: if file ends without encountering '{' outside comment/string.
            while (s.MoveNext())
            {
                if (s.InLineComment || s.InBlockComment || s.InString)
                    continue;

                if (s.Current == '{')
                    return true;

                // If we hit another type declaration before a '{', it's still OK to continue,
                // but in practice the first type should have one soon.
            }

            return false;
        }

        /// <summary>
        /// Minimal scanner that tracks strings and comments, so brace parsing ignores them.
        /// Handles:
        /// - // line comments
        /// - /* block comments * /
        /// - "string" with escapes
        /// - @"verbatim strings"
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
            private char _stringDelimiter; // '"' or '\''

            public Scanner(string text)
            {
                _text = text ?? string.Empty;
                _i = -1;

                Current = '\0';

                InLineComment = false;
                InBlockComment = false;
                InString = false;

                _inVerbatimString = false;
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

                // End line comment on newline.
                if (InLineComment)
                {
                    if (Current == '\n')
                        InLineComment = false;

                    return true;
                }

                // End block comment on */
                if (InBlockComment)
                {
                    if (prev == '*' && Current == '/')
                        InBlockComment = false;

                    return true;
                }

                // Handle string states.
                if (InString)
                {
                    if (_inVerbatimString)
                    {
                        // Verbatim string ends on " not doubled.
                        if (Current == '"' && next != '"')
                        {
                            InString = false;
                            _inVerbatimString = false;
                            _stringDelimiter = '\0';
                        }
                        else if (Current == '"' && next == '"')
                        {
                            // Skip escaped quote "" inside verbatim string.
                            _i++;
                            Current = '"';
                        }

                        return true;
                    }

                    // Normal string/char literal ends on delimiter not escaped.
                    if (Current == _stringDelimiter && prev != '\\')
                    {
                        InString = false;
                        _stringDelimiter = '\0';
                    }

                    return true;
                }

                // Start comment?
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

                // Start string? (include verbatim @"")
                if (Current == '@' && next == '"')
                {
                    InString = true;
                    _inVerbatimString = true;
                    _stringDelimiter = '"';
                    return true;
                }

                if (Current == '"' || Current == '\'')
                {
                    InString = true;
                    _inVerbatimString = false;
                    _stringDelimiter = Current;
                    return true;
                }

                return true;
            }
        }
    }
}
#endif
