using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace LegendaryTools.Editor
{
    public class AggregateCodeFiles : EditorWindow
    {
        private List<string> selectedFolders = new();

        [MenuItem("Tools/Aggregate Code from Folders")]
        public static void ShowWindow()
        {
            GetWindow<AggregateCodeFiles>("Aggregate Code");
        }

        private void OnGUI()
        {
            GUILayout.Label("Selected Folders", EditorStyles.boldLabel);

            // Display the list of selected folders
            if (selectedFolders.Count == 0)
                EditorGUILayout.LabelField("No folders selected.");
            else
                for (int i = 0; i < selectedFolders.Count; i++)
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(selectedFolders[i]);
                    if (GUILayout.Button("Remove", GUILayout.MaxWidth(60)))
                    {
                        selectedFolders.RemoveAt(i);
                        i--;
                    }

                    EditorGUILayout.EndHorizontal();
                }

            // Button to add a new folder
            if (GUILayout.Button("Add Folder"))
            {
                string folder = EditorUtility.OpenFolderPanel("Select Folder for Aggregation", Application.dataPath, "");
                if (!string.IsNullOrEmpty(folder)) selectedFolders.Add(folder);
            }

            EditorGUILayout.Space();

            // Button to perform code aggregation
            if (GUILayout.Button("Aggregate Code")) AggregateCode();
        }

        private void AggregateCode()
        {
            if (selectedFolders.Count == 0)
            {
                EditorUtility.DisplayDialog("No Folders Selected", "Please add at least one folder to aggregate code from.",
                    "OK");
                return;
            }

            // Prompt the user to select the destination folder
            string destinationFolder = EditorUtility.OpenFolderPanel("Select Destination Folder", Application.dataPath, "");
            if (string.IsNullOrEmpty(destinationFolder))
            {
                Debug.Log("Destination folder not selected. Operation canceled.");
                return;
            }

            // Gather all .cs files from the selected folders recursively
            List<string> csFiles = new();
            foreach (string folder in selectedFolders)
                csFiles.AddRange(Directory.GetFiles(folder, "*.cs", SearchOption.AllDirectories));

            if (csFiles.Count == 0)
            {
                EditorUtility.DisplayDialog("No Files Found", "No .cs files were found in the selected folders.", "OK");
                return;
            }

            // Create blocks for each file (keeping each file intact)
            List<string> fileBlocks = new();
            foreach (string file in csFiles)
            {
                List<string> blockLines = new();
                blockLines.Add($"// File: {file}");
                string[] lines = File.ReadAllLines(file);
                blockLines.AddRange(lines);
                blockLines.Add(""); // Add an empty line for separation
                string blockContent = string.Join("\n", blockLines);
                fileBlocks.Add(blockContent);
            }

            // Determine how many chunks to create (max 20)
            int totalBlocks = fileBlocks.Count;
            int chunks = Mathf.Min(20, totalBlocks);

            // Evenly distribute file blocks among the chunks without splitting any block
            int blocksPerChunk = totalBlocks / chunks;
            int remainder = totalBlocks % chunks;
            int currentIndex = 0;

            for (int i = 0; i < chunks; i++)
            {
                int numberOfBlocks = blocksPerChunk + (remainder > 0 ? 1 : 0);
                if (remainder > 0)
                    remainder--;

                List<string> chunkBlocks = new();
                for (int j = 0; j < numberOfBlocks; j++)
                    if (currentIndex < totalBlocks)
                    {
                        chunkBlocks.Add(fileBlocks[currentIndex]);
                        currentIndex++;
                    }

                // Join blocks to create the aggregated content for this chunk
                string chunkContent = string.Join("\n", chunkBlocks);
                string outputFilePath = Path.Combine(destinationFolder, $"AggregatedCode_Part{i + 1}.cs");
                File.WriteAllText(outputFilePath, chunkContent);
            }

            // Refresh the AssetDatabase in case the files are inside the Assets folder
            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("Completed", $"Exported {chunks} files to folder:\n{destinationFolder}", "OK");
        }
    }
}