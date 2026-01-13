using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace LegendaryTools.Editor
{
    /// <summary>
    /// Unity Editor Window for code analysis. Opens a folder, loads all .cs files and generates reports on type usage.
    /// </summary>
    public class CodeAnalyzerWindow : EditorWindow
    {
        private string folderPath = "";
        private Vector2 scrollPos;
        private string reportOutput = "";

        [MenuItem("Tools/LegendaryTools/Code/Code Analyzer")]
        public static void ShowWindow()
        {
            GetWindow<CodeAnalyzerWindow>("Code Analyzer");
        }

        private void OnGUI()
        {
            GUILayout.Label("Code Analyzer", EditorStyles.boldLabel);

            // Button to select folder containing .cs files
            if (GUILayout.Button("Select Folder"))
                folderPath = EditorUtility.OpenFolderPanel("Select folder with .cs files", "", "");

            if (!string.IsNullOrEmpty(folderPath))
            {
                GUILayout.Label("Selected Folder: " + folderPath);

                // Button to perform analysis
                if (GUILayout.Button("Analyze Code")) AnalyzeCode();
            }

            GUILayout.Label("Report Output", EditorStyles.boldLabel);
            scrollPos = GUILayout.BeginScrollView(scrollPos);
            GUILayout.TextArea(reportOutput, GUILayout.ExpandHeight(true));
            GUILayout.EndScrollView();
        }

        /// <summary>
        /// Main analysis method:
        /// 1. Gathers declared types (classes, structs, enums) from all files.
        /// 2. Processes each class to count usage of each declared type (ignoring primitives and self references).
        /// 3. Aggregates the results into per‑class and global reports.
        /// </summary>
        private void AnalyzeCode()
        {
            // Get all .cs files from folder and subfolders
            string[] csFiles = Directory.GetFiles(folderPath, "*.cs", SearchOption.AllDirectories);

            // Build a set of all declared types in all files
            HashSet<string> declaredTypes = new();
            foreach (string file in csFiles)
            {
                string content = File.ReadAllText(file);
                content = RemoveComments(content);
                // Match class, struct, and enum declarations
                MatchCollection typeMatches = Regex.Matches(content, @"\b(class|struct|enum)\s+(\w+)");
                foreach (Match match in typeMatches)
                {
                    if (match.Groups.Count >= 3) declaredTypes.Add(match.Groups[2].Value);
                }
            }

            // Prepare lists for per-class reports and global aggregation
            List<ClassReport> classReports = new();
            Dictionary<string, int> globalUsage = new();

            // Process each file to generate per-class reports
            foreach (string file in csFiles)
            {
                string content = File.ReadAllText(file);
                content = RemoveComments(content);

                // Find each class declaration
                MatchCollection classMatches = Regex.Matches(content, @"\bclass\s+(\w+)");
                foreach (Match classMatch in classMatches)
                {
                    string className = classMatch.Groups[1].Value;
                    int classDeclarationIndex = classMatch.Index;

                    // Locate the opening brace of the class body
                    int braceIndex = content.IndexOf('{', classDeclarationIndex);
                    if (braceIndex < 0) continue;

                    // Find the matching closing brace to extract the class body
                    int classEndIndex = FindMatchingBrace(content, braceIndex);
                    if (classEndIndex < 0) continue;

                    string classBody = content.Substring(braceIndex, classEndIndex - braceIndex + 1);

                    // Count type usage in the class body (ignoring primitives and self-reference)
                    Dictionary<string, int> typeUsage = CountTypeUsage(classBody, declaredTypes, className);
                    ClassReport cr = new(className, typeUsage, file);
                    classReports.Add(cr);

                    // Aggregate results for the global report
                    foreach (KeyValuePair<string, int> kvp in typeUsage)
                    {
                        if (globalUsage.ContainsKey(kvp.Key))
                            globalUsage[kvp.Key] += kvp.Value;
                        else
                            globalUsage[kvp.Key] = kvp.Value;
                    }
                }
            }

            // Build the output report
            System.Text.StringBuilder sb = new();
            sb.AppendLine("=== Per-Class Type Usage Report ===");
            foreach (ClassReport report in classReports)
            {
                sb.AppendLine(report.GetReport());
            }

            sb.AppendLine("\n=== Global Type Usage Report (Aggregated) ===");
            foreach (KeyValuePair<string, int> kvp in globalUsage.OrderByDescending(x => x.Value))
            {
                sb.AppendLine($"{kvp.Key}: {kvp.Value}");
            }

            reportOutput = sb.ToString();
        }

        /// <summary>
        /// Removes both single-line (//) and multi-line (/* */) comments from the provided code.
        /// </summary>
        private string RemoveComments(string code)
        {
            // Remove multi-line comments
            code = Regex.Replace(code, @"/\*.*?\*/", "", RegexOptions.Singleline);
            // Remove single-line comments
            code = Regex.Replace(code, @"//.*?$", "", RegexOptions.Multiline);
            return code;
        }

        /// <summary>
        /// Counts the occurrences of each declared type within the provided code block.
        /// The current class’s name is skipped to avoid self‑counting.
        /// </summary>
        private Dictionary<string, int> CountTypeUsage(string code, HashSet<string> declaredTypes,
            string currentClassName)
        {
            Dictionary<string, int> usage = new();

            // List of C# primitive types to ignore
            HashSet<string> primitives = new()
            {
                "bool", "byte", "sbyte", "char", "decimal", "double", "float",
                "int", "uint", "long", "ulong", "object", "short", "ushort", "string", "void"
            };

            foreach (string type in declaredTypes)
            {
                if (type == currentClassName) continue; // Skip self reference
                if (primitives.Contains(type)) continue;

                // Use regex to count exact whole word matches for the type
                Regex typeRegex = new(@"\b" + Regex.Escape(type) + @"\b");
                int count = typeRegex.Matches(code).Count;
                if (count > 0) usage[type] = count;
            }

            return usage;
        }

        /// <summary>
        /// Finds the matching closing brace in the text, starting at the given opening brace index.
        /// Returns the index of the matching brace or -1 if not found.
        /// </summary>
        private int FindMatchingBrace(string text, int openingBraceIndex)
        {
            int counter = 0;
            for (int i = openingBraceIndex; i < text.Length; i++)
            {
                if (text[i] == '{')
                    counter++;
                else if (text[i] == '}')
                    counter--;

                if (counter == 0)
                    return i;
            }

            return -1;
        }

        /// <summary>
        /// Simple class to encapsulate a per‑class report.
        /// </summary>
        private class ClassReport
        {
            public string ClassName;
            public Dictionary<string, int> TypeUsage;
            public string FilePath;

            public ClassReport(string className, Dictionary<string, int> typeUsage, string filePath)
            {
                ClassName = className;
                TypeUsage = typeUsage;
                FilePath = filePath;
            }

            /// <summary>
            /// Returns a formatted report string for this class.
            /// </summary>
            public string GetReport()
            {
                System.Text.StringBuilder sb = new();
                sb.AppendLine($"Class: {ClassName} (File: {Path.GetFileName(FilePath)})");
                foreach (KeyValuePair<string, int> kvp in TypeUsage)
                {
                    sb.AppendLine($"   {kvp.Key}: {kvp.Value}");
                }

                return sb.ToString();
            }
        }
    }
}