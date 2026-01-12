#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace AiClipboardPipeline.Editor
{
    /// <summary>
    /// Collects Unity compilation errors and writes a consolidated AI-friendly report to ClipboardHistoryStore.LastErrorReport.
    /// </summary>
    [InitializeOnLoad]
    public static class AiFriendlyCompilationErrorReporter
    {
        private const string PrefAutoCopyError = "AICodePasteHub.AutoCopyError";
        private const string PrefLogErrorToConsole = "AICodePasteHub.LogErrorToConsole";

        private static readonly List<CompilerMessage> s_Errors = new(64);
        private static double s_LastWriteTime;

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
            if (s_Errors.Count == 0)
                return;

            // Avoid spamming the report too frequently in quick successive compiles.
            double now = EditorApplication.timeSinceStartup;
            if (now - s_LastWriteTime < 0.25)
            {
                s_Errors.Clear();
                return;
            }

            s_LastWriteTime = now;

            string report = AiFriendlyCompilationErrorFormatter.BuildReport(s_Errors);

            // Persist into the shared store so the window shows it.
#if UNITY_EDITOR_WIN
            ClipboardHistoryStore.instance.SetLastErrorReport(report);
#else
            // If you later support other platforms, you can store it elsewhere.
#endif

            // Optional behavior driven by existing prefs.
            bool autoCopy = EditorPrefs.GetBool(PrefAutoCopyError, true);
            bool logToConsole = EditorPrefs.GetBool(PrefLogErrorToConsole, true);

            if (autoCopy)
                EditorGUIUtility.systemCopyBuffer = report;

            if (logToConsole)
                Debug.LogError(report);

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

            sb.AppendLine("AI-Friendly Unity Compilation Errors");
            sb.AppendLine("==================================");
            sb.AppendLine();
            sb.AppendLine("Format:");
            sb.AppendLine("- Type: class/struct/interface/enum name (best-effort)");
            sb.AppendLine("- Method: method name (best-effort)");
            sb.AppendLine("- File: Assets-relative path");
            sb.AppendLine("- Error: compiler message");
            sb.AppendLine("- Stack: up to 3 lines (if available)");
            sb.AppendLine("- Problem Line: exact source line (no line/column)");
            sb.AppendLine();

            int total = 0;

            foreach (KeyValuePair<string, List<CompilerMessage>> kvp in byFile)
            {
                string assetPath = kvp.Key;
                List<CompilerMessage> list = kvp.Value;

                sb.AppendLine($"FILE: {assetPath}");
                sb.AppendLine(new string('-', Mathf.Clamp(assetPath.Length + 6, 18, 80)));

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

                    // Stack trace best-effort: compile messages usually don't have one, so we try to fetch from Console logs.
                    string[] stack = TryFindStackTraceLines(assetPath, msg.message);
                    sb.AppendLine("Stack  :");
                    if (stack.Length == 0)
                        sb.AppendLine("  (not available for compiler errors)");
                    else
                        for (int s = 0; s < stack.Length; s++)
                        {
                            sb.AppendLine($"  {stack[s]}");
                        }

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

                // Keep it simple and safe: split into lines without heavy allocations.
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
            // Application.dataPath => "<project>/Assets"
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

                // Skip common non-method lines quickly.
                string trimmed = line.TrimStart();
                if (trimmed.StartsWith("//", StringComparison.Ordinal))
                    continue;

                // Heuristic: find "Name(" and exclude keywords.
                Match m = MethodRegex.Match(line);
                if (!m.Success)
                    continue;

                string name = m.Groups[1].Value;
                if (string.IsNullOrEmpty(name))
                    continue;

                if (DisallowedMethodNames.Contains(name))
                    continue;

                // Avoid matching constructors in weird contexts isn't necessary; constructors are fine.
                return name;
            }

            return string.Empty;
        }

        private static string NormalizeToAssetsPath(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                return string.Empty;

            string p = filePath.Replace("\\", "/").Trim();

            // If already Assets-relative.
            if (p.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                return "Assets/" + p.Substring("Assets/".Length);

            // Try locate "/Assets/" segment in absolute path.
            int idx = p.IndexOf("/Assets/", StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
                return "Assets/" + p.Substring(idx + "/Assets/".Length);

            // Unity can sometimes give just a file name; try resolve it via AssetDatabase (best-effort).
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

        private static string[] TryFindStackTraceLines(string assetPath, string message)
        {
            // Compiler errors typically do not have a runtime stack trace.
            // Best-effort: scrape Console stack if available.
            try
            {
                // UnityEditorInternal is internal API; use carefully.
                Type logEntriesType = Type.GetType("UnityEditor.LogEntries,UnityEditor");
                Type logEntryType = Type.GetType("UnityEditor.LogEntry,UnityEditor");
                if (logEntriesType == null || logEntryType == null)
                    return Array.Empty<string>();

                MethodInfo getCount = logEntriesType.GetMethod("GetCount",
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
                MethodInfo getEntryInternal = logEntriesType.GetMethod("GetEntryInternal",
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
                if (getCount == null || getEntryInternal == null)
                    return Array.Empty<string>();

                int count = (int)getCount.Invoke(null, null);
                if (count <= 0)
                    return Array.Empty<string>();

                object logEntry = Activator.CreateInstance(logEntryType);

                // Search backwards (most recent first), scan a limited window.
                int scan = Mathf.Min(count, 250);
                for (int i = 0; i < scan; i++)
                {
                    int idx = count - 1 - i;
                    getEntryInternal.Invoke(null, new object[] { idx, logEntry });

                    // logEntry.condition, logEntry.stackTrace
                    string condition = (string)logEntryType.GetField("condition").GetValue(logEntry);
                    string stackTrace = (string)logEntryType.GetField("stackTrace").GetValue(logEntry);

                    if (string.IsNullOrEmpty(condition))
                        continue;

                    // Try a loose match on file + message fragment.
                    bool fileMatch = !string.IsNullOrEmpty(assetPath) &&
                                     condition.IndexOf(assetPath, StringComparison.OrdinalIgnoreCase) >= 0;
                    bool msgMatch = !string.IsNullOrEmpty(message) &&
                                    condition.IndexOf(message, StringComparison.OrdinalIgnoreCase) >= 0;

                    if (!fileMatch && !msgMatch)
                        continue;

                    if (string.IsNullOrEmpty(stackTrace))
                        return Array.Empty<string>();

                    stackTrace = stackTrace.Replace("\r\n", "\n").Replace("\r", "\n");
                    string[] lines = stackTrace.Split('\n');

                    List<string> result = new(3);
                    for (int l = 0; l < lines.Length && result.Count < 3; l++)
                    {
                        string s = lines[l]?.Trim();
                        if (string.IsNullOrEmpty(s))
                            continue;

                        result.Add(s);
                    }

                    return result.ToArray();
                }
            }
            catch
            {
                // Ignore any reflection/internal API failures.
            }

            return Array.Empty<string>();
        }
    }
}
#endif