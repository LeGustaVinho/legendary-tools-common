using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace CSharpRegexStripper
{
    public static class CSharpRewriters
    {
        private static readonly Regex InterfaceBlock = new(
            @"(?msx)
            \binterface\b
            [^{;]*?
            \{
                (?>
                    [^{}]+
                  | \{ (?<d>)
                  | \} (?<-d>)
                )*
                (?(d)(?!))
            \}
            ",
            RegexOptions.Compiled
        );

        private static readonly Regex BlockBodiedMethod = new(
            @"(?msx)
            ^(?<lead>
                [ \t]*
                (?:\[[^\]]*\]\s*)*
                (?:
                    (?:public|private|protected|internal)\s+
                  | static\s+
                  | virtual\s+
                  | override\s+
                  | abstract\s+
                  | sealed\s+
                  | async\s+
                  | extern\s+
                  | unsafe\s+
                  | new\s+
                  | partial\s+
                )*
                (?!\s*(?:if|for|foreach|while|switch|catch|using|lock|return|throw|do|else|try|finally)\b)
                [^\r\n;{}]*?
                \(
                    [^(){};]*?
                \)
                (?:\s*where\b[^{;]*)?
                \s*
            )
            (?<body>
                \{
                    (?>
                        [^{}]+
                      | \{ (?<d>)
                      | \} (?<-d>)
                    )*
                    (?(d)(?!))
                \}
            )
            ",
            RegexOptions.Compiled
        );

        private static readonly Regex ExpressionBodiedMethod = new(
            @"(?msx)
            ^(?<lead>
                [ \t]*
                (?:\[[^\]]*\]\s*)*
                (?:
                    (?:public|private|protected|internal)\s+
                  | static\s+
                  | virtual\s+
                  | override\s+
                  | abstract\s+
                  | sealed\s+
                  | async\s+
                  | extern\s+
                  | unsafe\s+
                  | new\s+
                  | partial\s+
                )*
                (?!\s*(?:if|for|foreach|while|switch|catch|using|lock|return|throw|do|else|try|finally)\b)
                [^\r\n;{}]*?
                \(
                    [^(){};]*?
                \)
                (?:\s*where\b[^;{]*)?
                \s*
            )
            =>\s*
            (?<expr>.*?)
            \s*;
            ",
            RegexOptions.Compiled
        );

        private static readonly Regex PropertyBlock = new(
            @"(?msx)
            ^(?<lead>
                [ \t]*
                (?:\[[^\]]*\]\s*)*
                (?:
                    (?:public|private|protected|internal)\s+
                  | static\s+
                  | virtual\s+
                  | override\s+
                  | abstract\s+
                  | sealed\s+
                  | unsafe\s+
                  | extern\s+
                  | new\s+
                  | partial\s+
                )*
                (?!\s*(?:if|for|foreach|while|switch|catch|using|lock|return|throw|do|else|try|finally)\b)
                [^\r\n;{}=]*?
                \b(?<name>@?[A-Za-z_][A-Za-z0-9_]*)\b
                \s*
                (?:\[[^\]]*\]\s*)?
            )
            \{
                (?<content>
                    (?>
                        [^{}]+
                      | \{ (?<d>)
                      | \} (?<-d>)
                    )*
                    (?(d)(?!))
                )
            \}
            ",
            RegexOptions.Compiled
        );

        private static readonly Regex GetAccessorHead = new(
            @"(?msx)\b(?<acc>(?:public|private|protected|internal)\s+)?get\b\s*(?:;|\{|\=\>)",
            RegexOptions.Compiled
        );

        private static readonly Regex SetAccessorHead = new(
            @"(?msx)\b(?<acc>(?:public|private|protected|internal)\s+)?set\b\s*(?:;|\{|\=\>)",
            RegexOptions.Compiled
        );

        private static readonly Regex AutoGetAccessor = new(
            @"(?msx)\b(?:(?:public|private|protected|internal)\s+)?get\b\s*;",
            RegexOptions.Compiled
        );

        private static readonly Regex AutoSetAccessor = new(
            @"(?msx)\b(?:(?:public|private|protected|internal)\s+)?set\b\s*;",
            RegexOptions.Compiled
        );

        public static string StripMethodBodies(string original, string masked, StripOptions options)
        {
            if (original == null || masked == null)
                return original;

            List<TextRange> interfaceRanges = options.SkipInterfaceMembers ? FindInterfaceRanges(masked) : null;

            List<TextEdit> edits = new();

            foreach (Match m in BlockBodiedMethod.Matches(masked))
            {
                string lead = m.Groups["lead"].Value;

                if (options.SkipAbstractMembers && ContainsWord(lead, "abstract"))
                    continue;

                int bodyStart = m.Groups["body"].Index;
                int bodyLen = m.Groups["body"].Length;

                if (interfaceRanges != null && IntersectsAny(interfaceRanges, bodyStart, bodyLen))
                    continue;

                if (options.MethodBodyMode == MethodBodyMode.Semicolon)
                {
                    int start = FindSemicolonInsertionPoint(original, bodyStart);
                    int length = bodyStart + bodyLen - start;
                    edits.Add(new TextEdit(start, length, ";"));
                }
                else
                {
                    edits.Add(new TextEdit(bodyStart, bodyLen, "{ }"));
                }
            }

            foreach (Match m in ExpressionBodiedMethod.Matches(masked))
            {
                string lead = m.Groups["lead"].Value;

                if (options.SkipAbstractMembers && ContainsWord(lead, "abstract"))
                    continue;

                int arrowIndex = m.Index;
                int endIndex = m.Index + m.Length;

                int start = FindArrowStart(original, arrowIndex, endIndex);
                if (start < 0)
                    start = m.Index;

                int length = m.Index + m.Length - start;

                if (interfaceRanges != null && IntersectsAny(interfaceRanges, start, length))
                    continue;

                string replacement = options.MethodBodyMode == MethodBodyMode.EmptyBlock ? "{ }" : ";";
                edits.Add(new TextEdit(start, length, replacement));
            }

            return ApplyEditsDescending(original, edits);
        }

        public static string ConvertNonAutoGetSetPropertiesToAutoProperties(string original, string masked,
            StripOptions options)
        {
            if (original == null || masked == null)
                return original;

            List<TextRange> interfaceRanges = options.SkipInterfaceMembers ? FindInterfaceRanges(masked) : null;

            List<TextEdit> edits = new();

            foreach (Match m in PropertyBlock.Matches(masked))
            {
                int blockStart = m.Index;
                int blockLen = m.Length;

                if (interfaceRanges != null && IntersectsAny(interfaceRanges, blockStart, blockLen))
                    continue;

                string lead = m.Groups["lead"].Value;

                if (options.SkipAbstractMembers && ContainsWord(lead, "abstract"))
                    continue;

                string content = m.Groups["content"].Value;

                bool hasGet = GetAccessorHead.IsMatch(content);
                bool hasSet = SetAccessorHead.IsMatch(content);

                if (!hasGet || !hasSet)
                    continue;

                bool isAuto = AutoGetAccessor.IsMatch(content) && AutoSetAccessor.IsMatch(content) &&
                              !content.Contains("{") && !content.Contains("=>");

                if (isAuto)
                    continue;

                string getAcc = ExtractAccessorAccessibility(content, true);
                string setAcc = ExtractAccessorAccessibility(content, false);

                string accessorBlock;
                if (string.IsNullOrEmpty(getAcc) && string.IsNullOrEmpty(setAcc))
                    accessorBlock = "{ get; set; }";
                else if (!string.IsNullOrEmpty(getAcc) && string.IsNullOrEmpty(setAcc))
                    accessorBlock = "{ " + getAcc + "get; set; }";
                else if (string.IsNullOrEmpty(getAcc) && !string.IsNullOrEmpty(setAcc))
                    accessorBlock = "{ get; " + setAcc + "set; }";
                else
                    accessorBlock = "{ " + getAcc + "get; " + setAcc + "set; }";

                string leadFromOriginal = original.Substring(m.Groups["lead"].Index, m.Groups["lead"].Length);
                string replacement = leadFromOriginal + accessorBlock;

                edits.Add(new TextEdit(blockStart, blockLen, replacement));
            }

            return ApplyEditsDescending(original, edits);
        }

        private static int FindSemicolonInsertionPoint(string source, int bodyStart)
        {
            int i = bodyStart - 1;

            while (i >= 0 && (source[i] == ' ' || source[i] == '\t' || source[i] == '\r' || source[i] == '\n'))
            {
                i--;
            }

            return i + 1;
        }

        private static List<TextRange> FindInterfaceRanges(string masked)
        {
            List<TextRange> ranges = new();
            foreach (Match m in InterfaceBlock.Matches(masked))
            {
                ranges.Add(new TextRange(m.Index, m.Length));
            }

            return ranges;
        }

        private static bool IntersectsAny(List<TextRange> ranges, int start, int length)
        {
            if (ranges == null || ranges.Count == 0)
                return false;

            for (int i = 0; i < ranges.Count; i++)
            {
                if (ranges[i].Intersects(start, length))
                    return true;
            }

            return false;
        }

        private static bool ContainsWord(string text, string word)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(word))
                return false;

            return Regex.IsMatch(text, @"(?<!\w)" + Regex.Escape(word) + @"(?!\w)");
        }

        private static string ExtractAccessorAccessibility(string content, bool isGet)
        {
            Match m = (isGet ? GetAccessorHead : SetAccessorHead).Match(content);
            if (!m.Success)
                return string.Empty;

            string acc = m.Groups["acc"].Value;
            if (string.IsNullOrWhiteSpace(acc))
                return string.Empty;

            acc = acc.Trim();
            return acc.Length == 0 ? string.Empty : acc + " ";
        }

        private static int FindArrowStart(string s, int approxStart, int approxEnd)
        {
            int count = Math.Max(0, approxEnd - approxStart);
            int idx = s.IndexOf("=>", approxStart, count, StringComparison.Ordinal);
            if (idx < 0)
                return -1;
            return idx;
        }

        private static string ApplyEditsDescending(string source, List<TextEdit> edits)
        {
            if (edits == null || edits.Count == 0)
                return source;

            edits.Sort((a, b) => b.Start.CompareTo(a.Start));

            StringBuilder sb = new(source);

            foreach (TextEdit e in edits)
            {
                if (e.Start < 0 || e.Start > sb.Length)
                    continue;

                int len = e.Length;
                if (e.Start + len > sb.Length)
                    len = sb.Length - e.Start;

                sb.Remove(e.Start, len);
                sb.Insert(e.Start, e.Replacement);
            }

            return sb.ToString();
        }

        private readonly struct TextEdit
        {
            public int Start { get; }
            public int Length { get; }
            public string Replacement { get; }

            public TextEdit(int start, int length, string replacement)
            {
                Start = start;
                Length = length;
                Replacement = replacement ?? string.Empty;
            }
        }
    }
}