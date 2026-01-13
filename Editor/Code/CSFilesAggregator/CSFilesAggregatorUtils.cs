using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace LegendaryTools.Editor
{
    public static class CSFilesAggregatorUtils
    {
        // -----------------------
        // Paths / project policy
        // -----------------------
        public static string NormalizePath(string path)
        {
            return string.IsNullOrEmpty(path) ? string.Empty : path.Replace('\\', '/');
        }

        public static string ToAbsolutePath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return string.Empty;

            string normalized = NormalizePath(path);

            if (normalized.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("Assets", StringComparison.OrdinalIgnoreCase))
            {
                string assets = NormalizePath(Application.dataPath);
                if (normalized.Equals("Assets", StringComparison.OrdinalIgnoreCase))
                    return assets;

                return NormalizePath(System.IO.Path.Combine(assets, normalized.Substring("Assets/".Length)));
            }

            return normalized;
        }

        public static string TryToProjectRelativePath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return string.Empty;

            string abs = NormalizePath(path);
            string assets = NormalizePath(Application.dataPath);

            if (abs.StartsWith(assets, StringComparison.OrdinalIgnoreCase))
            {
                string suffix = abs.Substring(assets.Length).TrimStart('/');
                return "Assets/" + suffix;
            }

            return abs;
        }

        public static bool IsProjectScriptInAssets(string absolutePath)
        {
            if (string.IsNullOrEmpty(absolutePath))
                return false;

            string path = NormalizePath(absolutePath);
            string assetsRoot = NormalizePath(Application.dataPath);

            if (!path.StartsWith(assetsRoot, StringComparison.OrdinalIgnoreCase))
                return false;

            if (path.Contains("/Packages/", StringComparison.OrdinalIgnoreCase))
                return false;

            return path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase);
        }

        // -----------------------
        // Prompt / report
        // -----------------------
        public static void ShowSingleReportPrompt(string reportText)
        {
            // Only ONE prompt after aggregation.
            string title = "C# File Aggregator";
            string message = string.IsNullOrWhiteSpace(reportText) ? "Done." : reportText;

            int choice = EditorUtility.DisplayDialogComplex(
                title,
                message,
                "OK",
                "Copy report to clipboard",
                "Cancel");

            if (choice == 1)
                EditorGUIUtility.systemCopyBuffer = message;
        }

        public static string BuildSinglePromptReport(
            IReadOnlyList<string> rootFiles,
            CSFilesAggregatorDiscovery.DependencyScanResult depScan,
            bool resolveDependencies,
            int dependencyDepth,
            int maxItems)
        {
            StringBuilder sb = new();

            sb.AppendLine("Aggregation completed.");
            sb.AppendLine($"Roots: {rootFiles.Count}");

            if (!resolveDependencies)
            {
                sb.AppendLine("Resolve dependencies: OFF");
                sb.AppendLine("Aggregated output: roots only.");
                return sb.ToString();
            }

            sb.AppendLine("Resolve dependencies: ON");
            sb.AppendLine($"Dependency depth: {dependencyDepth}");
            sb.AppendLine("Dependency contents included: YES");
            sb.AppendLine();

            for (int i = 0; i < rootFiles.Count; i++)
            {
                string rootRel = TryToProjectRelativePath(rootFiles[i]);

                CSFilesAggregatorDiscovery.RootDependencyInfo info = depScan.PerRoot[i];

                List<string> depsRel = info.DependencyFiles.Select(TryToProjectRelativePath).ToList();

                sb.AppendLine($"Root: {rootRel}");
                sb.AppendLine($"Dependencies found: {depsRel.Count}");

                if (depsRel.Count > 0)
                {
                    sb.AppendLine("Dependencies:");
                    AppendLimitedList(sb, depsRel, maxItems, " - ");
                }

                List<string> unresolved = info.UnresolvedCandidates
                    .Where(ShouldShowInReport)
                    .OrderBy(x => x, StringComparer.Ordinal)
                    .ToList();

                List<string> ambiguous = info.AmbiguousCandidates
                    .Where(ShouldShowInReport)
                    .OrderBy(x => x, StringComparer.Ordinal)
                    .ToList();

                if (unresolved.Count > 0)
                {
                    sb.AppendLine($"Unresolved candidates (showing {Math.Min(maxItems, unresolved.Count)}):");
                    AppendLimitedList(sb, unresolved, maxItems, " - ");
                }

                if (ambiguous.Count > 0)
                {
                    sb.AppendLine($"Ambiguous candidates (showing {Math.Min(maxItems, ambiguous.Count)}):");
                    AppendLimitedList(sb, ambiguous, maxItems, " - ");
                }

                sb.AppendLine();
            }

            return sb.ToString();
        }

        private static void AppendLimitedList(StringBuilder sb, List<string> items, int maxItems, string prefix)
        {
            int shown = 0;
            foreach (string item in items)
            {
                sb.AppendLine(prefix + item);
                shown++;
                if (shown >= maxItems)
                    break;
            }

            if (items.Count > shown)
                sb.AppendLine($"{prefix}... ({items.Count - shown} more)");
        }

        private static bool ShouldShowInReport(string candidate)
        {
            if (string.IsNullOrEmpty(candidate))
                return false;

            if (candidate.Length == 1)
                return false;

            // Filter obvious framework noise from the REPORT only (does not affect resolution).
            if (candidate.StartsWith("System", StringComparison.Ordinal) ||
                candidate.StartsWith("Unity", StringComparison.Ordinal) ||
                candidate.StartsWith("Microsoft", StringComparison.Ordinal))
                return false;

            return true;
        }

        // -----------------------
        // Output processing
        // -----------------------
        public static string RemoveUsingDirectives(string rawCode)
        {
            if (string.IsNullOrEmpty(rawCode))
                return string.Empty;

            string[] lines = rawCode.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            IEnumerable<string> filtered =
                lines.Where(l => !l.TrimStart().StartsWith("using ", StringComparison.Ordinal));
            return string.Join("\n", filtered);
        }

        // -----------------------
        // Sanitizer (comments + strings)
        // -----------------------
        public static string StripCommentsAndStrings(string code)
        {
            if (string.IsNullOrEmpty(code))
                return string.Empty;

            StringBuilder sb = new(code.Length);

            bool inLineComment = false;
            bool inBlockComment = false;
            bool inString = false;
            bool inVerbatimString = false;
            bool inChar = false;

            for (int i = 0; i < code.Length; i++)
            {
                char c = code[i];
                char next = i + 1 < code.Length ? code[i + 1] : '\0';

                if (inLineComment)
                {
                    if (c == '\n')
                    {
                        inLineComment = false;
                        sb.Append('\n');
                    }
                    else
                    {
                        sb.Append(' ');
                    }

                    continue;
                }

                if (inBlockComment)
                {
                    if (c == '*' && next == '/')
                    {
                        inBlockComment = false;
                        sb.Append("  ");
                        i++;
                    }
                    else
                    {
                        sb.Append(c == '\n' ? '\n' : ' ');
                    }

                    continue;
                }

                if (inString)
                {
                    if (inVerbatimString)
                    {
                        if (c == '"' && next == '"')
                        {
                            sb.Append("  ");
                            i++;
                            continue;
                        }

                        if (c == '"')
                        {
                            inString = false;
                            inVerbatimString = false;
                            sb.Append(' ');
                            continue;
                        }

                        sb.Append(c == '\n' ? '\n' : ' ');
                        continue;
                    }

                    if (c == '\\')
                    {
                        sb.Append("  ");
                        if (i + 1 < code.Length)
                        {
                            i++;
                            sb.Append(' ');
                        }

                        continue;
                    }

                    if (c == '"')
                    {
                        inString = false;
                        sb.Append(' ');
                        continue;
                    }

                    sb.Append(c == '\n' ? '\n' : ' ');
                    continue;
                }

                if (inChar)
                {
                    if (c == '\\')
                    {
                        sb.Append("  ");
                        if (i + 1 < code.Length)
                        {
                            i++;
                            sb.Append(' ');
                        }

                        continue;
                    }

                    if (c == '\'')
                    {
                        inChar = false;
                        sb.Append(' ');
                        continue;
                    }

                    sb.Append(c == '\n' ? '\n' : ' ');
                    continue;
                }

                if (c == '/' && next == '/')
                {
                    inLineComment = true;
                    sb.Append("  ");
                    i++;
                    continue;
                }

                if (c == '/' && next == '*')
                {
                    inBlockComment = true;
                    sb.Append("  ");
                    i++;
                    continue;
                }

                if (c == '"')
                {
                    inString = true;
                    inVerbatimString = i > 0 && code[i - 1] == '@';
                    sb.Append(' ');
                    continue;
                }

                if (c == '\'')
                {
                    inChar = true;
                    sb.Append(' ');
                    continue;
                }

                sb.Append(c);
            }

            return sb.ToString();
        }

        // -----------------------
        // Candidate extraction (more contexts + better generic parsing)
        // -----------------------
        private static readonly Regex QualifiedIdentifierRegex =
            new(@"\b[A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)*\b", RegexOptions.Compiled);

        private static readonly Regex NewTypeRegex =
            new(@"\bnew\s+([A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)*)\b", RegexOptions.Compiled);

        private static readonly Regex TypeofRegex =
            new(@"\btypeof\s*\(\s*([A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)*)", RegexOptions.Compiled);

        private static readonly Regex NameofRegex =
            new(@"\bnameof\s*\(\s*([A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)*)", RegexOptions.Compiled);

        private static readonly Regex CastRegex =
            new(@"\(\s*([A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)*)\s*(?:<|\)|\s)", RegexOptions.Compiled);

        private static readonly Regex AsRegex =
            new(@"\bas\s+([A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)*)\b", RegexOptions.Compiled);

        private static readonly Regex IsRegex =
            new(@"\bis\s+([A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)*)\b", RegexOptions.Compiled);

        private static readonly Regex CatchTypeRegex =
            new(@"\bcatch\s*\(\s*([A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)*)\s+[A-Za-z_][A-Za-z0-9_]*\s*\)",
                RegexOptions.Compiled);

        private static readonly Regex ForeachTypeRegex =
            new(
                @"\bforeach\s*\(\s*([A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)*)\s+[A-Za-z_][A-Za-z0-9_]*\s+in\b",
                RegexOptions.Compiled);

        private static readonly Regex UsingVarTypeRegex =
            new(@"\busing\s*\(\s*([A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)*)\s+[A-Za-z_][A-Za-z0-9_]*\s*=",
                RegexOptions.Compiled);

        private static readonly Regex EventTypeRegex =
            new(@"\bevent\s+([A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)*)\s+[A-Za-z_][A-Za-z0-9_]*\b",
                RegexOptions.Compiled);

        private static readonly Regex AttributeBracketRegex =
            new(@"\[\s*([^\]]+)\]", RegexOptions.Compiled);

        private static readonly Regex TypeDeclWithBasesRegex =
            new(@"\b(class|struct|interface)\s+[A-Za-z_][A-Za-z0-9_]*\s*:\s*([^{\n\r]+)", RegexOptions.Compiled);

        private static readonly Regex WhereConstraintRegex =
            new(@"\bwhere\s+[A-Za-z_][A-Za-z0-9_]*\s*:\s*([^{\n\r]+)", RegexOptions.Compiled);

        private static readonly Regex DeclarationTypeRegex =
            new(@"\b([A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)*)\s+[A-Za-z_][A-Za-z0-9_]*\s*(?:[;=,{(]|=>)",
                RegexOptions.Compiled);

        private static readonly Regex MethodSignatureLineRegex =
            new(
                @"^\s*(?:public|private|protected|internal|static|virtual|override|abstract|sealed|async|extern|new|\s)+\s*([A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)*)\s+[A-Za-z_][A-Za-z0-9_]*\s*\(([^)]*)\)\s*(?:\{|=>|where|\r?$)",
                RegexOptions.Compiled | RegexOptions.Multiline);

        private static readonly Regex DelegateSignatureLineRegex =
            new(
                @"^\s*delegate\s+([A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)*)\s+[A-Za-z_][A-Za-z0-9_]*\s*\(([^)]*)\)\s*;",
                RegexOptions.Compiled | RegexOptions.Multiline);

        public static HashSet<string> ExtractCandidateTypeNames(string sanitizedCode)
        {
            HashSet<string> result = new(StringComparer.Ordinal);

            if (string.IsNullOrEmpty(sanitizedCode))
                return result;

            AddTypeMatches(result, NewTypeRegex, sanitizedCode);
            AddTypeMatches(result, TypeofRegex, sanitizedCode);
            AddTypeMatches(result, NameofRegex, sanitizedCode);
            AddTypeMatches(result, AsRegex, sanitizedCode);
            AddTypeMatches(result, IsRegex, sanitizedCode);
            AddTypeMatches(result, CastRegex, sanitizedCode);

            AddTypeMatches(result, CatchTypeRegex, sanitizedCode);
            AddTypeMatches(result, ForeachTypeRegex, sanitizedCode);
            AddTypeMatches(result, UsingVarTypeRegex, sanitizedCode);
            AddTypeMatches(result, EventTypeRegex, sanitizedCode);

            ExtractBaseLists(result, sanitizedCode);
            ExtractWhereConstraints(result, sanitizedCode);
            ExtractAttributes(result, sanitizedCode);

            ExtractGenericsFromTypeContexts(result, sanitizedCode);

            ExtractMethodLikeSignatures(result, sanitizedCode);
            ExtractDeclarationTypes(result, sanitizedCode);

            result.RemoveWhere(IsNoiseToken);

            return result;
        }

        private static void AddTypeMatches(HashSet<string> result, Regex regex, string code)
        {
            foreach (Match m in regex.Matches(code))
            {
                if (!m.Success || m.Groups.Count < 2)
                    continue;

                AddType(result, m.Groups[1].Value);
            }
        }

        private static void ExtractMethodLikeSignatures(HashSet<string> result, string code)
        {
            foreach (Match m in MethodSignatureLineRegex.Matches(code))
            {
                if (!m.Success || m.Groups.Count < 3)
                    continue;

                AddType(result, m.Groups[1].Value);
                ExtractParameterTypes(result, m.Groups[2].Value);
            }

            foreach (Match m in DelegateSignatureLineRegex.Matches(code))
            {
                if (!m.Success || m.Groups.Count < 3)
                    continue;

                AddType(result, m.Groups[1].Value);
                ExtractParameterTypes(result, m.Groups[2].Value);
            }
        }

        private static void ExtractDeclarationTypes(HashSet<string> result, string code)
        {
            foreach (Match m in DeclarationTypeRegex.Matches(code))
            {
                if (!m.Success || m.Groups.Count < 2)
                    continue;

                AddType(result, m.Groups[1].Value);
            }
        }

        private static void ExtractParameterTypes(HashSet<string> result, string paramList)
        {
            if (string.IsNullOrWhiteSpace(paramList))
                return;

            foreach (string part in SplitTopLevelCommaList(paramList))
            {
                string p = part.Trim();
                if (p.Length == 0)
                    continue;

                p = RemoveLeadingToken(p, "this");
                p = RemoveLeadingToken(p, "in");
                p = RemoveLeadingToken(p, "ref");
                p = RemoveLeadingToken(p, "out");
                p = RemoveLeadingToken(p, "params");

                p = StripLeadingAttributes(p);

                string typeName = ExtractLeadingTypeName(p);
                if (!string.IsNullOrEmpty(typeName))
                    AddType(result, typeName);

                foreach (string t in ExtractTypesFromGenericText(p))
                {
                    AddType(result, t);
                }
            }
        }

        private static string RemoveLeadingToken(string text, string token)
        {
            text = text.TrimStart();
            if (text.StartsWith(token + " ", StringComparison.Ordinal))
                return text.Substring(token.Length + 1).TrimStart();
            return text;
        }

        private static string StripLeadingAttributes(string text)
        {
            while (true)
            {
                text = text.TrimStart();
                if (!text.StartsWith("[", StringComparison.Ordinal))
                    return text;

                int end = FindMatchingBracket(text, 0, '[', ']');
                if (end < 0)
                    return text;

                text = text.Substring(end + 1);
            }
        }

        private static int FindMatchingBracket(string text, int openIndex, char open, char close)
        {
            int depth = 0;
            for (int i = openIndex; i < text.Length; i++)
            {
                if (text[i] == open)
                {
                    depth++;
                }
                else if (text[i] == close)
                {
                    depth--;
                    if (depth == 0) return i;
                }
            }

            return -1;
        }

        private static void ExtractBaseLists(HashSet<string> result, string code)
        {
            foreach (Match m in TypeDeclWithBasesRegex.Matches(code))
            {
                if (!m.Success || m.Groups.Count < 3)
                    continue;

                string baseList = m.Groups[2].Value;
                foreach (string part in SplitTopLevelCommaList(baseList))
                {
                    string candidate = part.Trim();
                    if (candidate.Length == 0)
                        continue;

                    string typeName = ExtractLeadingTypeName(candidate);
                    if (!string.IsNullOrEmpty(typeName))
                        AddType(result, typeName);

                    foreach (string t in ExtractTypesFromGenericText(candidate))
                    {
                        AddType(result, t);
                    }
                }
            }
        }

        private static void ExtractWhereConstraints(HashSet<string> result, string code)
        {
            foreach (Match m in WhereConstraintRegex.Matches(code))
            {
                if (!m.Success || m.Groups.Count < 2)
                    continue;

                string list = m.Groups[1].Value;

                foreach (string part in SplitTopLevelCommaList(list))
                {
                    string candidate = part.Trim();
                    if (candidate.Length == 0)
                        continue;

                    if (candidate.Equals("new()", StringComparison.Ordinal) ||
                        candidate.Equals("struct", StringComparison.Ordinal) ||
                        candidate.Equals("class", StringComparison.Ordinal) ||
                        candidate.Equals("notnull", StringComparison.Ordinal) ||
                        candidate.Equals("unmanaged", StringComparison.Ordinal))
                        continue;

                    string typeName = ExtractLeadingTypeName(candidate);
                    if (!string.IsNullOrEmpty(typeName))
                        AddType(result, typeName);

                    foreach (string t in ExtractTypesFromGenericText(candidate))
                    {
                        AddType(result, t);
                    }
                }
            }
        }

        private static void ExtractAttributes(HashSet<string> result, string code)
        {
            foreach (Match m in AttributeBracketRegex.Matches(code))
            {
                if (!m.Success || m.Groups.Count < 2)
                    continue;

                string inside = m.Groups[1].Value;

                foreach (string part in SplitTopLevelCommaList(inside))
                {
                    string trimmed = part.Trim();
                    if (trimmed.Length == 0)
                        continue;

                    string attrName = ExtractLeadingTypeName(trimmed);
                    if (attrName.Length == 0)
                        continue;

                    AddAttributeVariants(result, attrName);

                    foreach (string t in ExtractTypesFromGenericText(trimmed))
                    {
                        AddType(result, t);
                    }
                }
            }
        }

        private static void AddAttributeVariants(HashSet<string> result, string attrName)
        {
            string cleaned = CleanupTypeToken(attrName);
            if (cleaned.Length == 0)
                return;

            result.Add(cleaned);

            if (cleaned.EndsWith("Attribute", StringComparison.Ordinal))
            {
                string withoutSuffix = cleaned.Substring(0, cleaned.Length - "Attribute".Length);
                if (withoutSuffix.Length > 0)
                    result.Add(withoutSuffix);
            }
            else
            {
                result.Add(cleaned + "Attribute");
            }
        }

        private static void AddType(HashSet<string> result, string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName))
                return;

            string cleaned = CleanupTypeToken(typeName);
            if (cleaned.Length == 0)
                return;

            result.Add(cleaned);
        }

        private static string CleanupTypeToken(string token)
        {
            token = token.Trim().TrimEnd('?');
            Match m = QualifiedIdentifierRegex.Match(token);
            return m.Success ? m.Value : string.Empty;
        }

        private static string ExtractLeadingTypeName(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            text = text.Trim();

            if (text.StartsWith("in ", StringComparison.Ordinal))
                text = text.Substring(3).Trim();
            else if (text.StartsWith("ref ", StringComparison.Ordinal))
                text = text.Substring(4).Trim();
            else if (text.StartsWith("out ", StringComparison.Ordinal))
                text = text.Substring(4).Trim();

            Match m = QualifiedIdentifierRegex.Match(text);
            return m.Success ? m.Value : string.Empty;
        }

        private static bool IsNoiseToken(string token)
        {
            if (string.IsNullOrEmpty(token))
                return true;

            if (token.Length < 2)
                return true;

            switch (token)
            {
                case "void":
                case "bool":
                case "byte":
                case "sbyte":
                case "short":
                case "ushort":
                case "int":
                case "uint":
                case "long":
                case "ulong":
                case "float":
                case "double":
                case "decimal":
                case "char":
                case "string":
                case "object":
                case "var":
                case "dynamic":
                case "nint":
                case "nuint":
                case "class":
                case "struct":
                case "interface":
                case "enum":
                case "namespace":
                case "public":
                case "private":
                case "protected":
                case "internal":
                case "static":
                case "readonly":
                case "const":
                case "sealed":
                case "partial":
                case "virtual":
                case "override":
                case "abstract":
                case "new":
                case "return":
                case "this":
                case "base":
                case "if":
                case "else":
                case "for":
                case "foreach":
                case "while":
                case "do":
                case "switch":
                case "case":
                case "default":
                case "break":
                case "continue":
                case "try":
                case "catch":
                case "finally":
                case "throw":
                case "using":
                case "typeof":
                case "nameof":
                case "is":
                case "as":
                case "get":
                case "set":
                case "add":
                case "remove":
                case "null":
                case "true":
                case "false":
                case "where":
                case "event":
                case "delegate":
                    return true;

                default:
                    return false;
            }
        }

        // -----------------------
        // Better generic parsing (context-limited + handles >>)
        // -----------------------
        private static void ExtractGenericsFromTypeContexts(HashSet<string> result, string code)
        {
            ExtractGenericsAfterKeyword(result, code, "new");
            ExtractGenericsAfterKeyword(result, code, "typeof");
            ExtractGenericsAfterKeyword(result, code, "nameof");
            ExtractGenericsAfterKeyword(result, code, "as");
            ExtractGenericsAfterKeyword(result, code, "is");
            ExtractGenericsAfterKeyword(result, code, "catch");
            ExtractGenericsAfterKeyword(result, code, "foreach");
            ExtractGenericsAfterKeyword(result, code, "using");
            ExtractGenericsAfterKeyword(result, code, "event");

            ExtractGenericsInsideRegexMatches(result, code, AttributeBracketRegex, 1);
            ExtractGenericsInsideRegexMatches(result, code, TypeDeclWithBasesRegex, 2);
            ExtractGenericsInsideRegexMatches(result, code, WhereConstraintRegex, 1);
        }

        private static void ExtractGenericsAfterKeyword(HashSet<string> result, string code, string keyword)
        {
            int idx = 0;

            while (idx < code.Length)
            {
                idx = code.IndexOf(keyword, idx, StringComparison.Ordinal);
                if (idx < 0)
                    break;

                int windowStart = idx + keyword.Length;
                int windowLen = Math.Min(256, code.Length - windowStart);
                if (windowLen > 0)
                {
                    string window = code.Substring(windowStart, windowLen);
                    foreach (GenericSegment seg in EnumerateGenericSegments(window))
                    {
                        AddType(result, seg.OuterTypeName);
                        foreach (string t in seg.InnerTypeNames)
                        {
                            AddType(result, t);
                        }
                    }
                }

                idx += keyword.Length;
            }
        }

        private static void ExtractGenericsInsideRegexMatches(HashSet<string> result, string code, Regex regex,
            int groupIndex)
        {
            foreach (Match m in regex.Matches(code))
            {
                if (!m.Success || m.Groups.Count <= groupIndex)
                    continue;

                string text = m.Groups[groupIndex].Value;
                foreach (GenericSegment seg in EnumerateGenericSegments(text))
                {
                    AddType(result, seg.OuterTypeName);
                    foreach (string t in seg.InnerTypeNames)
                    {
                        AddType(result, t);
                    }
                }
            }
        }

        private readonly struct GenericSegment
        {
            public GenericSegment(string outerTypeName, List<string> innerTypeNames)
            {
                OuterTypeName = outerTypeName;
                InnerTypeNames = innerTypeNames ?? new List<string>();
            }

            public string OuterTypeName { get; }
            public List<string> InnerTypeNames { get; }
        }

        private static IEnumerable<string> ExtractTypesFromGenericText(string text)
        {
            foreach (GenericSegment seg in EnumerateGenericSegments(text))
            {
                if (!string.IsNullOrEmpty(seg.OuterTypeName))
                    yield return seg.OuterTypeName;

                foreach (string t in seg.InnerTypeNames)
                {
                    yield return t;
                }
            }
        }

        private static IEnumerable<GenericSegment> EnumerateGenericSegments(string code)
        {
            if (string.IsNullOrEmpty(code))
                yield break;

            for (int i = 0; i < code.Length; i++)
            {
                if (!IsIdentifierStart(code[i]))
                    continue;

                int idStart = i;
                int idEnd = i;

                while (idEnd < code.Length && IsQualifiedIdentifierChar(code[idEnd]))
                {
                    idEnd++;
                }

                string outer = code.Substring(idStart, idEnd - idStart);

                int j = idEnd;
                while (j < code.Length && char.IsWhiteSpace(code[j]))
                {
                    j++;
                }

                if (j >= code.Length || code[j] != '<')
                {
                    i = idEnd - 1;
                    continue;
                }

                int ltIndex = j;
                if (!TryFindMatchingAngleBracket(code, ltIndex, out int gtIndex))
                {
                    i = idEnd - 1;
                    continue;
                }

                string inside = code.Substring(ltIndex + 1, gtIndex - ltIndex - 1);
                List<string> innerTypes = ExtractTypeNamesFromGenericInside(inside);

                yield return new GenericSegment(outer, innerTypes);

                i = gtIndex;
            }
        }

        private static bool TryFindMatchingAngleBracket(string text, int ltIndex, out int gtIndex)
        {
            gtIndex = -1;

            int depth = 0;
            for (int i = ltIndex; i < text.Length; i++)
            {
                char c = text[i];

                if (c == '<')
                {
                    depth++;
                    continue;
                }

                if (c == '>')
                {
                    depth--;
                    if (depth == 0)
                    {
                        gtIndex = i;
                        return true;
                    }
                }
            }

            return false;
        }

        private static List<string> ExtractTypeNamesFromGenericInside(string inside)
        {
            List<string> result = new();
            if (string.IsNullOrWhiteSpace(inside))
                return result;

            foreach (string part in SplitTopLevelCommaList(inside))
            {
                string trimmed = part.Trim();
                if (trimmed.Length == 0)
                    continue;

                if (trimmed.StartsWith("in ", StringComparison.Ordinal))
                    trimmed = trimmed.Substring(3).Trim();
                else if (trimmed.StartsWith("out ", StringComparison.Ordinal))
                    trimmed = trimmed.Substring(4).Trim();

                string type = ExtractLeadingTypeName(trimmed);
                if (!string.IsNullOrEmpty(type))
                    result.Add(type);

                // Nested generics
                foreach (string nested in ExtractTypesFromGenericText(trimmed))
                {
                    result.Add(nested);
                }
            }

            return result;
        }

        private static IEnumerable<string> SplitTopLevelCommaList(string text)
        {
            if (string.IsNullOrEmpty(text))
                yield break;

            int depth = 0;
            int start = 0;

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];

                if (c == '<')
                {
                    depth++;
                }
                else if (c == '>')
                {
                    depth = Math.Max(0, depth - 1);
                }
                else if (c == ',' && depth == 0)
                {
                    yield return text.Substring(start, i - start);
                    start = i + 1;
                }
            }

            if (start < text.Length)
                yield return text.Substring(start);
        }

        private static bool IsIdentifierStart(char c)
        {
            return char.IsLetter(c) || c == '_';
        }

        private static bool IsQualifiedIdentifierChar(char c)
        {
            return char.IsLetterOrDigit(c) || c == '_' || c == '.';
        }
    }
}