#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace AiClipboardPipeline.Editor
{
    /// <summary>
    /// Collects Unity compilation errors and logs a consolidated AI-friendly report to Console.
    /// Also writes compilation outcome markers to EditorPrefs to support reliable compile gates.
    /// </summary>
    [InitializeOnLoad]
    public static class AiFriendlyCompilationErrorReporter
    {
        internal static class PrefKeys
        {
            public const string LastCompilationFinishedAtTicks = "AICodePasteHub.Compilation.LastFinishedAtTicks";
            public const string LastCompilationHadErrors = "AICodePasteHub.Compilation.LastHadErrors";
        }

        private static readonly List<CompilerMessage> s_Errors = new(64);

        static AiFriendlyCompilationErrorReporter()
        {
            CompilationPipeline.assemblyCompilationFinished -= OnAssemblyCompilationFinished;
            CompilationPipeline.assemblyCompilationFinished += OnAssemblyCompilationFinished;

            CompilationPipeline.compilationFinished -= OnCompilationFinished;
            CompilationPipeline.compilationFinished += OnCompilationFinished;
        }

        private static void OnAssemblyCompilationFinished(string assemblyPath, CompilerMessage[] messages)
        {
            if (messages == null || messages.Length == 0)
                return;

            for (int i = 0; i < messages.Length; i++)
            {
                if (messages[i].type == CompilerMessageType.Error)
                    s_Errors.Add(messages[i]);
            }
        }

        private static void OnCompilationFinished(object _)
        {
            long nowTicks = DateTime.UtcNow.Ticks;

            bool hadErrors = s_Errors.Count > 0;

            // Compile-gate markers (must be accurate even if we skip logging).
            EditorPrefs.SetString(PrefKeys.LastCompilationFinishedAtTicks, nowTicks.ToString());
            EditorPrefs.SetBool(PrefKeys.LastCompilationHadErrors, hadErrors);

            if (!hadErrors)
            {
                // Clear stale unified compilation error report to prevent false positives in other systems.
#if UNITY_EDITOR_WIN
                ClipboardHistoryStore.instance.SetLastCompilationErrorReport(string.Empty);
#endif
                s_Errors.Clear();
                return;
            }

            // Build report for Console.
            string report = AiFriendlyCompilationErrorFormatter.BuildReport(s_Errors);

            // IMPORTANT:
            // Always log on errors. Time-based throttling can suppress the only useful report,
            // especially during domain reload/fast recompiles.
            Debug.LogError(report);

#if UNITY_EDITOR_WIN
            // Store the unified compilation error report where the Hub expects it.
            ClipboardHistoryStore.instance.SetLastCompilationErrorReport(report);
#endif

            s_Errors.Clear();
        }
    }

    internal static class AiFriendlyCompilationErrorFormatter
    {
        private static readonly Regex TypeRegex =
            new(@"\b(class|struct|interface|enum)\s+([A-Za-z_][A-Za-z0-9_]*)\b", RegexOptions.Compiled);

        // Heuristic method signature matcher (avoids control-flow keywords).
        private static readonly Regex MethodRegex = new(@"\b([A-Za-z_][A-Za-z0-9_]*)\s*\(", RegexOptions.Compiled);

        private static readonly HashSet<string> DisallowedMethodNames = new(StringComparer.Ordinal)
        {
            "if", "for", "foreach", "while", "switch", "catch", "using", "lock", "return", "new", "throw", "checked",
            "unchecked"
        };

        public static string BuildReport(List<CompilerMessage> errors)
        {
            if (errors == null || errors.Count == 0)
                return string.Empty;

            // Group by file to reduce noise.
            Dictionary<string, List<CompilerMessage>> byFile = new(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < errors.Count; i++)
            {
                string file = NormalizeToAssetsPath(errors[i].file);
                if (string.IsNullOrEmpty(file))
                    file = "(unknown file)";

                if (!byFile.TryGetValue(file, out List<CompilerMessage> list))
                {
                    list = new List<CompilerMessage>(8);
                    byFile[file] = list;
                }

                list.Add(errors[i]);
            }

            StringBuilder sb = new(4096);

            sb.AppendLine("Unity Compilation Errors");
            sb.AppendLine("==================================");
            sb.AppendLine();

            int total = 0;

            foreach (KeyValuePair<string, List<CompilerMessage>> kvp in byFile)
            {
                string assetPath = kvp.Key;
                List<CompilerMessage> list = kvp.Value;

                string[] lines = TryReadAllLines(assetPath);
                for (int i = 0; i < list.Count; i++)
                {
                    total++;

                    CompilerMessage msg = list[i];
                    int lineIndex = Mathf.Max(0, msg.line - 1);

                    string typeName = FindEnclosingTypeName(lines, lineIndex);
                    string methodName = FindEnclosingMethodName(lines, lineIndex);

                    string problemLine = GetLineSafe(lines, lineIndex);

                    sb.AppendLine();
                    sb.AppendLine($"[{total}]");
                    sb.AppendLine($"Type   : {(string.IsNullOrEmpty(typeName) ? "(unknown)" : typeName)}");
                    sb.AppendLine($"Method : {(string.IsNullOrEmpty(methodName) ? "(none)" : methodName)}");
                    sb.AppendLine($"File   : {assetPath}");
                    sb.AppendLine($"Error  : {msg.message}");
                    sb.AppendLine("Problem Line:");
                    sb.AppendLine("```");
                    sb.AppendLine(string.IsNullOrEmpty(problemLine) ? "(source not available)" : problemLine);
                    sb.AppendLine("```");
                }

                sb.AppendLine();
            }

            return sb.ToString();
        }

        private static string[] TryReadAllLines(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
                return null;

            if (!assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                return null;

            try
            {
                string abs = AssetPathToAbsolute(assetPath);
                if (!File.Exists(abs))
                    return null;

                string text = File.ReadAllText(abs);
                text = text.Replace("\r\n", "\n").Replace("\r", "\n");
                return text.Split('\n');
            }
            catch
            {
                return null;
            }
        }

        private static string AssetPathToAbsolute(string assetPath)
        {
            string assetsAbs = Application.dataPath.Replace("\\", "/");
            string rel = assetPath.Substring("Assets/".Length);
            return Path.Combine(assetsAbs, rel).Replace("\\", "/");
        }

        private static string GetLineSafe(string[] lines, int lineIndex)
        {
            if (lines == null || lines.Length == 0)
                return string.Empty;

            if (lineIndex < 0 || lineIndex >= lines.Length)
                return string.Empty;

            return lines[lineIndex]?.TrimEnd() ?? string.Empty;
        }

        private static string FindEnclosingTypeName(string[] lines, int lineIndex)
        {
            if (lines == null || lines.Length == 0)
                return string.Empty;

            int start = Mathf.Clamp(lineIndex, 0, lines.Length - 1);

            for (int i = start; i >= 0; i--)
            {
                string line = lines[i];
                if (string.IsNullOrEmpty(line))
                    continue;

                Match m = TypeRegex.Match(line);
                if (m.Success)
                    return m.Groups[2].Value;
            }

            return string.Empty;
        }

        private static string FindEnclosingMethodName(string[] lines, int lineIndex)
        {
            if (lines == null || lines.Length == 0)
                return string.Empty;

            int start = Mathf.Clamp(lineIndex, 0, lines.Length - 1);

            for (int i = start; i >= 0; i--)
            {
                string line = lines[i];
                if (string.IsNullOrEmpty(line))
                    continue;

                string trimmed = line.TrimStart();
                if (trimmed.StartsWith("//", StringComparison.Ordinal))
                    continue;

                Match m = MethodRegex.Match(line);
                if (!m.Success)
                    continue;

                string name = m.Groups[1].Value;
                if (string.IsNullOrEmpty(name))
                    continue;

                if (DisallowedMethodNames.Contains(name))
                    continue;

                return name;
            }

            return string.Empty;
        }

        private static string NormalizeToAssetsPath(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                return string.Empty;

            string p = filePath.Replace("\\", "/").Trim();

            if (p.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                return "Assets/" + p.Substring("Assets/".Length);

            int idx = p.IndexOf("/Assets/", StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
                return "Assets/" + p.Substring(idx + "/Assets/".Length);

            if (p.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
                string fileName = Path.GetFileNameWithoutExtension(p);
                string[] guids = AssetDatabase.FindAssets($"{fileName} t:MonoScript");
                for (int i = 0; i < guids.Length; i++)
                {
                    string ap = AssetDatabase.GUIDToAssetPath(guids[i]);
                    if (ap.EndsWith("/" + fileName + ".cs", StringComparison.OrdinalIgnoreCase))
                        return ap;
                }
            }

            return p;
        }
    }
}
#endif