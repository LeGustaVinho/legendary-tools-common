using System;
using UnityEngine;
using System.IO;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Linq;
using Newtonsoft.Json;
using System.Threading;
using UnityEditor;

namespace LegendaryTools.Editor
{
    /// <summary>
    /// A utility class for mapping and managing Unity asset GUIDs across project files.
    /// </summary>
    public class AssetGuidMapper
    {
        /// <summary>
        /// Dictionary mapping file paths to lists of GUIDs found in those files.
        /// </summary>
        private ConcurrentDictionary<string, List<string>> guidMap = new ConcurrentDictionary<string, List<string>>();

        /// <summary>
        /// Inverted index mapping GUIDs to lists of file paths containing those GUIDs.
        /// </summary>
        private ConcurrentDictionary<string, List<string>> guidToFilesMap = new ConcurrentDictionary<string, List<string>>();

        /// <summary>
        /// The root path of the Unity project (Assets folder).
        /// </summary>
        private readonly string projectPath = Application.dataPath;

        /// <summary>
        /// Regular expression for identifying 32-character hexadecimal GUIDs.
        /// </summary>
        private readonly Regex guidRegex;

        /// <summary>
        /// Maximum number of parallel tasks based on processor count.
        /// </summary>
        private readonly int maxDegreeOfParallelism = Environment.ProcessorCount;

        /// <summary>
        /// File extensions to consider for GUID mapping.
        /// </summary>
        private readonly string[] relevantExtensions = { ".prefab", ".asset", ".unity", ".mat", ".anim", ".controller", ".shader", ".meta" };

        /// <summary>
        /// Tracks progress of the mapping process (0 to 1).
        /// </summary>
        private float progress = 0f;

        /// <summary>
        /// Total number of files processed during mapping.
        /// </summary>
        private int totalFilesProcessed = 0;

        /// <summary>
        /// Queue for dispatching actions to the main thread for Editor UI updates.
        /// </summary>
        private static readonly ConcurrentQueue<Action> mainThreadActions = new ConcurrentQueue<Action>();

        /// <summary>
        /// Flag indicating if the main thread update callback is registered.
        /// </summary>
        private static bool isUpdateRegistered = false;

        /// <summary>
        /// Initializes a new instance of the AssetGuidMapper class.
        /// </summary>
        public AssetGuidMapper()
        {
            guidRegex = new Regex(@"[0-9a-fA-F]{32}", RegexOptions.Compiled);
            RegisterMainThreadUpdate();
        }

        /// <summary>
        /// Registers the main thread update callback for processing UI actions.
        /// </summary>
        private static void RegisterMainThreadUpdate()
        {
            if (!isUpdateRegistered)
            {
                EditorApplication.update += ProcessMainThreadActions;
                isUpdateRegistered = true;
            }
        }

        /// <summary>
        /// Processes queued actions on the main thread for Editor UI updates.
        /// </summary>
        private static void ProcessMainThreadActions()
        {
            while (mainThreadActions.TryDequeue(out var action))
            {
                action?.Invoke();
            }
        }

        /// <summary>
        /// Asynchronously maps GUIDs in project files with specified extensions.
        /// </summary>
        /// <param name="fileExtensions">Array of file extensions to process.</param>
        /// <param name="progressCallback">Callback for reporting progress.</param>
        /// <param name="cancellationToken">Token to cancel the operation.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task MapProjectGUIDsAsync(string[] fileExtensions, Action<float, string> progressCallback = null, CancellationToken cancellationToken = default)
        {
            guidMap.Clear();
            guidToFilesMap.Clear();
            progress = 0f;
            totalFilesProcessed = 0;

            // Validate and prepare file extensions
            List<string> validExtensions = fileExtensions.Select(ext => ext.StartsWith(".") ? ext : $".{ext}").ToList();
            List<string> allFiles = validExtensions
                .SelectMany(ext => Directory.GetFiles(projectPath, $"*{ext}", SearchOption.AllDirectories))
                .Select(f => "Assets" + f.Substring(projectPath.Length).Replace("\\", "/"))
                .ToList();

            // Split files into batches for parallel processing
            int batchSize = Mathf.Max(1, allFiles.Count / maxDegreeOfParallelism, 100);
            List<List<string>> batches = SplitIntoBatches(allFiles, batchSize);

            // Initial progress update
            mainThreadActions.Enqueue(() =>
            {
                EditorUtility.DisplayCancelableProgressBar("Mapping GUIDs", "Starting mapping...", 0f);
            });
            progressCallback?.Invoke(0f, "Starting mapping...");

            // Process batches in parallel
            try
            {
                await Task.WhenAll(batches.Select(batch => Task.Run(async () => await ProcessBatchAsync(batch, allFiles.Count, progressCallback, cancellationToken), cancellationToken)));
            }
            catch (OperationCanceledException)
            {
                mainThreadActions.Enqueue(() => EditorUtility.ClearProgressBar());
                Debug.Log("Mapping cancelled by user.");
                return;
            }

            mainThreadActions.Enqueue(() => EditorUtility.ClearProgressBar());
            progressCallback?.Invoke(1f, "Mapping completed.");
            Debug.Log($"Mapping completed. Total files processed: {guidMap.Count}");
        }

