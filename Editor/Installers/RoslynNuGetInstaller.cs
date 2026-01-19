using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace LegendaryTools.CSFilesAggregator.TypeIndex.Installer
{
    /// <summary>
    /// Downloads Roslyn (Microsoft.CodeAnalysis*) and required dependencies from NuGet,
    /// extracts netstandard2.0 DLLs, and installs them under an Editor-only folder.
    /// </summary>
    public static class RoslynNuGetInstaller
    {
        private const string NuGetV2PackageUrlFormat = "https://www.nuget.org/api/v2/package/{0}/{1}";

        // Keep these inside Editor so they never ship into builds.
        private const string InstallFolderRelative = "Assets/legendary-tools-common/Editor/ThirdParty/Roslyn";

        // Unity-friendly baseline. Unity docs mention Roslyn 3.8 for generator/analyzer compatibility. :contentReference[oaicite:2]{index=2}
        private const string RoslynVersion = "3.8.0";

        // Known dependencies for Roslyn netstandard2.0 usage (parsing/compilation APIs).
        // We pin versions that provide netstandard2.0 assets and are commonly compatible with Unity's .NET 4.x runtime.
        private static readonly (string id, string version)[] Packages =
        {
            ("Microsoft.CodeAnalysis", RoslynVersion),
            ("Microsoft.CodeAnalysis.Common", RoslynVersion),
            ("Microsoft.CodeAnalysis.CSharp", RoslynVersion),

            // Dependencies (netstandard2.0)
            ("System.Collections.Immutable", "1.7.1"),
            ("System.Reflection.Metadata", "1.8.1"),
            ("System.Memory", "4.5.4"),
            ("System.Runtime.CompilerServices.Unsafe", "4.7.1"),
            ("System.Threading.Tasks.Extensions", "4.5.4"),
            ("System.Numerics.Vectors", "4.5.0"),
            ("System.Buffers", "4.5.1"),
        };

        [MenuItem("Tools/LegendaryTools/Installers/Roslyn/Install (NuGet)")]
        private static void Install()
        {
            try
            {
                EnsureInstallFolders(out string installAbsoluteFolder, out string tempAbsoluteFolder);

                EditorUtility.DisplayProgressBar("Roslyn Installer", "Downloading packages...", 0f);

                var downloaded = new List<string>(Packages.Length);
                for (int i = 0; i < Packages.Length; i++)
                {
                    (string id, string version) = Packages[i];

                    float p = (i / (float)Mathf.Max(1, Packages.Length)) * 0.4f;
                    EditorUtility.DisplayProgressBar("Roslyn Installer", $"Downloading {id} {version}", p);

                    string nupkgPath = DownloadNuGetPackageBlocking(id, version, tempAbsoluteFolder);
                    if (!string.IsNullOrEmpty(nupkgPath))
                    {
                        downloaded.Add(nupkgPath);
                    }
                }

                EditorUtility.DisplayProgressBar("Roslyn Installer", "Extracting DLLs...", 0.45f);

                int extractedCount = 0;
                for (int i = 0; i < downloaded.Count; i++)
                {
                    float p = 0.45f + (i / (float)Mathf.Max(1, downloaded.Count)) * 0.45f;
                    EditorUtility.DisplayProgressBar("Roslyn Installer", $"Extracting {Path.GetFileName(downloaded[i])}", p);

                    extractedCount += ExtractNetStandardDlls(downloaded[i], installAbsoluteFolder);
                }

                EditorUtility.DisplayProgressBar("Roslyn Installer", "Finalizing...", 0.95f);

                AssetDatabase.Refresh();

                Debug.Log($"Roslyn install complete. Extracted DLLs: {extractedCount}. Installed to: {InstallFolderRelative}");
                Debug.Log("If you still see compile errors, check for conflicting Roslyn DLLs already present in the project (e.g., other analyzers packages).");
            }
            catch (Exception ex)
            {
                Debug.LogError($"Roslyn install failed: {ex}");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        [MenuItem("Tools/LegendaryTools/Installers/Roslyn/Uninstall")]
        private static void Uninstall()
        {
            try
            {
                string folderAbs = Path.GetFullPath(InstallFolderRelative);
                if (Directory.Exists(folderAbs))
                {
                    FileUtil.DeleteFileOrDirectory(InstallFolderRelative);
                    FileUtil.DeleteFileOrDirectory(InstallFolderRelative + ".meta");
                    AssetDatabase.Refresh();
                    Debug.Log($"Roslyn uninstalled from: {InstallFolderRelative}");
                }
                else
                {
                    Debug.Log("Roslyn install folder not found. Nothing to uninstall.");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Roslyn uninstall failed: {ex}");
            }
        }

        private static void EnsureInstallFolders(out string installAbsoluteFolder, out string tempAbsoluteFolder)
        {
            installAbsoluteFolder = Path.GetFullPath(InstallFolderRelative);

            if (!Directory.Exists(installAbsoluteFolder))
            {
                Directory.CreateDirectory(installAbsoluteFolder);
            }

            // Keep temp under Library to avoid polluting Assets.
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            tempAbsoluteFolder = Path.Combine(projectRoot, "Library", "LegendaryTools", "TypeIndex", "NuGetCache");
            if (!Directory.Exists(tempAbsoluteFolder))
            {
                Directory.CreateDirectory(tempAbsoluteFolder);
            }
        }

        private static string DownloadNuGetPackageBlocking(string id, string version, string tempAbsoluteFolder)
        {
            string fileName = $"{id}.{version}.nupkg";
            string outputPath = Path.Combine(tempAbsoluteFolder, fileName);

            if (File.Exists(outputPath) && new FileInfo(outputPath).Length > 0)
            {
                return outputPath;
            }

            string url = string.Format(NuGetV2PackageUrlFormat, id, version);

            using (UnityWebRequest req = UnityWebRequest.Get(url))
            {
                req.downloadHandler = new DownloadHandlerBuffer();

                UnityWebRequestAsyncOperation op = req.SendWebRequest();
                while (!op.isDone)
                {
                    // Keep UI responsive-ish with progress bar updates.
                    EditorUtility.DisplayProgressBar("Roslyn Installer", $"Downloading {id} {version} ({req.downloadProgress:P0})", 0.1f);
                }

#if UNITY_2020_1_OR_NEWER
                if (req.result != UnityWebRequest.Result.Success)
#else
                if (req.isNetworkError || req.isHttpError)
#endif
                {
                    Debug.LogError($"Failed to download {id} {version}: {req.error}");
                    return null;
                }

                try
                {
                    File.WriteAllBytes(outputPath, req.downloadHandler.data);
                    return outputPath;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Failed to write nupkg for {id} {version}: {ex}");
                    return null;
                }
            }
        }

        private static int ExtractNetStandardDlls(string nupkgPath, string installAbsoluteFolder)
        {
            int count = 0;

            using (FileStream fs = File.OpenRead(nupkgPath))
            using (ZipArchive zip = new ZipArchive(fs, ZipArchiveMode.Read))
            {
                // Prefer netstandard2.0. Fallback to net461/net472 if a package doesn't ship ns2.0.
                string[] preferredRoots =
                {
                    "lib/netstandard2.0/",
                    "lib/netstandard2.1/",
                    "lib/net472/",
                    "lib/net471/",
                    "lib/net46/",
                    "lib/net461/",
                };

                ZipArchiveEntry[] dllEntries = zip.Entries
                    .Where(e => e.FullName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    .ToArray();

                // Group by filename; pick best target framework folder using preferredRoots order.
                var byName = dllEntries
                    .GroupBy(e => Path.GetFileName(e.FullName), StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.OrdinalIgnoreCase);

                foreach (KeyValuePair<string, ZipArchiveEntry[]> kvp in byName)
                {
                    ZipArchiveEntry selected = SelectBestEntry(kvp.Value, preferredRoots);
                    if (selected == null)
                    {
                        continue;
                    }

                    string outPath = Path.Combine(installAbsoluteFolder, kvp.Key);
                    try
                    {
                        selected.ExtractToFile(outPath, overwrite: true);
                        count++;
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"Failed to extract {selected.FullName} from {Path.GetFileName(nupkgPath)}: {ex.Message}");
                    }
                }
            }

            return count;
        }

        private static ZipArchiveEntry SelectBestEntry(ZipArchiveEntry[] entries, string[] preferredRoots)
        {
            for (int i = 0; i < preferredRoots.Length; i++)
            {
                string root = preferredRoots[i];

                for (int j = 0; j < entries.Length; j++)
                {
                    string full = entries[j].FullName.Replace('\\', '/');
                    if (full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    {
                        return entries[j];
                    }
                }
            }

            return null;
        }
    }
}
