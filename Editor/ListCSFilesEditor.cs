using UnityEditor;
using UnityEngine;
using System.IO;

namespace LegendaryTools.Editor
{
    public class ListCSFilesEditor : UnityEditor.Editor
    {
        [MenuItem("Tools/LegendaryTools/Automation/List All CS Files")]
        public static void ListAllCSFiles()
        {
            string assetsPath = Application.dataPath;
            string[] csFiles = Directory.GetFiles(assetsPath, "*.cs", SearchOption.AllDirectories);

            // Prepare the output file path in Assets folder
            string outputFilePath = Path.Combine(assetsPath, "CSFilesList.txt");

            using (StreamWriter writer = new StreamWriter(outputFilePath))
            {
                writer.WriteLine("Listing all .cs files relative to Assets folder:");

                foreach (string fullPath in csFiles)
                {
                    // Calculate relative path: remove the Assets path prefix and normalize slashes
                    string relativePath = fullPath.Substring(assetsPath.Length + 1).Replace("\\", "/");
                    writer.WriteLine(relativePath);
                }

                writer.WriteLine("Listing complete.");
            }

            // Refresh the AssetDatabase to make the new file visible in Unity
            AssetDatabase.Refresh();

            Debug.Log("CS files list exported to: " + outputFilePath);
        }
    }
}