        /// <summary>
        /// Processes a batch of files asynchronously to extract GUIDs.
        /// </summary>
        /// <param name="filePaths">List of file paths to process.</param>
        /// <param name="totalFiles">Total number of files for progress calculation.</param>
        /// <param name="progressCallback">Callback for reporting progress.</param>
        /// <param name="cancellationToken">Token to cancel the operation.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        private async Task ProcessBatchAsync(List<string> filePaths, int totalFiles, Action<float, string> progressCallback, CancellationToken cancellationToken)
        {
            int filesProcessedInBatch = 0;
            foreach (string filePath in filePaths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    // Use full path directly
                    string fullPath = Path.Combine(projectPath, filePath.Substring("Assets".Length).TrimStart('/'));
                    if (!File.Exists(fullPath))
                    {
                        Debug.LogWarning($"File not found: {fullPath}");
                        continue;
                    }

                    // Asynchronous file reading
                    string content = await File.ReadAllTextAsync(fullPath, cancellationToken).ConfigureAwait(false);
                    List<string> foundGuids = guidRegex.Matches(content).Select(m => m.Value).Distinct().ToList();

                    // Add to mapping
                    guidMap.TryAdd(filePath, foundGuids);

                    // Update inverted index
                    foreach (string guid in foundGuids)
                    {
                        guidToFilesMap.AddOrUpdate(guid, new List<string> { filePath }, (key, oldList) =>
                        {
                            oldList.Add(filePath);
                            return oldList;
                        });
                    }

                    // Update progress
                    Interlocked.Increment(ref totalFilesProcessed);
                    filesProcessedInBatch++;
                    if (filesProcessedInBatch % 10 == 0) // Update every 10 files to reduce UI overhead
                    {
                        progress = (float)totalFilesProcessed / totalFiles;
                        string message = $"Processing {filePath}";
                        progressCallback?.Invoke(progress, message);

                        // Queue progress bar update on main thread
                        mainThreadActions.Enqueue(() =>
                        {
                            if (EditorUtility.DisplayCancelableProgressBar("Mapping GUIDs", message, progress))
                            {
                                cancellationToken.ThrowIfCancellationRequested();
                            }
                        });
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"Error processing file {filePath}: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Saves the GUID mapping to a JSON file.
        /// </summary>
        /// <param name="outputPath">Path to save the JSON file.</param>
        public void SaveMappingToJson(string outputPath)
        {
            try
            {
                using (var stream = new FileStream(outputPath, FileMode.Create))
                using (var writer = new StreamWriter(stream))
                using (var jsonWriter = new JsonTextWriter(writer))
                {
                    JsonSerializer serializer = new JsonSerializer { Formatting = Formatting.Indented };
                    serializer.Serialize(jsonWriter, guidMap);
                }
                Debug.Log($"Mapping saved to: {outputPath}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error saving mapping: {ex.Message}");
            }
        }

        /// <summary>
        /// Loads the GUID mapping from a JSON file.
        /// </summary>
        /// <param name="inputPath">Path to the JSON file.</param>
        /// <returns>True if loading was successful, false otherwise.</returns>
        public bool LoadMappingFromJson(string inputPath)
        {
            try
            {
                if (!File.Exists(inputPath))
                {
                    return false;
                }

                using (var stream = new FileStream(inputPath, FileMode.Open))
                using (var reader = new StreamReader(stream))
                using (var jsonReader = new JsonTextReader(reader))
                {
                    JsonSerializer serializer = new JsonSerializer();
                    var loadedMap = serializer.Deserialize<Dictionary<string, List<string>>>(jsonReader);
                    guidMap = new ConcurrentDictionary<string, List<string>>(loadedMap);

                    // Rebuild inverted index
                    guidToFilesMap.Clear();
                    foreach (var entry in guidMap)
                    {
                        foreach (string guid in entry.Value)
                        {
                            guidToFilesMap.AddOrUpdate(guid, new List<string> { entry.Key }, (key, oldList) =>
                            {
                                oldList.Add(entry.Key);
                                return oldList;
                            });
                        }
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error loading mapping: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Splits a list of files into batches for parallel processing.
        /// </summary>
        /// <param name="files">List of file paths.</param>
        /// <param name="batchSize">Size of each batch.</param>
        /// <returns>List of file path batches.</returns>
        private List<List<string>> SplitIntoBatches(List<string> files, int batchSize)
        {
            List<List<string>> batches = new List<List<string>>();
            for (int i = 0; i < files.Count; i += batchSize)
            {
                batches.Add(files.GetRange(i, Math.Min(batchSize, files.Count - i)));
            }
            return batches;
        }

        /// <summary>
        /// Checks if a file contains a specific GUID asynchronously.
        /// </summary>
        /// <param name="filePathOrGuid">File path or GUID to check.</param>
        /// <param name="searchGuid">GUID to search for.</param>
        /// <returns>True if the file contains the GUID, false otherwise.</returns>
        public async Task<bool> FileContainsGuidAsync(string filePathOrGuid, string searchGuid)
        {
            // Convert GUID to path if necessary
            string filePath = filePathOrGuid;
            if (Regex.IsMatch(filePathOrGuid, @"[0-9a-fA-F]{32}"))
            {
                filePath = AssetDatabase.GUIDToAssetPath(filePathOrGuid);
                if (string.IsNullOrEmpty(filePath))
                {
                    Debug.LogWarning($"No file found for GUID: {filePathOrGuid}");
                    return false;
                }
            }

            // Normalize path
            filePath = filePath.Replace("\\", "/");
            if (!filePath.StartsWith("Assets"))
            {
                filePath = Path.Combine("Assets", filePath).Replace("\\", "/");
            }

            // Check if file is mapped
            if (!guidMap.ContainsKey(filePath))
            {
                await MapSingleFileAsync(filePath);
            }

            if (guidMap.TryGetValue(filePath, out var guids))
            {
                return guids.Contains(searchGuid);
            }

            Debug.LogWarning($"File not found in mapping: {filePath}");
            return false;
        }

        /// <summary>
        /// Finds all files containing a specific GUID asynchronously.
        /// </summary>
        /// <param name="searchGuid">GUID to search for.</param>
        /// <returns>List of file paths containing the GUID.</returns>
        public async Task<List<string>> FindFilesContainingGuidAsync(string searchGuid)
        {
            // Check inverted index
            if (guidToFilesMap.TryGetValue(searchGuid, out var files))
            {
                return files.ToList();
            }

            // Map unmapped files with relevant extensions
            List<string> unmappedFiles = Directory.GetFiles(projectPath, "*", SearchOption.AllDirectories)
                .Select(f => "Assets" + f.Substring(projectPath.Length).Replace("\\", "/"))
                .Where(f => !guidMap.ContainsKey(f) && relevantExtensions.Any(ext => f.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            foreach (string file in unmappedFiles)
            {
                await MapSingleFileAsync(file);
            }

            return guidToFilesMap.TryGetValue(searchGuid, out files) ? files.ToList() : new List<string>();
        }

        /// <summary>
        /// Maps GUIDs in a single file asynchronously.
        /// </summary>
        /// <param name="filePath">Path to the file to map.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        private async Task MapSingleFileAsync(string filePath)
        {
            try
            {
                // Verify file existence
                string fullPath = Path.Combine(projectPath, filePath.Substring("Assets".Length).TrimStart('/'));
                if (!File.Exists(fullPath))
                {
                    Debug.LogWarning($"File not found: {fullPath}");
                    return;
                }

                // Asynchronous file reading
                string content = await File.ReadAllTextAsync(fullPath, CancellationToken.None).ConfigureAwait(false);
                List<string> foundGuids = guidRegex.Matches(content).Select(m => m.Value).Distinct().ToList();

                // Add to mapping
                guidMap.TryAdd(filePath, foundGuids);

                // Update inverted index
                foreach (string guid in foundGuids)
                {
                    guidToFilesMap.AddOrUpdate(guid, new List<string> { filePath }, (key, oldList) =>
                    {
                        oldList.Add(filePath);
                        return oldList;
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Error mapping file {filePath}: {ex.Message}");
            }
        }

        /// <summary>
        /// Clears all mappings and resets progress.
        /// </summary>
        public void ClearMapping()
        {
            guidMap.Clear();
            guidToFilesMap.Clear();
            progress = 0f;
            totalFilesProcessed = 0;
            mainThreadActions.Enqueue(() => EditorUtility.ClearProgressBar());
            Debug.Log("Mapping cleared from memory.");
        }
    }
}