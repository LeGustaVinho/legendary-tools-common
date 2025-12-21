using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using LegendaryTools.AttributeSystem;
using UnityEditor;
using UnityEngine;

namespace LegendaryTools.AttributeSystem.Editor
{
    public static class CloneOverrideUpdater
    {
        [MenuItem("Tools/LegendaryTools/AttributeSystem/Update Clone Overrides")]
        public static void UpdateCloneOverrides()
        {
            // Get the base type (EntityConfig).
            Type baseType = typeof(EntityConfig);

            // Find all non-abstract, non-generic types that derive from EntityConfig.
            List<Type> derivedTypes = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(asm =>
                {
                    try
                    {
                        return asm.GetTypes();
                    }
                    catch
                    {
                        return new Type[0];
                    }
                })
                .Where(t => t != baseType && baseType.IsAssignableFrom(t) && !t.IsGenericTypeDefinition &&
                            !t.IsAbstract)
                .ToList();

            int countUpdated = 0;
            foreach (Type type in derivedTypes)
            {
                // Find the source file (MonoScript asset) for this type.
                string assetPath = FindScriptPathForType(type);
                if (string.IsNullOrEmpty(assetPath))
                {
                    Debug.LogWarning($"[CloneOverrideUpdater] Source file not found for type: {type.FullName}");
                    continue;
                }

                // Load the file text.
                string filePath = Path.Combine(Directory.GetCurrentDirectory(), assetPath);
                string fileContent = File.ReadAllText(filePath);

                // Generate the expected override code.
                string generatedMethod = GenerateCloneOverrideCode(type);

                // Update the file text by inserting/updating the clone override inside the class.
                string updatedContent = UpdateFileWithCloneOverride(fileContent, generatedMethod, type.Name);

                if (updatedContent != fileContent)
                {
                    File.WriteAllText(filePath, updatedContent);
                    Debug.Log($"[CloneOverrideUpdater] Updated Clone override in: {assetPath}");
                    countUpdated++;
                }
                else
                {
                    Debug.Log($"[CloneOverrideUpdater] No update needed for: {assetPath}");
                }
            }

            AssetDatabase.Refresh();
            Debug.Log($"[CloneOverrideUpdater] Processed {derivedTypes.Count} types, updated {countUpdated} file(s).");
        }

        /// <summary>
        ///     Searches all MonoScript assets to find the one that defines the given type.
        /// </summary>
        private static string FindScriptPathForType(Type type)
        {
            // We search by type name.
            string[] guids = AssetDatabase.FindAssets(type.Name + " t:Script");
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                MonoScript script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                if (script != null && script.GetClass() == type)
                {
                    return path;
                }
            }

            return null;
        }

        /// <summary>
        ///     Generates the full override method code as a string.
        ///     The code is wrapped with BEGIN/END markers so that it can later be updated.
        /// </summary>
        private static string GenerateCloneOverrideCode(Type type)
        {
            // Assumes one indent level of 4 spaces inside the class.
            const string indent = "    ";
            StringBuilder sb = new StringBuilder();

            sb.AppendLine(indent + "// BEGIN CLONE OVERRIDE");
            sb.AppendLine(indent + "public override T Clone<T>(IEntity parent)");
            sb.AppendLine(indent + "{");
            sb.AppendLine(indent + indent + "T clone = base.Clone<T>(parent);");
            sb.AppendLine(indent + indent + "if(clone is " + type.Name + " castedEntityConfig)");
            sb.AppendLine(indent + indent + "{");

            // Process only the fields declared in THIS class.
            FieldInfo[] fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic |
                                                BindingFlags.DeclaredOnly);
            foreach (FieldInfo field in fields)
            {
                // Skip static fields.
                if (field.IsStatic)
                {
                    continue;
                }

                // Consider public fields or private fields marked with [SerializeField].
                bool isSerializable = field.IsPublic ||
                                      field.GetCustomAttributes(typeof(SerializeField), true).Length > 0;
                if (!isSerializable)
                {
                    continue;
                }

                // Generate the assignment to copy the field.
                sb.AppendLine(indent + indent + indent + $"castedEntityConfig.{field.Name} = this.{field.Name};");
            }

