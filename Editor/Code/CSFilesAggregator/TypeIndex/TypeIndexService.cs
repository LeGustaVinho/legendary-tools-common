using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.CodeAnalysis.CSharp;
using UnityEditor;
using UnityEngine;

namespace LegendaryTools.CSFilesAggregator.TypeIndex
{
    /// <summary>
    /// Builds and loads a persistent type index (JSON) stored under the project's Library folder.
    /// </summary>
    public static class TypeIndexService
    {
        private const string LibraryFolderName = "Library";
        private const string IndexFolderRelative = "LegendaryTools/TypeIndex";
        private const string IndexFileName = "type_index.json";

        /// <summary>
        /// Gets the absolute path to the persisted index file in the Library folder.
        /// </summary>
        public static string GetIndexFileAbsolutePath()
        {
            string projectRoot = TypeIndexSettings.instance.GetProjectRootAbsolute();
            string folder = Path.Combine(projectRoot, LibraryFolderName, IndexFolderRelative);
            return Path.Combine(folder, IndexFileName);
        }

        /// <summary>
        /// Loads the index from disk.
        /// </summary>
        /// <returns>The loaded index, or an empty index if none exists.</returns>
        public static TypeIndex Load()
        {
            string indexPath = GetIndexFileAbsolutePath();

            if (!File.Exists(indexPath))
                return new TypeIndex(new TypeIndexData
                {
                    GeneratedAtUtcIso = DateTime.UtcNow.ToString("o")
                });

            try
            {
                string json = File.ReadAllText(indexPath);
                TypeIndexData data = JsonUtility.FromJson<TypeIndexData>(json);
                if (data == null) data = new TypeIndexData { GeneratedAtUtcIso = DateTime.UtcNow.ToString("o") };

                if (string.IsNullOrEmpty(data.GeneratedAtUtcIso))
                    data.GeneratedAtUtcIso = DateTime.UtcNow.ToString("o");

                if (data.Entries == null) data.Entries = new List<TypeIndexEntry>();

                return new TypeIndex(data);
            }
            catch
            {
                return new TypeIndex(new TypeIndexData
                {
                    GeneratedAtUtcIso = DateTime.UtcNow.ToString("o")
                });
            }
        }

        /// <summary>
        /// Rebuilds the index from configured roots and saves it to disk.
        /// </summary>
        /// <returns>The rebuilt index.</returns>
        public static TypeIndex RebuildAndSave()
        {
            TypeIndexSettings settings = TypeIndexSettings.instance;
            string projectRoot = settings.GetProjectRootAbsolute();

            List<string> roots = new(3);
            if (settings.ScanAssets) roots.Add(Path.Combine(projectRoot, "Assets"));

            if (settings.ScanPackages) roots.Add(Path.Combine(projectRoot, "Packages"));

            if (settings.ScanPackageCache) roots.Add(Path.Combine(projectRoot, "Library", "PackageCache"));

            List<TypeIndexEntry> entries = new(4096);

            // Use a conservative parse option. Unity projects vary; "Latest" generally works across Roslyn versions included in Unity.
            CSharpParseOptions parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);

            try
            {
                int totalFiles = CountCsFiles(roots, settings.IgnoreHiddenFolders);
                int processed = 0;

                foreach (string root in roots)
                {
                    foreach (string file in EnumerateCsFiles(root, settings.IgnoreHiddenFolders))
                    {
                        processed++;
                        if (processed % 20 == 0)
                            EditorUtility.DisplayProgressBar(
                                "Building Type Index",
                                $"Scanning {processed}/{Mathf.Max(1, totalFiles)}",
                                totalFiles <= 0 ? 0f : processed / (float)totalFiles);

                        string relativePath = ToProjectRelativePath(projectRoot, file);
                        RoslynTypeScanner.ScanFile(file, relativePath, parseOptions, entries);
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            TypeIndexData data = new()
            {
                Version = 1,
                GeneratedAtUtcIso = DateTime.UtcNow.ToString("o"),
                Entries = entries
            };

            Save(data);

            return new TypeIndex(data);
        }

        /// <summary>
        /// Saves the given index data to disk under the Library folder.
        /// </summary>
        /// <param name="data">The data payload to save.</param>
        public static void Save(TypeIndexData data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));

            string indexPath = GetIndexFileAbsolutePath();
            string folder = Path.GetDirectoryName(indexPath);

            if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);

            string json = JsonUtility.ToJson(data, true);
            File.WriteAllText(indexPath, json);
        }

        private static IEnumerable<string> EnumerateCsFiles(string rootAbsolutePath, bool ignoreHiddenFolders)
        {
            if (string.IsNullOrEmpty(rootAbsolutePath) || !Directory.Exists(rootAbsolutePath)) yield break;

            Stack<string> stack = new();
            stack.Push(rootAbsolutePath);

            while (stack.Count > 0)
            {
                string dir = stack.Pop();

                // Skip hidden folders (e.g., ".git") if requested.
                if (ignoreHiddenFolders)
                {
                    string name = Path.GetFileName(dir);
                    if (!string.IsNullOrEmpty(name) && name.StartsWith(".", StringComparison.Ordinal)) continue;
                }

                string[] subDirs;
                try
                {
                    subDirs = Directory.GetDirectories(dir);
                }
                catch
                {
                    continue;
                }

                for (int i = 0; i < subDirs.Length; i++)
                {
                    stack.Push(subDirs[i]);
                }

                string[] files;
                try
                {
                    files = Directory.GetFiles(dir, "*.cs", SearchOption.TopDirectoryOnly);
                }
                catch
                {
                    continue;
                }

                for (int i = 0; i < files.Length; i++)
                {
                    yield return files[i];
                }
            }
        }

        private static int CountCsFiles(List<string> roots, bool ignoreHiddenFolders)
        {
            int count = 0;

            for (int i = 0; i < roots.Count; i++)
            {
                foreach (string _ in EnumerateCsFiles(roots[i], ignoreHiddenFolders))
                {
                    count++;
                }
            }

            return count;
        }

        private static string ToProjectRelativePath(string projectRootAbsolute, string fileAbsolutePath)
        {
            string fullProjectRoot = Path.GetFullPath(projectRootAbsolute).Replace('\\', '/').TrimEnd('/');
            string fullFile = Path.GetFullPath(fileAbsolutePath).Replace('\\', '/');

            if (fullFile.StartsWith(fullProjectRoot + "/", StringComparison.OrdinalIgnoreCase))
                return fullFile.Substring(fullProjectRoot.Length + 1);

            return fullFile;
        }
    }
}