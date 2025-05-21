using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using LegendaryTools.AttributeSystem;
using UnityEditor;
using UnityEngine;

namespace LegendaryTools.AttributeSystem.Editor
{
    public static class AttributeEnumGenerator
    {
        /// <summary>
        ///     Scans all AttributeConfig assets and creates/updates an enum for each one that defines options.
        /// </summary>
        [MenuItem("Tools/LegendaryTools/AttributeSystem/Generate AttributeConfig Enums")]
        public static void GenerateAttributeConfigEnums()
        {
            // Find all AttributeConfig assets.
            string[] guids = AssetDatabase.FindAssets("t:AttributeConfig");
            foreach (string guid in guids)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                AttributeConfig config = AssetDatabase.LoadAssetAtPath<AttributeConfig>(assetPath);

                // Only process if the config is valid and has nonempty Options.
                if (config == null || config.Data == null || config.Data.Options == null ||
                    config.Data.Options.Length == 0)
                {
                    continue;
                }

                // Derive the enum name by sanitizing the asset name and ensuring it ends with "Options".
                string enumName = SanitizeIdentifier(config.name);
                if (!enumName.EndsWith("Options"))
                {
                    enumName += "Options";
                }

                bool isFlags = config.Data.OptionsAreFlags;

                // Build the enum members string.
                string enumMembers = "";
                string[] options = config.Data.Options;
                HashSet<string> usedMemberNames = new HashSet<string>();
                for (int i = 0; i < options.Length; i++)
                {
                    string option = options[i];
                    string memberName = SanitizeIdentifier(option);
                    if (string.IsNullOrEmpty(memberName))
                    {
                        memberName = "Option" + i;
                    }

                    // Ensure unique member names.
                    if (usedMemberNames.Contains(memberName))
                    {
                        memberName += "_" + i;
                    }

                    usedMemberNames.Add(memberName);

                    // Use a power of two if the enum is flagged; otherwise, use the index.
                    int value = isFlags ? 1 << i : i;
                    enumMembers += $"        {memberName} = {value},\n";
                }

                // Step 1: Search the project for an existing enum file by name.
                string existingFilePath = FindEnumSourceFilePath(enumName);
                string existingNamespace = "";
                string newContent = "";

                if (!string.IsNullOrEmpty(existingFilePath))
                {
                    // Step 2: An existing file was found.
                    existingNamespace = GetExistingNamespace(existingFilePath);
                    newContent = BuildEnumContent(enumName, enumMembers, isFlags, existingNamespace);

                    string currentContent = File.ReadAllText(existingFilePath);
                    if (currentContent != newContent)
                    {
                        File.WriteAllText(existingFilePath, newContent);
                        Debug.Log($"Updated enum: {enumName} at {existingFilePath}");
                    }
                    else
                    {
                        Debug.Log($"Enum {enumName} is already up to date at {existingFilePath}");
                    }
                }
                else
                {
                    // Step 3: No existing enum file was found. Create a new one.
                    string outputFolder = "Assets/Generated/Enums";
                    if (!Directory.Exists(outputFolder))
                    {
                        Directory.CreateDirectory(outputFolder);
                    }

                    string filePath = Path.Combine(outputFolder, enumName + ".cs");

                    // No namespace is preserved for new files.
                    newContent = BuildEnumContent(enumName, enumMembers, isFlags, "");
                    File.WriteAllText(filePath, newContent);
                    Debug.Log($"Generated enum: {enumName} at {filePath}");
                }
            }

            AssetDatabase.Refresh();
            Debug.Log("AttributeConfig enum generation complete.");
        }

        /// <summary>
        ///     Sanitizes a string to be a valid C# identifier by removing invalid characters.
        ///     If the first character is not a letter or underscore, prefixes an underscore.
        /// </summary>
        private static string SanitizeIdentifier(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return "";
            }

            string sanitized = "";
            foreach (char c in input)
            {
                if (char.IsLetterOrDigit(c) || c == '_')
                {
                    sanitized += c;
                }
            }

            if (!string.IsNullOrEmpty(sanitized) && !char.IsLetter(sanitized[0]) && sanitized[0] != '_')
            {
                sanitized = "_" + sanitized;
            }

            return sanitized;
        }

        /// <summary>
        ///     Searches all C# files in the project for an enum with the specified name.
        ///     Returns the file path (relative to Assets) if found; otherwise, returns null.
        /// </summary>
        private static string FindEnumSourceFilePath(string enumName)
        {
            // Get all .cs files in the project under the Assets folder.
            string[] csFiles = Directory.GetFiles(Application.dataPath, "*.cs", SearchOption.AllDirectories);
            foreach (string file in csFiles)
            {
                string fileContent = File.ReadAllText(file);
                // Look for a pattern like: "enum EnumName"
                if (Regex.IsMatch(fileContent, @"\benum\s+" + Regex.Escape(enumName) + @"\b"))
                {
                    // Convert the absolute path to a relative path (starting with "Assets").
                    string relativePath = "Assets" + file.Substring(Application.dataPath.Length);
                    return relativePath;
                }
            }

            return null;
        }

        /// <summary>
        ///     If the file at the given path declares a namespace, returns it; otherwise, returns an empty string.
        /// </summary>
        private static string GetExistingNamespace(string filePath)
        {
            if (!File.Exists(filePath))
            {
                return "";
            }

            string content = File.ReadAllText(filePath);
            Match match = Regex.Match(content, @"namespace\s+([^\s{]+)");
            if (match.Success && match.Groups.Count > 1)
            {
                return match.Groups[1].Value;
            }

            return "";
        }

        /// <summary>
        ///     Builds the full enum file content.
        ///     If a namespace is provided, the enum is wrapped inside that namespace.
        /// </summary>
        private static string BuildEnumContent(string enumName, string enumMembers, bool isFlags, string ns)
        {
            string enumAttribute = isFlags ? "[System.Flags]\n    " : "";
            string content = "// Auto-generated enum file. Do not modify manually.\n";
            content += "using System;\n\n";
            if (!string.IsNullOrEmpty(ns))
            {
                content += $"namespace {ns}\n{{\n";
                content += $"    {enumAttribute}public enum {enumName}\n    {{\n";
                content += enumMembers;
                content += "    }\n";
                content += "}\n";
            }
            else
            {
                content += $"{enumAttribute}public enum {enumName}\n{{\n";
                content += enumMembers;
                content += "}\n";
            }

            return content;
        }
    }
}