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
        // List of selected folder or file paths
        private List<string> paths = new();

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

            // Button to add a folder or file manually
            if (GUILayout.Button("Add Folder or File"))
            {
                string selectedPath = EditorUtility.OpenFilePanel("Select folder or .cs file", "", "cs");
                if (!string.IsNullOrEmpty(selectedPath))
                {
                    // Convert absolute path to project relative path if possible
                    if (selectedPath.StartsWith(Application.dataPath))
                        selectedPath = "Assets" + selectedPath.Substring(Application.dataPath.Length);
                    
                    if (!paths.Contains(selectedPath))
                        paths.Add(selectedPath);
                }
            }

            // Handle drag-and-drop
            Rect dropArea = GUILayoutUtility.GetRect(0.0f, 50.0f, GUILayout.ExpandWidth(true));
            GUI.Box(dropArea, "Drag and Drop Folders or .cs Files Here");
            
            Event evt = Event.current;
            if (evt.type == EventType.DragUpdated && dropArea.Contains(evt.mousePosition))
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                Event.current.Use();
            }
            else if (evt.type == EventType.DragPerform && dropArea.Contains(evt.mousePosition))
            {
                DragAndDrop.AcceptDrag();
                foreach (string path in DragAndDrop.paths)
                {
                    // Convert absolute path to project relative path if possible
                    string relativePath = path;
                    if (path.StartsWith(Application.dataPath))
                        relativePath = "Assets" + path.Substring(Application.dataPath.Length);

                    // Check if the path is a folder or a .cs file
                    if (Directory.Exists(path) || (File.Exists(path) && path.EndsWith(".cs")))
                    {
                        if (!paths.Contains(relativePath))
                            paths.Add(relativePath);
                    }
                }
                Event.current.Use();
            }

            // Display the list of selected folders and files with remove buttons
            GUILayout.Label("Selected Folders and Files:");
            for (int i = 0; i < paths.Count; i++)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(paths[i]);
                if (GUILayout.Button("Remove", GUILayout.MaxWidth(70)))
                {
                    paths.RemoveAt(i);
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
        /// Method to aggregate the content of .cs files from multiple folders or individual files.
        /// </summary>
        private void AggregateCSFiles()
        {
            if (paths.Count == 0)
            {
                EditorUtility.DisplayDialog("Error", "Please add at least one folder or .cs file.", "OK");
                return;
            }

            StringBuilder sb = new();
            SearchOption searchOption = includeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

            foreach (string path in paths)
            {
                string absolutePath = path;

                // Convert relative path to absolute path if needed
                if (path.StartsWith("Assets/") || path.StartsWith("Assets\\"))
                    absolutePath = Path.Combine(Application.dataPath, path.Substring("Assets".Length + 1));

                // Check if the path is a file or directory
                if (File.Exists(absolutePath) && absolutePath.EndsWith(".cs"))
                {
                    try
                    {
                        // Read the file content
                        string fileContent = File.ReadAllText(absolutePath);

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
                        sb.AppendLine();
                        sb.AppendLine($"// End of file: {path}");
                        sb.AppendLine();
                    }
                    catch (System.Exception ex)
                    {
                        EditorUtility.DisplayDialog("Error",
                            $"An error occurred while processing file {path}: {ex.Message}", "OK");
                    }
                }
                else if (Directory.Exists(absolutePath))
                {
                    if (!Directory.Exists(absolutePath))
                    {
                        EditorUtility.DisplayDialog("Error", $"The folder does not exist: {path}", "OK");
                        continue;
                    }

                    try
                    {
                        // Get all .cs files in the folder (and subfolders if selected)
                        string[] csFiles = Directory.GetFiles(absolutePath, "*.cs", searchOption);

                        if (csFiles.Length == 0)
                        {
                            // Continue to next folder if no .cs files are found here
                            sb.AppendLine($"// No .cs files found in folder: {path}");
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
                            sb.AppendLine();
                        }

                        // Optionally add a comment to separate content from different folders
                        sb.AppendLine($"// End of folder: {path}");
                        sb.AppendLine();
                    }
                    catch (System.Exception ex)
                    {
                        EditorUtility.DisplayDialog("Error",
                            $"An error occurred while aggregating files from folder {path}: {ex.Message}", "OK");
                    }
                }
                else
                {
                    EditorUtility.DisplayDialog("Error", $"Invalid path: {path}. It is neither a folder nor a .cs file.", "OK");
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