// File: Assets/legendary-tools-common/Editor/Installers/RoslynNuGetInstaller.cs

#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;
using UnityEngine.Networking;

namespace LegendaryTools.CSFilesAggregator.TypeIndex.Installer
{
    /// <summary>
    /// Makes one Roslyn installation available to Legendary Tools.
    ///
    /// Priority:
    /// 1. A complete Roslyn pair already imported by a Unity package or another package.
    /// 2. The compatible, self-contained Roslyn set bundled by Legendary Tools.
    /// 3. The latest stable NuGet fallback known by this installer.
    ///
    /// A Unity/package Roslyn pair always replaces the whole Legendary Tools set atomically.
    /// </summary>
    public static class RoslynNuGetInstaller
    {
        private const string NuGetV2PackageUrlFormat = "https://www.nuget.org/api/v2/package/{0}/{1}";
        private const string InstallFolderRelative =
            "Assets/legendary-tools-common/Editor/ThirdParty/Roslyn";

        // Latest stable Microsoft.CodeAnalysis.CSharp version on NuGet on 2026-08-31.
        // This is only a fallback: an imported Unity/third-party pair always has priority.
        private const string NuGetFallbackRoslynVersion = "5.9.0";
        private static readonly Version LegacyBundledRoslynAssemblyVersion = new(3, 8, 0, 0);

        private static readonly string[] RoslynAssemblyNames =
        {
            "Microsoft.CodeAnalysis",
            "Microsoft.CodeAnalysis.CSharp"
        };

        // Unity validates direct plugin references before loading editor assemblies. These two
        // assemblies can exist in a package's private tool folder (for example Burst/.Runtime)
        // without being resolvable by project plugins, so the bundled Roslyn set must keep them.
        private static readonly string[] RequiredBundledDependencyAssemblyNames =
        {
            "System.Collections.Immutable",
            "System.Reflection.Metadata"
        };

        // Minimum netstandard2.0 dependencies declared by Microsoft.CodeAnalysis.CSharp 5.9.0.
        // An assembly already supplied by Unity or another package wins over every entry here.
        private static readonly (string id, string version)[] NuGetFallbackPackages =
        {
            ("Microsoft.CodeAnalysis.Common", NuGetFallbackRoslynVersion),
            ("Microsoft.CodeAnalysis.CSharp", NuGetFallbackRoslynVersion),
            ("System.Buffers", "4.6.1"),
            ("System.Collections.Immutable", "10.0.1"),
            ("System.Memory", "4.6.3"),
            ("System.Numerics.Vectors", "4.6.1"),
            ("System.Reflection.Metadata", "10.0.1"),
            ("System.Runtime.CompilerServices.Unsafe", "6.1.2"),
            ("System.Text.Encoding.CodePages", "8.0.0"),
            ("System.Threading.Tasks.Extensions", "4.6.3")
        };