            sb.AppendLine(indent + indent + "}");
            sb.AppendLine(indent + indent + "return clone;");
            sb.AppendLine(indent + "}");
            sb.AppendLine(indent + "// END CLONE OVERRIDE");

            return sb.ToString();
        }

        /// <summary>
        ///     Updates the file content by inserting or updating the clone override method inside the class.
        /// </summary>
        /// <param name="fileContent">The original file text.</param>
        /// <param name="generatedMethod">The generated clone override code.</param>
        /// <param name="typeName">The name of the type whose class block should be modified.</param>
        /// <returns>The updated file content.</returns>
        private static string UpdateFileWithCloneOverride(string fileContent, string generatedMethod, string typeName)
        {
            // Find the class declaration for the given type name.
            int classDeclarationIndex = fileContent.IndexOf("class " + typeName);
            if (classDeclarationIndex == -1)
            {
                Debug.LogWarning($"[CloneOverrideUpdater] Could not find class declaration for {typeName}");
                return fileContent;
            }

            // Locate the opening brace "{" that starts the class body.
            int classBodyStart = fileContent.IndexOf("{", classDeclarationIndex);
            if (classBodyStart == -1)
            {
                Debug.LogWarning($"[CloneOverrideUpdater] Could not find opening brace for class {typeName}");
                return fileContent;
            }

            // Find the matching closing brace "}" for the class using a simple brace-matching algorithm.
            int classBodyEnd = FindMatchingClosingBrace(fileContent, classBodyStart);
            if (classBodyEnd == -1)
            {
                Debug.LogWarning($"[CloneOverrideUpdater] Could not find matching closing brace for class {typeName}");
                return fileContent;
            }

            // Extract the class body for marker lookup.
            string classBody = fileContent.Substring(classBodyStart, classBodyEnd - classBodyStart);
            const string beginMarker = "// BEGIN CLONE OVERRIDE";
            const string endMarker = "// END CLONE OVERRIDE";

            int beginIndexInClass = classBody.IndexOf(beginMarker);
            if (beginIndexInClass != -1)
            {
                // Found an existing clone override block; update it if necessary.
                int beginIndex = classBodyStart + beginIndexInClass;
                int endIndex = fileContent.IndexOf(endMarker, beginIndex);
                if (endIndex != -1)
                {
                    // Move endIndex to the end of the line.
                    int endLine = fileContent.IndexOf("\n", endIndex);
                    if (endLine == -1)
                    {
                        endLine = endIndex;
                    }

                    int blockLength = endLine - beginIndex;
                    string oldBlock = fileContent.Substring(beginIndex, blockLength);

                    // Compare normalized (whitespace removed) versions.
                    if (NormalizeCode(oldBlock) == NormalizeCode(generatedMethod))
                    {
                        return fileContent;
                    }

                    // Replace the old block with the generated method.
                    string newContent = fileContent.Remove(beginIndex, blockLength).Insert(beginIndex, generatedMethod);
                    return newContent;
                }
            }
            else
            {
                // No existing clone override block found. Insert the generated method before the class's closing brace.
                string insertion = "\n" + generatedMethod + "\n";
                fileContent = fileContent.Insert(classBodyEnd, insertion);
                return fileContent;
            }

            return fileContent;
        }

        /// <summary>
        ///     Finds the index of the matching closing brace '}' for the opening brace at openBraceIndex.
        /// </summary>
        private static int FindMatchingClosingBrace(string text, int openBraceIndex)
        {
            int count = 0;
            for (int i = openBraceIndex; i < text.Length; i++)
            {
                if (text[i] == '{')
                {
                    count++;
                }
                else if (text[i] == '}')
                {
                    count--;
                    if (count == 0)
                    {
                        return i;
                    }
                }
            }

            return -1;
        }

        /// <summary>
        ///     Returns a normalized version of the code by removing all whitespace.
        ///     This is used to compare two code blocks.
        /// </summary>
        private static string NormalizeCode(string code)
        {
            return new string(code.Where(c => !char.IsWhiteSpace(c)).ToArray());
        }
    }
}