using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace LegendaryTools.Editor
{
    public class CSFilesAggregator : EditorWindow
    {
        // List of selected folder paths
        private List<string> folderPaths = new();

        // Toggle to include subfolders
        private bool includeSubfolders = false;

        // Toggle to remove 'using' declarations
        private bool removeUsings = false;

        // Aggregated text from .cs files
        private string aggregatedText = "";

        // Adds an option in the Unity menu to open the Editor window
        [MenuItem("Tools/LegendaryTools/Code/C# File Aggregator")]
        public static void ShowWindow()
        {
            // Creates or focuses the existing window
            GetWindow<CSFilesAggregator>("C# File Aggregator");
        }

        private void OnGUI()
        {
            GUILayout.Label("Settings", EditorStyles.boldLabel);

            // Button to add a folder to the list
            if (GUILayout.Button("Add Folder"))
            {
                string selectedPath = EditorUtility.OpenFolderPanel("Select folder containing .cs files", "", "");
                if (!string.IsNullOrEmpty(selectedPath))
                {
                    // Convert absolute path to project relative path if possible
                    if (selectedPath.StartsWith(Application.dataPath))
                        selectedPath = "Assets" + selectedPath.Substring(Application.dataPath.Length);

                    folderPaths.Add(selectedPath);
                }
            }

            // Display the list of selected folders with remove buttons
            GUILayout.Label("Selected Folders:");
            for (int i = 0; i < folderPaths.Count; i++)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(folderPaths[i]);
                if (GUILayout.Button("Remove", GUILayout.MaxWidth(70)))
                {
                    folderPaths.RemoveAt(i);
                    i--;
                }

                GUILayout.EndHorizontal();
            }

            GUILayout.Space(10);

            // Toggle to include subfolders
            includeSubfolders = EditorGUILayout.Toggle("Include subfolders", includeSubfolders);

            // Toggle to remove 'using' declarations
            removeUsings = EditorGUILayout.Toggle("Remove 'using' declarations", removeUsings);

            GUILayout.Space(10);

            // Button to start the aggregation of files
            if (GUILayout.Button("Aggregate .cs Files")) AggregateCSFiles();

            GUILayout.Space(10);
            GUILayout.Label("Aggregated Content", EditorStyles.boldLabel);

            // Text area to display the aggregated content (expands to fill the window)
            aggregatedText = EditorGUILayout.TextArea(aggregatedText, GUILayout.ExpandHeight(true));
        }

        /// <summary>
        /// Method to aggregate the content of .cs files from multiple folders.
        /// </summary>
        private void AggregateCSFiles()
        {
            if (folderPaths.Count == 0)
            {
                EditorUtility.DisplayDialog("Error", "Please add at least one folder.", "OK");
                return;
            }

            StringBuilder sb = new();
            SearchOption searchOption = includeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

            foreach (string folderPath in folderPaths)
            {
                string absolutePath = "";

                // Convert relative path to absolute path if needed
                if (folderPath.StartsWith("Assets/") || folderPath.StartsWith("Assets\\"))
                    absolutePath = Path.Combine(Application.dataPath, folderPath.Substring("Assets".Length + 1));
                else
                    absolutePath = folderPath;

                if (!Directory.Exists(absolutePath))
                {
                    EditorUtility.DisplayDialog("Error", $"The folder does not exist: {folderPath}", "OK");
                    continue;
                }

                try
                {
                    // Get all .cs files in the folder (and subfolders if selected)
                    string[] csFiles = Directory.GetFiles(absolutePath, "*.cs", searchOption);

                    if (csFiles.Length == 0)
                    {
                        // Continue to next folder if no .cs files are found here
                        sb.AppendLine($"// No .cs files found in folder: {folderPath}");
                        sb.AppendLine();
                        continue;
                    }

                    foreach (string file in csFiles)
                    {
                        // Read the file content
                        string fileContent = File.ReadAllText(file);

                        if (removeUsings)
                        {
                            // Remove lines that start with 'using'
                            string[] lines =
                                fileContent.Split(new[] { "\r\n", "\r", "\n" }, System.StringSplitOptions.None);
                            IEnumerable<string> filteredLines = lines.Where(line => !line.TrimStart().StartsWith("using "));
                            fileContent = string.Join("\n", filteredLines);
                        }

                        // Append the processed content to the StringBuilder
                        sb.AppendLine(fileContent);
                        // Add an empty line to separate files
                        sb.AppendLine();
                    }

                    // Optionally add a comment to separate content from different folders
                    sb.AppendLine($"// End of folder: {folderPath}");
                    sb.AppendLine();
                }
                catch (System.Exception ex)
                {
                    EditorUtility.DisplayDialog("Error",
                        $"An error occurred while aggregating files from folder {folderPath}: {ex.Message}", "OK");
                }
            }

            // Update the text area with the aggregated content
            aggregatedText = sb.ToString();
            // Optionally copy the content to the system clipboard
            EditorGUIUtility.systemCopyBuffer = aggregatedText;

            EditorUtility.DisplayDialog("Success", "The .cs files have been aggregated successfully!", "OK");
        }
    }
}