        /// <summary>
        /// Removes Legendary Tools' copy after a package starts providing a complete Roslyn pair.
        /// No downloads or writes to immutable package folders are performed during editor startup.
        /// </summary>
        [InitializeOnLoadMethod]
        private static void ReconcileDuplicateOnEditorLoad()
        {
            EditorApplication.delayCall += () =>
            {
                try
                {
                    RoslynInstallation? existing = FindExistingRoslynOutsideLegendaryTools();
                    if (existing != null && Directory.Exists(GetInstallFolderAbsolute()))
                    {
                        RemoveLegendaryToolsInstallation();
                        AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
                        Debug.Log(
                            $"Legendary Tools removed its Roslyn copy and will use the already imported " +
                            $"Roslyn {existing.Version} from '{existing.Description}'.");
                        return;
                    }

                    RoslynInstallation? bundled = FindBundledLegendaryToolsRoslyn();
                    if (bundled != null)
                    {
                        int removedDependencies = RemoveRedundantBundledDependencies(bundled.Version);
                        if (removedDependencies > 0)
                        {
                            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
                            Debug.Log(
                                $"Legendary Tools kept its loadable Roslyn {bundled.Version} set and removed " +
                                $"{removedDependencies} redundant dependency DLL(s).");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"Legendary Tools could not reconcile duplicate Roslyn DLLs: {ex.Message}");
                }
                finally
                {
                    EditorUtility.ClearProgressBar();
                }
            };
        }

        [MenuItem("Tools/Legendary Tools/Installers/Roslyn/Install or Reconcile")]
        private static void Install()
        {
            try
            {
                RoslynInstallation? existing = FindExistingRoslynOutsideLegendaryTools();
                if (existing != null)
                {
                    if (Directory.Exists(GetInstallFolderAbsolute()))
                    {
                        RemoveLegendaryToolsInstallation();
                        AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
                    }

                    Debug.Log(
                        $"Roslyn is already installed ({existing.Version}) by '{existing.Description}'. " +
                        "Legendary Tools will use it and will not install another copy.");
                    return;
                }

                RoslynInstallation? bundled = FindBundledLegendaryToolsRoslyn();
                if (bundled != null)
                {
                    int removedDependencies = RemoveRedundantBundledDependencies(bundled.Version);
                    if (removedDependencies > 0)
                        AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);

                    Debug.Log(
                        $"Legendary Tools will use its existing loadable Roslyn {bundled.Version} set. " +
                        $"Removed redundant bundled dependency DLLs: {removedDependencies}.");
                    return;
                }

                Debug.LogWarning(
                    "No complete Roslyn pair is currently imported or bundled. " +
                    $"Falling back to Microsoft.CodeAnalysis.CSharp {NuGetFallbackRoslynVersion} from NuGet.");
                InstallNuGetFallback();
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

        [MenuItem("Tools/Legendary Tools/Installers/Roslyn/Uninstall Legendary Tools Copy")]
        private static void Uninstall()
        {
            try
            {
                if (!Directory.Exists(GetInstallFolderAbsolute()))
                {
                    Debug.Log("Legendary Tools Roslyn folder was not found. Nothing to uninstall.");
                    return;
                }

                RemoveLegendaryToolsInstallation();
                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
                Debug.Log($"Legendary Tools Roslyn copy was removed from: {InstallFolderRelative}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"Roslyn uninstall failed: {ex}");
            }
        }

        private static void InstallNuGetFallback()
        {
            PrepareFallbackFolders(
                out string installAbsoluteFolder,
                out string stagingAbsoluteFolder,
                out string tempAbsoluteFolder);

            HashSet<string> externallyProvidedAssemblies = GetImportedAssemblyNamesOutsideLegendaryTools();
            List<string> downloaded = new(NuGetFallbackPackages.Length);

            for (int i = 0; i < NuGetFallbackPackages.Length; i++)
            {
                (string id, string version) = NuGetFallbackPackages[i];
                float progress = i / (float)Mathf.Max(1, NuGetFallbackPackages.Length) * 0.45f;
                EditorUtility.DisplayProgressBar(
                    "Roslyn Installer", $"Downloading {id} {version}", progress);

                string? nupkgPath = DownloadNuGetPackageBlocking(id, version, tempAbsoluteFolder);
                if (string.IsNullOrEmpty(nupkgPath))
                {
                    throw new InvalidOperationException(
                        $"Could not download the required NuGet package {id} {version}. " +
                        "The current Roslyn installation was not changed.");
                }

                downloaded.Add(nupkgPath!);
            }

            int extractedCount = 0;
            for (int i = 0; i < downloaded.Count; i++)
            {
                float progress = 0.5f + i / (float)Mathf.Max(1, downloaded.Count) * 0.45f;
                EditorUtility.DisplayProgressBar(
                    "Roslyn Installer", $"Extracting {Path.GetFileName(downloaded[i])}", progress);

                extractedCount += ExtractNetStandardDlls(
                    downloaded[i], stagingAbsoluteFolder, externallyProvidedAssemblies);
            }

            string[] missingAssemblies = NuGetFallbackPackages
                .Select(package => GetExpectedAssemblyName(package.id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(assemblyName =>
                    !externallyProvidedAssemblies.Contains(assemblyName) &&
                    !File.Exists(Path.Combine(stagingAbsoluteFolder, assemblyName + ".dll")))
                .ToArray();
            if (missingAssemblies.Length > 0)
            {
                throw new InvalidOperationException(
                    "NuGet did not produce a complete Roslyn dependency set. Missing: " +
                    string.Join(", ", missingAssemblies) +
                    ". The current Roslyn installation was not changed.");
            }

            RemoveLegendaryToolsInstallation();
            Directory.CreateDirectory(Path.GetDirectoryName(installAbsoluteFolder));
            Directory.Move(stagingAbsoluteFolder, installAbsoluteFolder);
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            Debug.Log(
                $"Roslyn NuGet fallback {NuGetFallbackRoslynVersion} installed. " +
                $"Extracted DLLs: {extractedCount}. Unity/package-provided dependencies were preserved.");
        }

        private static RoslynInstallation? FindExistingRoslynOutsideLegendaryTools()
        {
            string installFolder = GetInstallFolderAbsolute();
            List<AssemblyCandidate> candidates = GetImportedAssemblyCandidates(roslynAssembliesOnly: true)
                .Where(candidate => !IsPathInside(candidate.Path, installFolder))
                .Where(candidate => !IsPrivatePackageToolAssembly(candidate.Path))
                .Where(candidate => RoslynAssemblyNames.Contains(
                    candidate.Name, StringComparer.OrdinalIgnoreCase))
                .ToList();

            IEnumerable<IGrouping<Version, AssemblyCandidate>> versionGroups = candidates
                .GroupBy(candidate => candidate.Version)
                .Where(group => RoslynAssemblyNames.All(requiredName =>
                    group.Any(candidate => string.Equals(
                        candidate.Name, requiredName, StringComparison.OrdinalIgnoreCase))));

            IGrouping<Version, AssemblyCandidate>? selected = versionGroups
                .OrderBy(group => group.Min(candidate => GetSourcePriority(candidate.Path)))
                .ThenByDescending(group => group.Key)
                .FirstOrDefault();

            if (selected == null) return null;

            AssemblyCandidate representative = selected
                .OrderBy(candidate => GetSourcePriority(candidate.Path))
                .First();
            return new RoslynInstallation(selected.Key, DescribeSource(representative.Path));
        }

        private static List<AssemblyCandidate> GetImportedAssemblyCandidates(bool roslynAssembliesOnly = false)
        {
            string[] paths;
            try
            {
                paths = CompilationPipeline.GetPrecompiledAssemblyPaths(
                    CompilationPipeline.PrecompiledAssemblySources.All);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "Could not inspect Unity precompiled assemblies safely. " +
                    "No Roslyn copy will be installed because an existing installation cannot be ruled out.", ex);
            }

            List<AssemblyCandidate> candidates = new(paths.Length);
            foreach (string path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || !File.Exists(path)) continue;

                string fileAssemblyName = Path.GetFileNameWithoutExtension(path);
                if (roslynAssembliesOnly && !RoslynAssemblyNames.Contains(
                        fileAssemblyName, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    AssemblyName assemblyName = AssemblyName.GetAssemblyName(path);
                    if (string.IsNullOrEmpty(assemblyName.Name) || assemblyName.Version == null) continue;
                    candidates.Add(new AssemblyCandidate(assemblyName.Name, assemblyName.Version, path));
                }
                catch (BadImageFormatException ex)
                {
                    if (RoslynAssemblyNames.Contains(fileAssemblyName, StringComparer.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException(
                            $"Could not inspect the existing Roslyn candidate '{path}'.", ex);
                    }

                    // Native Unity plugins are included in the precompiled assembly list.
                }
                catch (FileLoadException ex)
                {
                    if (RoslynAssemblyNames.Contains(fileAssemblyName, StringComparer.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException(
                            $"Could not inspect the existing Roslyn candidate '{path}'.", ex);
                    }

                    // Ignore a plugin that cannot be inspected; Unity will report its import issue.
                }
            }

            return candidates;
        }

        private static HashSet<string> GetImportedAssemblyNamesOutsideLegendaryTools()
        {
            string installFolder = GetInstallFolderAbsolute();
            return GetImportedAssemblyCandidates()
                .Where(candidate => !IsPathInside(candidate.Path, installFolder))
                .Where(candidate => !IsPrivatePackageToolAssembly(candidate.Path))
                .Select(candidate => candidate.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        private static RoslynInstallation? FindBundledLegendaryToolsRoslyn()
        {
            string installFolder = GetInstallFolderAbsolute();
            Version? version = GetRoslynPairVersion(GetInstallFolderAbsolute());
            if (version != null && RequiredBundledDependencyAssemblyNames.Any(assemblyName =>
                    !File.Exists(Path.Combine(installFolder, assemblyName + ".dll"))))
            {
                return null;
            }

            return version == null
                ? null
                : new RoslynInstallation(version, InstallFolderRelative);
        }

        private static int RemoveRedundantBundledDependencies(Version roslynVersion)
        {
            // This cleanup applies only to the historical 3.8 bundle committed with the package.
            // A newer NuGet fallback has a different dependency closure and must stay intact.
            if (roslynVersion != LegacyBundledRoslynAssemblyVersion) return 0;

            string installFolder = GetInstallFolderAbsolute();
            if (!Directory.Exists(installFolder)) return 0;

            IEnumerable<string> preservedAssemblyNames = RoslynAssemblyNames
                .Concat(RequiredBundledDependencyAssemblyNames);
            HashSet<string> preservedFiles = preservedAssemblyNames
                .Select(assemblyName => assemblyName + ".dll")
                .Concat(preservedAssemblyNames.Select(assemblyName => assemblyName + ".dll.meta"))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            int removedDlls = 0;
            string[] files = Directory.GetFiles(installFolder, "*", SearchOption.TopDirectoryOnly);
            foreach (string file in files)
            {
                string fileName = Path.GetFileName(file);
                if (preservedFiles.Contains(fileName)) continue;

                if (fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) removedDlls++;
                FileUtil.DeleteFileOrDirectory(InstallFolderRelative + "/" + fileName);
            }

            string[] directories = Directory.GetDirectories(installFolder, "*", SearchOption.TopDirectoryOnly);
            foreach (string directory in directories)
            {
                FileUtil.DeleteFileOrDirectory(
                    InstallFolderRelative + "/" + Path.GetFileName(directory));
            }

            return removedDlls;
        }

        private static Version? GetRoslynPairVersion(string folder)
        {
            Version? version = null;
            foreach (string assemblyName in RoslynAssemblyNames)
            {
                string path = Path.Combine(folder, assemblyName + ".dll");
                if (!File.Exists(path)) return null;

                try
                {
                    Version current = AssemblyName.GetAssemblyName(path).Version;
                    if (version != null && version != current) return null;
                    version = current;
                }
                catch (BadImageFormatException)
                {
                    return null;
                }
                catch (FileLoadException)
                {
                    return null;
                }
            }

            return version;
        }

        private static int GetSourcePriority(string path)
        {
            string normalized = NormalizePath(path);
            if (normalized.Contains("/library/packagecache/com.unity.", StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains("/packages/com.unity.", StringComparison.OrdinalIgnoreCase))
            {
                return 0;
            }

            if (normalized.Contains("/library/packagecache/", StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains("/packages/", StringComparison.OrdinalIgnoreCase))
            {
                return 1;
            }

            return 2;
        }

        private static string DescribeSource(string path)
        {
            string normalized = NormalizePath(path);
            int packageCacheIndex = normalized.IndexOf(
                "/library/packagecache/", StringComparison.OrdinalIgnoreCase);
            if (packageCacheIndex >= 0)
            {
                string packagePath = normalized.Substring(
                    packageCacheIndex + "/library/packagecache/".Length);
                int slash = packagePath.IndexOf('/');
                return slash >= 0 ? packagePath.Substring(0, slash) : packagePath;
            }

            int packagesIndex = normalized.IndexOf("/packages/", StringComparison.OrdinalIgnoreCase);
            if (packagesIndex >= 0)
            {
                string packagePath = normalized.Substring(packagesIndex + "/packages/".Length);
                int slash = packagePath.IndexOf('/');
                return slash >= 0 ? packagePath.Substring(0, slash) : packagePath;
            }

            return path;
        }

        private static string GetExpectedAssemblyName(string packageId)
        {
            return string.Equals(
                packageId, "Microsoft.CodeAnalysis.Common", StringComparison.OrdinalIgnoreCase)
                ? "Microsoft.CodeAnalysis"
                : packageId;
        }

        private static void PrepareFallbackFolders(
            out string installAbsoluteFolder,
            out string stagingAbsoluteFolder,
            out string tempAbsoluteFolder)
        {
            installAbsoluteFolder = GetInstallFolderAbsolute();

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string roslynWorkingFolder = Path.Combine(
                projectRoot, "Library", "LegendaryTools", "Roslyn");
            stagingAbsoluteFolder = Path.Combine(roslynWorkingFolder, "Staging");
            tempAbsoluteFolder = Path.Combine(
                roslynWorkingFolder, "NuGetCache");

            if (Directory.Exists(stagingAbsoluteFolder))
                Directory.Delete(stagingAbsoluteFolder, recursive: true);

            Directory.CreateDirectory(stagingAbsoluteFolder);
            Directory.CreateDirectory(tempAbsoluteFolder);
        }

        private static void RemoveLegendaryToolsInstallation()
        {
            if (Directory.Exists(GetInstallFolderAbsolute()))
            {
                FileUtil.DeleteFileOrDirectory(InstallFolderRelative);
            }

            if (File.Exists(GetInstallFolderAbsolute() + ".meta"))
            {
                FileUtil.DeleteFileOrDirectory(InstallFolderRelative + ".meta");
            }
        }

        private static string GetInstallFolderAbsolute()
        {
            return Path.GetFullPath(InstallFolderRelative);
        }

        private static string? DownloadNuGetPackageBlocking(
            string id, string version, string tempAbsoluteFolder)
        {
            string fileName = $"{id}.{version}.nupkg";
            string outputPath = Path.Combine(tempAbsoluteFolder, fileName);
            if (File.Exists(outputPath) && new FileInfo(outputPath).Length > 0) return outputPath;

            string url = string.Format(NuGetV2PackageUrlFormat, id, version);
            using UnityWebRequest request = UnityWebRequest.Get(url);
            request.downloadHandler = new DownloadHandlerBuffer();

            UnityWebRequestAsyncOperation operation = request.SendWebRequest();
            while (!operation.isDone)
            {
                EditorUtility.DisplayProgressBar(
                    "Roslyn Installer",
                    $"Downloading {id} {version} ({request.downloadProgress:P0})",
                    0.2f);
            }

#if UNITY_2020_1_OR_NEWER
            if (request.result != UnityWebRequest.Result.Success)
#else
            if (request.isNetworkError || request.isHttpError)
#endif
            {
                Debug.LogError($"Failed to download {id} {version}: {request.error}");
                return null;
            }

            File.WriteAllBytes(outputPath, request.downloadHandler.data);
            return outputPath;
        }

        private static int ExtractNetStandardDlls(
            string nupkgPath,
            string installAbsoluteFolder,
            HashSet<string> externallyProvidedAssemblies)
        {
            string[] preferredRoots =
            {
                "lib/netstandard2.0/",
                "lib/netstandard2.1/",
                "lib/net472/",
                "lib/net471/",
                "lib/net461/",
                "lib/net46/"
            };

            using FileStream stream = File.OpenRead(nupkgPath);
            using ZipArchive zip = new(stream, ZipArchiveMode.Read);

            List<ZipArchiveEntry> selectedEntries = new();
            foreach (string root in preferredRoots)
            {
                selectedEntries = zip.Entries
                    .Where(entry => IsDirectDllInFrameworkRoot(entry, root))
                    .ToList();
                if (selectedEntries.Count > 0) break;
            }

            int count = 0;
            foreach (ZipArchiveEntry entry in selectedEntries)
            {
                string fileName = Path.GetFileName(entry.FullName);
                string assemblyName = Path.GetFileNameWithoutExtension(fileName);
                if (externallyProvidedAssemblies.Contains(assemblyName))
                {
                    Debug.Log(
                        $"Roslyn installer kept the existing Unity/package assembly '{assemblyName}' " +
                        $"instead of extracting it from {Path.GetFileName(nupkgPath)}.");
                    continue;
                }

                entry.ExtractToFile(Path.Combine(installAbsoluteFolder, fileName), true);
                count++;
            }

            return count;
        }

        private static bool IsDirectDllInFrameworkRoot(ZipArchiveEntry entry, string root)
        {
            string fullName = entry.FullName.Replace('\\', '/');
            if (!fullName.StartsWith(root, StringComparison.OrdinalIgnoreCase) ||
                !fullName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string relative = fullName.Substring(root.Length);
            return relative.Length > 0 && relative.IndexOf('/') < 0;
        }

        private static bool IsPathInside(string path, string parentFolder)
        {
            string normalizedPath = NormalizePath(Path.GetFullPath(path)).TrimEnd('/');
            string normalizedParent = NormalizePath(Path.GetFullPath(parentFolder)).TrimEnd('/');
            return normalizedPath.Equals(normalizedParent, StringComparison.OrdinalIgnoreCase) ||
                   normalizedPath.StartsWith(
                       normalizedParent + "/", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsPrivatePackageToolAssembly(string path)
        {
            string normalized = NormalizePath(path);
            return normalized.Contains("/.runtime/", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizePath(string path)
        {
            return path.Replace('\\', '/');
        }

        private sealed class AssemblyCandidate
        {
            public readonly string Name;
            public readonly Version Version;
            public readonly string Path;

            public AssemblyCandidate(string name, Version version, string path)
            {
                Name = name;
                Version = version;
                Path = path;
            }
        }

        private sealed class RoslynInstallation
        {
            public readonly Version Version;
            public readonly string Description;

            public RoslynInstallation(Version version, string description)
            {
                Version = version;
                Description = description;
            }
        }
    }
}
