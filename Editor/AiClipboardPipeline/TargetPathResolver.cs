#if UNITY_EDITOR_WIN
using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace AiClipboardPipeline.Editor
{
    internal sealed class TargetPathResolver
    {
        private static readonly Regex TypeRegex =
            new(@"\b(class|struct|interface|enum)\s+([A-Za-z_][A-Za-z0-9_]*)\b", RegexOptions.Compiled);

        private static readonly Regex NamespaceRegex =
            new(@"\bnamespace\s+([A-Za-z_][A-Za-z0-9_\.]*)\s*[{;]", RegexOptions.Compiled);

        private static readonly Regex FileHeaderRegex =
            new(@"^\s*//\s*File\s*:\s*(.+?)\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public string ResolveTargetAssetPath(string csharpText, string fallbackFolder, out string note)
        {
            note = string.Empty;

            if (TryExtractHeaderFilePath(csharpText, out string headerPath))
            {
                string normalized = NormalizeUnityAssetPath(headerPath);

                if (!normalized.StartsWith("Assets/", StringComparison.Ordinal))
                    normalized = "Assets/" + normalized.TrimStart('/');

                if (!normalized.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                    normalized += ".cs";

                note = "// File header resolved target.";
                return normalized;
            }

            string typeName = ExtractPrimaryTypeName(csharpText);
            if (!string.IsNullOrEmpty(typeName))
            {
                string ns = ExtractPrimaryNamespace(csharpText);
                string found = FindScriptAssetPathByFileName(typeName, ns);
                if (!string.IsNullOrEmpty(found))
                {
                    note = "Resolved by searching existing script file.";
                    return found;
                }
            }

            if (string.IsNullOrEmpty(typeName))
                typeName = "NewFile";

            string folder = string.IsNullOrEmpty(fallbackFolder) ? "Assets/Scripts/Generated/" : fallbackFolder;
            folder = NormalizeUnityAssetPath(folder);

            if (!folder.StartsWith("Assets/", StringComparison.Ordinal))
                folder = "Assets/" + folder.TrimStart('/');

            if (!folder.EndsWith("/", StringComparison.Ordinal))
                folder += "/";

            note = "Resolved by fallback folder (new file).";
            return folder + typeName + ".cs";
        }

        private static string FindScriptAssetPathByFileName(string typeName, string namespaceName)
        {
            string[] guids = AssetDatabase.FindAssets($"{typeName} t:MonoScript");
            string bestPath = string.Empty;
            int bestScore = int.MinValue;

            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (string.IsNullOrEmpty(path))
                    continue;

                if (!path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                    continue;

                string file = Path.GetFileNameWithoutExtension(path);
                if (!string.Equals(file, typeName, StringComparison.Ordinal))
                    continue;

                int score = 100;

                try
                {
                    string abs = ProjectPaths.AssetPathToAbsolute(path);
                    if (File.Exists(abs))
                    {
                        // Read with BOM detection for better compatibility (UTF-8/UTF-16).
                        string content;
                        using (StreamReader sr = new(abs, Encoding.UTF8, true))
                        {
                            content = sr.ReadToEnd();
                        }

                        if (!string.IsNullOrEmpty(namespaceName) &&
                            content.IndexOf("namespace " + namespaceName, StringComparison.Ordinal) >= 0)
                            score += 50;

                        if (content.IndexOf("class " + typeName, StringComparison.Ordinal) >= 0 ||
                            content.IndexOf("struct " + typeName, StringComparison.Ordinal) >= 0 ||
                            content.IndexOf("interface " + typeName, StringComparison.Ordinal) >= 0 ||
                            content.IndexOf("enum " + typeName, StringComparison.Ordinal) >= 0)
                            score += 20;
                    }
                }
                catch
                {
                    // Ignore IO errors.
                }

                bool isBetter =
                    score > bestScore ||
                    (score == bestScore && (string.IsNullOrEmpty(bestPath) || path.Length < bestPath.Length));

                if (isBetter)
                {
                    bestScore = score;
                    bestPath = path;
                }
            }

            return bestPath;
        }

        private static string ExtractPrimaryTypeName(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            Match m = TypeRegex.Match(text);
            if (!m.Success)
                return string.Empty;

            return m.Groups[2].Value;
        }

        private static string ExtractPrimaryNamespace(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            Match m = NamespaceRegex.Match(text);
            return m.Success ? m.Groups[1].Value : string.Empty;
        }

        private static bool TryExtractHeaderFilePath(string text, out string path)
        {
            path = string.Empty;

            if (string.IsNullOrEmpty(text))
                return false;

            string normalized = text.Replace("\r\n", "\n").Replace("\r", "\n");
            string[] lines = normalized.Split('\n');

            int max = Mathf.Min(lines.Length, 50);
            for (int i = 0; i < max; i++)
            {
                Match m = FileHeaderRegex.Match(lines[i]);
                if (!m.Success)
                    continue;

                string raw = m.Groups[1].Value.Trim();
                if (string.IsNullOrEmpty(raw))
                    continue;

                path = raw;
                return true;
            }

            return false;
        }

        private static string NormalizeUnityAssetPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return string.Empty;

            string p = path.Trim().Replace("\\", "/");

            if (p.Length >= 2 && ((p[0] == '"' && p[^1] == '"') || (p[0] == '\'' && p[^1] == '\'')))
                p = p.Substring(1, p.Length - 2).Trim();

            while (p.StartsWith("./", StringComparison.Ordinal))
            {
                p = p.Substring(2);
            }

            int idx = p.IndexOf("/Assets/", StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
                p = "Assets/" + p.Substring(idx + "/Assets/".Length);

            p = p.TrimStart('/');

            return p;
        }
    }
}
#endif