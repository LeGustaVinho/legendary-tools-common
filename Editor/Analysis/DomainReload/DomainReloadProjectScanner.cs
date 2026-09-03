using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.Compilation;
using UnityEditor.PackageManager;
using UnityEngine;

namespace LegendaryTools.Editor.DomainReload
{
    public static class DomainReloadProjectScanner
    {
        private sealed class ScanRoot
        {
            public string FullPath;
            public string AssetPath;
            public string Origin;
            public string PackageName;
        }

        private sealed class SourceRule
        {
            public Regex Regex;
            public DomainReloadFindingKind Kind;
            public DomainReloadRisk Risk;
            public string Symbol;
            public string Detail;
            public bool RequiresReloadContext;

            public SourceRule(string pattern, DomainReloadFindingKind kind, DomainReloadRisk risk,
                string symbol, string detail, bool requiresReloadContext = false)
            {
                Regex = new Regex(pattern, RegexOptions.Compiled | RegexOptions.Multiline);
                Kind = kind;
                Risk = risk;
                Symbol = symbol;
                Detail = detail;
                RequiresReloadContext = requiresReloadContext;
            }
        }

        private static readonly SourceRule[] Rules =
        {
            new(@"\[\s*(?:UnityEditor\.)?InitializeOnLoad(?:Attribute)?\s*\]", DomainReloadFindingKind.ReloadCallback,
                DomainReloadRisk.High, "InitializeOnLoad", "The type's static constructor runs on every Domain Reload."),
            new(@"\[\s*(?:UnityEditor\.)?InitializeOnLoadMethod(?:Attribute)?(?:\s*\([^\]]*\))?\s*\]",
                DomainReloadFindingKind.ReloadCallback, DomainReloadRisk.High, "InitializeOnLoadMethod",
                "The method runs automatically while the new domain is initialized."),
            new(@"\[\s*(?:UnityEditor\.Callbacks\.)?DidReloadScripts(?:Attribute)?(?:\s*\([^\]]*\))?\s*\]",
                DomainReloadFindingKind.ReloadCallback, DomainReloadRisk.High, "DidReloadScripts",
                "Callback invoked after scripts have been reloaded."),
            new(@"AssemblyReloadEvents\s*\.\s*beforeAssemblyReload\s*\+=", DomainReloadFindingKind.ReloadCallback,
                DomainReloadRisk.High, "beforeAssemblyReload", "Synchronous work executed before the old domain is unloaded."),
            new(@"AssemblyReloadEvents\s*\.\s*afterAssemblyReload\s*\+=", DomainReloadFindingKind.ReloadCallback,
                DomainReloadRisk.High, "afterAssemblyReload", "Synchronous work executed after the new domain has loaded."),
            new(@"(?:AppDomain\s*\.\s*CurrentDomain\s*\.)?DomainUnload\s*\+=", DomainReloadFindingKind.ReloadCallback,
                DomainReloadRisk.High, "AppDomain.DomainUnload", "Callback executed while the previous domain is being unloaded."),
            new(@"\bISerializationCallbackReceiver\b", DomainReloadFindingKind.Serialization, DomainReloadRisk.Medium,
                "ISerializationCallbackReceiver", "Instances receive OnBeforeSerialize/OnAfterDeserialize during state restoration."),
            new(@"\bOnBeforeSerialize\s*\(", DomainReloadFindingKind.Serialization, DomainReloadRisk.Medium,
                "OnBeforeSerialize", "Can participate in state backup before reload."),
            new(@"\bOnAfterDeserialize\s*\(", DomainReloadFindingKind.Serialization, DomainReloadRisk.Medium,
                "OnAfterDeserialize", "Can participate in state restoration after reload."),
            new(@"\bOnPostprocessAllAssets\s*\(", DomainReloadFindingKind.ImportPipeline, DomainReloadRisk.Medium,
                "OnPostprocessAllAssets", "Can run after Domain Reload and before the rest of asset import."),
            new(@"\bScriptedImporter\b|\bAssetPostprocessor\b", DomainReloadFindingKind.ImportPipeline,
                DomainReloadRisk.Low, "Asset import hook", "Importers and post-processors extend the Refresh operation surrounding reload."),
            new(@"\[\s*(?:UnityEngine\.)?RuntimeInitializeOnLoadMethod", DomainReloadFindingKind.ReloadCallback,
                DomainReloadRisk.Low, "RuntimeInitializeOnLoadMethod", "Affects Play Mode entry; it does not run on every script reload."),
            new(@"\b(?:Thread\s*\(|new\s+Thread|Task\s*\.\s*Run|TaskFactory\s*\.\s*StartNew)\b",
                DomainReloadFindingKind.BackgroundWork, DomainReloadRisk.Medium, "Background work",
                "Threads and Tasks must stop or cooperate with domain unloading.", true),
            new(@"\b(?:WaitAll|WaitOne|GetAwaiter\s*\(\s*\)\s*\.\s*GetResult|\.Result\b|\.Wait\s*\()",
                DomainReloadFindingKind.BackgroundWork, DomainReloadRisk.High, "Blocking wait",
                "A blocking wait during initialization/reload can freeze the main thread.", true),
            new(@"\bAssetDatabase\s*\.\s*(?:Refresh|SaveAssets|ImportAsset|FindAssets|LoadAllAssetsAtPath)\s*\(",
                DomainReloadFindingKind.ExpensiveOperation, DomainReloadRisk.High, "AssetDatabase operation",
                "An AssetDatabase operation in a reload callback can restart import or scan the project.", true),
            new(@"\b(?:Directory|DirectoryInfo)\s*\.\s*(?:GetFiles|EnumerateFiles|GetDirectories|EnumerateDirectories)\s*\(",
                DomainReloadFindingKind.ExpensiveOperation, DomainReloadRisk.Medium, "Filesystem scan",
                "A synchronous filesystem scan during initialization increases reload time.", true),
            new(@"\b(?:Resources\s*\.\s*FindObjectsOfTypeAll|Object\s*\.\s*FindObjectsByType|FindObjectsOfType)\s*<?",
                DomainReloadFindingKind.ExpensiveOperation, DomainReloadRisk.High, "Object scan",
                "A global object scan during restoration usually scales with project size.", true),
            new(@"\b(?:AppDomain\s*\.\s*CurrentDomain\s*\.\s*GetAssemblies|TypeCache\s*\.|GetTypes\s*\(\s*\))",
                DomainReloadFindingKind.ExpensiveOperation, DomainReloadRisk.Medium, "Type/assembly scan",
                "Reflection or TypeCache work during initialization scales with loaded assemblies and types.", true),
            new(@"\b(?:GC\s*\.\s*Collect|Resources\s*\.\s*UnloadUnusedAssets|UnloadUnusedAssetsImmediate)\s*\(",
                DomainReloadFindingKind.ExpensiveOperation, DomainReloadRisk.High, "Forced cleanup",
                "Forced GC/unload during reload can cause a long pause.", true)
        };

        public static DomainReloadAudit Scan(bool includePackages = true, bool includeLiveObjects = true)
        {
            DomainReloadAudit audit = new()
            {
                ScannedAtUtc = DateTime.UtcNow.ToString("O"),
                UnityVersion = Application.unityVersion
            };

            List<ScanRoot> roots = BuildRoots(includePackages, audit);
            List<(string file, ScanRoot root)> files = EnumerateSourceFiles(roots, audit);

            try
            {
                for (int i = 0; i < files.Count; i++)
                {
                    (string file, ScanRoot root) = files[i];
                    if (i % 25 == 0 && EditorUtility.DisplayCancelableProgressBar(
                            "Domain Reload Analyzer",
                            $"Reading source {i + 1}/{files.Count}: {Path.GetFileName(file)}",
                            files.Count == 0 ? 1f : (float)i / files.Count))
                    {
                        audit.Diagnostics.Add("Source scan was canceled by the user.");
                        break;
                    }

                    ScanSourceFile(file, root, audit.Findings);
                    audit.FilesScanned++;
                }

                ScanAssemblies(audit, roots);
                ScanCompiledHooks(audit);
                if (includeLiveObjects)
                    ScanLiveObjects(audit);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            audit.Findings = audit.Findings
                .GroupBy(f => $"{f.FullPath}|{f.Line}|{f.Symbol}|{f.Evidence}")
                .Select(group => group.First())
                .OrderByDescending(f => f.Risk)
                .ThenBy(f => f.Origin)
                .ThenBy(f => f.AssetPath)
                .ThenBy(f => f.Line)
                .ToList();
            BuildScriptAndAssemblyBreakdown(audit);
            audit.Assemblies = audit.Assemblies.OrderByDescending(a => a.SourceFileCount).ThenBy(a => a.Name).ToList();
            audit.LiveObjects = audit.LiveObjects.OrderByDescending(o => o.Count).ThenBy(o => o.TypeName).ToList();
            return audit;
        }

        private static List<ScanRoot> BuildRoots(bool includePackages, DomainReloadAudit audit)
        {
            List<ScanRoot> roots = new()
            {
                new ScanRoot
                {
                    FullPath = Path.GetFullPath(Application.dataPath),
                    AssetPath = "Assets",
                    Origin = "Project"
                }
            };

            if (includePackages)
            {
                foreach (UnityEditor.PackageManager.PackageInfo package in
                         UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages())
                {
                    if (package == null || string.IsNullOrEmpty(package.resolvedPath) || !Directory.Exists(package.resolvedPath))
                        continue;
                    string full = Path.GetFullPath(package.resolvedPath);
                    if (roots.Any(root => PathsEqual(root.FullPath, full)))
                        continue;
                    roots.Add(new ScanRoot
                    {
                        FullPath = full,
                        AssetPath = string.IsNullOrEmpty(package.assetPath) ? "Packages/" + package.name : package.assetPath,
                        Origin = package.source + " Package",
                        PackageName = package.name + "@" + package.version
                    });
                    audit.PackagesScanned++;
                }
            }

            return roots;
        }

        private static List<(string file, ScanRoot root)> EnumerateSourceFiles(IEnumerable<ScanRoot> roots,
            DomainReloadAudit audit)
        {
            List<(string file, ScanRoot root)> files = new();
            foreach (ScanRoot root in roots)
            {
                try
                {
                    foreach (string file in Directory.EnumerateFiles(root.FullPath, "*.cs", SearchOption.AllDirectories))
                    {
                        string normalized = file.Replace('\\', '/');
                        if (normalized.Contains("/.git/") || normalized.Contains("/obj/") || normalized.Contains("/Temp/"))
                            continue;
                        files.Add((file, root));
                    }
                }
                catch (Exception ex)
                {
                    audit.Diagnostics.Add($"Could not enumerate {root.FullPath}: {ex.Message}");
                }
            }

            return files;
        }

        private static void ScanSourceFile(string file, ScanRoot root, List<DomainReloadFinding> findings)
        {
            string original;
            try
            {
                original = File.ReadAllText(file);
            }
            catch (Exception ex)
            {
                findings.Add(CreateFinding(root, file, 0, DomainReloadFindingKind.Assembly, DomainReloadRisk.Info,
                    "Unreadable source", ex.Message, string.Empty));
                return;
            }

            string source = MaskCommentsAndStrings(original);
            int[] lineStarts = BuildLineStarts(source);
            string[] originalLines = original.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            bool reloadContext = Regex.IsMatch(source,
                @"InitializeOnLoad|DidReloadScripts|AssemblyReloadEvents|DomainUnload|OnPostprocessAllAssets");
            bool unityObjectContext = Regex.IsMatch(source,
                @"\b(?:MonoBehaviour|ScriptableObject|EditorWindow|UnityEditor\.Editor)\b");

            foreach (SourceRule rule in Rules)
            {
                if (rule.RequiresReloadContext && !reloadContext)
                    continue;
                if ((rule.Symbol == "OnBeforeSerialize" || rule.Symbol == "OnAfterDeserialize") &&
                    !unityObjectContext && !source.Contains("ISerializationCallbackReceiver"))
                    continue;

                foreach (Match match in rule.Regex.Matches(source))
                {
                    int line = GetLineNumber(lineStarts, match.Index);
                    findings.Add(CreateFinding(root, file, line, rule.Kind, rule.Risk, rule.Symbol, rule.Detail,
                        GetSourceLine(originalLines, line)));
                }
            }

            foreach (Match type in Regex.Matches(source, @"\b(?:class|struct)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)"))
            {
                string name = type.Groups["name"].Value;
                Regex staticCtor = new(@"\bstatic\s+" + Regex.Escape(name) + @"\s*\(", RegexOptions.Multiline);
                foreach (Match match in staticCtor.Matches(source))
                {
                    int line = GetLineNumber(lineStarts, match.Index);
                    DomainReloadRisk risk = reloadContext ? DomainReloadRisk.High : DomainReloadRisk.Low;
                    findings.Add(CreateFinding(root, file, line,
                        DomainReloadFindingKind.StaticInitialization, risk, name + ".cctor",
                        reloadContext
                            ? "Static constructor in a file with a reload hook; inspect its full body and field initializers."
                            : "The static constructor runs again in the new domain when the type is used.",
                        GetSourceLine(originalLines, line)));
                }
            }

            if (unityObjectContext)
            {
                foreach (Match match in Regex.Matches(source, @"\b(?<method>OnDisable|OnEnable|OnValidate)\s*\("))
                {
                    string method = match.Groups["method"].Value;
                    int line = GetLineNumber(lineStarts, match.Index);
                    findings.Add(CreateFinding(root, file, line,
                        DomainReloadFindingKind.ObjectLifecycle, DomainReloadRisk.Low, method,
                        "Can run while objects are disabled/restored; cost depends on the number of live instances.",
                        GetSourceLine(originalLines, line)));
                }
            }
        }

        private static void ScanAssemblies(DomainReloadAudit audit, IReadOnlyList<ScanRoot> roots)
        {
            Dictionary<string, DomainReloadAssemblyInfo> byName = new(StringComparer.OrdinalIgnoreCase);
            foreach (UnityEditor.Compilation.Assembly assembly in CompilationPipeline.GetAssemblies(AssembliesType.Editor))
            {
                ScanRoot root = FindRoot(assembly.sourceFiles.FirstOrDefault(), roots);
                FileInfo binary = string.IsNullOrEmpty(assembly.outputPath) ? null : new FileInfo(assembly.outputPath);
                DomainReloadAssemblyInfo info = new()
                {
                    Name = assembly.name,
                    Origin = root?.Origin ?? "Generated/Unknown",
                    PackageName = root?.PackageName,
                    OutputPath = assembly.outputPath,
                    SourceFileCount = assembly.sourceFiles?.Length ?? 0,
                    ReferenceCount = assembly.assemblyReferences?.Length ?? 0,
                    BinaryBytes = binary != null && binary.Exists ? binary.Length : 0
                };
                byName[assembly.name] = info;
                audit.Assemblies.Add(info);
            }

            try
            {
                foreach (string path in CompilationPipeline.GetPrecompiledAssemblyPaths(
                             CompilationPipeline.PrecompiledAssemblySources.All))
                {
                    FileInfo file = new(path);
                    string name = Path.GetFileNameWithoutExtension(path);
                    if (byName.ContainsKey(name))
                        continue;
                    ScanRoot root = FindRoot(path, roots);
                    audit.Assemblies.Add(new DomainReloadAssemblyInfo
                    {
                        Name = name,
                        Origin = root?.Origin ?? "Precompiled/External",
                        PackageName = root?.PackageName,
                        OutputPath = path,
                        SourceFileCount = 0,
                        BinaryBytes = file.Exists ? file.Length : 0
                    });
                }
            }
            catch (Exception ex)
            {
                audit.Diagnostics.Add("Failed to list precompiled assemblies: " + ex.Message);
            }
        }

        private static void ScanCompiledHooks(DomainReloadAudit audit)
        {
            AddTypeCacheFindings(TypeCache.GetTypesWithAttribute<InitializeOnLoadAttribute>(), audit,
                "InitializeOnLoad (compiled)", DomainReloadFindingKind.ReloadCallback, DomainReloadRisk.High);
            AddMethodCacheFindings(TypeCache.GetMethodsWithAttribute<InitializeOnLoadMethodAttribute>(), audit,
                "InitializeOnLoadMethod (compiled)", DomainReloadRisk.High);
            AddMethodCacheFindings(TypeCache.GetMethodsWithAttribute<DidReloadScripts>(), audit,
                "DidReloadScripts (compiled)", DomainReloadRisk.High);
            AddTypeCacheFindings(TypeCache.GetTypesDerivedFrom<AssetPostprocessor>(), audit,
                "AssetPostprocessor (compiled)", DomainReloadFindingKind.ImportPipeline, DomainReloadRisk.Low);
        }

        private static void AddTypeCacheFindings(IEnumerable<Type> types, DomainReloadAudit audit, string symbol,
            DomainReloadFindingKind kind, DomainReloadRisk risk)
        {
            foreach (Type type in types)
            {
                if (!TryGetAssemblyOrigin(type.Assembly.GetName().Name, audit, out string origin, out string package))
                    continue;
                audit.Findings.Add(new DomainReloadFinding
                {
                    Kind = kind,
                    Risk = risk,
                    Symbol = symbol,
                    Detail = "Detected in the loaded assembly; covers source-less code and confirms the type entered the domain.",
                    Evidence = type.FullName,
                    Origin = origin,
                    PackageName = package,
                    AssemblyName = type.Assembly.GetName().Name
                });
            }
        }

        private static void AddMethodCacheFindings(IEnumerable<MethodInfo> methods, DomainReloadAudit audit,
            string symbol, DomainReloadRisk risk)
        {
            foreach (MethodInfo method in methods)
            {
                if (!TryGetAssemblyOrigin(method.DeclaringType?.Assembly.GetName().Name, audit,
                        out string origin, out string package))
                    continue;
                audit.Findings.Add(new DomainReloadFinding
                {
                    Kind = DomainReloadFindingKind.ReloadCallback,
                    Risk = risk,
                    Symbol = symbol,
                    Detail = "Method confirmed by Unity TypeCache.",
                    Evidence = method.DeclaringType?.FullName + "." + method.Name,
                    Origin = origin,
                    PackageName = package,
                    AssemblyName = method.DeclaringType?.Assembly.GetName().Name
                });
            }
        }

        private static void BuildScriptAndAssemblyBreakdown(DomainReloadAudit audit)
        {
            Dictionary<string, string> assemblyBySource = new(StringComparer.OrdinalIgnoreCase);
            foreach (UnityEditor.Compilation.Assembly assembly in CompilationPipeline.GetAssemblies(AssembliesType.Editor))
            {
                foreach (string source in assembly.sourceFiles ?? Array.Empty<string>())
                {
                    string normalized = source.Replace('\\', '/');
                    assemblyBySource[normalized] = assembly.name;
                    try { assemblyBySource[Path.GetFullPath(source).Replace('\\', '/')] = assembly.name; }
                    catch { /* A virtual Packages path might not resolve to a physical project path. */ }
                }
            }

            foreach (DomainReloadFinding finding in audit.Findings)
            {
                if (!string.IsNullOrEmpty(finding.AssemblyName))
                    continue;
                if (!string.IsNullOrEmpty(finding.AssetPath) &&
                    assemblyBySource.TryGetValue(finding.AssetPath.Replace('\\', '/'), out string byAssetPath))
                    finding.AssemblyName = byAssetPath;
                else if (!string.IsNullOrEmpty(finding.FullPath) &&
                         assemblyBySource.TryGetValue(finding.FullPath.Replace('\\', '/'), out string byFullPath))
                    finding.AssemblyName = byFullPath;
            }

            audit.Scripts = audit.Findings
                .Where(finding => !string.IsNullOrEmpty(finding.AssetPath))
                .GroupBy(finding => finding.AssetPath, StringComparer.OrdinalIgnoreCase)
                .Select(group => new DomainReloadScriptInfo
                {
                    AssetPath = group.Key,
                    FullPath = group.Select(item => item.FullPath).FirstOrDefault(value => !string.IsNullOrEmpty(value)),
                    AssemblyName = group.Select(item => item.AssemblyName).FirstOrDefault(value => !string.IsNullOrEmpty(value)),
                    Origin = group.Select(item => item.Origin).FirstOrDefault(value => !string.IsNullOrEmpty(value)),
                    PackageName = group.Select(item => item.PackageName).FirstOrDefault(value => !string.IsNullOrEmpty(value)),
                    FindingCount = group.Count(),
                    HighRiskCount = group.Count(item => item.Risk == DomainReloadRisk.High),
                    ReloadCallbackCount = group.Count(item => item.Kind == DomainReloadFindingKind.ReloadCallback)
                })
                .OrderByDescending(item => item.HighRiskCount)
                .ThenByDescending(item => item.ReloadCallbackCount)
                .ThenByDescending(item => item.FindingCount)
                .ThenBy(item => item.AssetPath, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (DomainReloadAssemblyInfo assembly in audit.Assemblies)
            {
                List<DomainReloadFinding> findings = audit.Findings.Where(item =>
                    string.Equals(item.AssemblyName, assembly.Name, StringComparison.OrdinalIgnoreCase)).ToList();
                assembly.FindingCount = findings.Count;
                assembly.HighRiskCount = findings.Count(item => item.Risk == DomainReloadRisk.High);
                assembly.ReloadCallbackCount = findings.Count(item => item.Kind == DomainReloadFindingKind.ReloadCallback);
            }
        }

        private static void ScanLiveObjects(DomainReloadAudit audit)
        {
            Dictionary<(Type type, string kind), int> counts = new();
            CountObjects(Resources.FindObjectsOfTypeAll<MonoBehaviour>(), "MonoBehaviour", counts);
            CountObjects(Resources.FindObjectsOfTypeAll<ScriptableObject>(), "ScriptableObject", counts);
            CountObjects(Resources.FindObjectsOfTypeAll<EditorWindow>(), "EditorWindow", counts);

            foreach (KeyValuePair<(Type type, string kind), int> pair in counts)
            {
                string assemblyName = pair.Key.type.Assembly.GetName().Name;
                TryGetAssemblyOrigin(assemblyName, audit, out string origin, out _);
                audit.LiveObjects.Add(new DomainReloadObjectInfo
                {
                    TypeName = pair.Key.type.FullName,
                    AssemblyName = assemblyName,
                    Kind = pair.Key.kind,
                    Origin = origin ?? "Unity/External",
                    Count = pair.Value
                });
            }
        }

        private static void CountObjects<T>(IEnumerable<T> objects, string kind,
            Dictionary<(Type type, string kind), int> counts) where T : UnityEngine.Object
        {
            foreach (T item in objects)
            {
                if (item == null)
                    continue;
                (Type type, string kind) key = (item.GetType(), kind);
                counts.TryGetValue(key, out int count);
                counts[key] = count + 1;
            }
        }

        private static bool TryGetAssemblyOrigin(string assemblyName, DomainReloadAudit audit,
            out string origin, out string package)
        {
            DomainReloadAssemblyInfo info = audit.Assemblies.FirstOrDefault(a =>
                string.Equals(a.Name, assemblyName, StringComparison.OrdinalIgnoreCase));
            origin = info?.Origin;
            package = info?.PackageName;
            return info != null && (origin == "Project" || origin.EndsWith(" Package", StringComparison.Ordinal));
        }

        private static DomainReloadFinding CreateFinding(ScanRoot root, string file, int line,
            DomainReloadFindingKind kind, DomainReloadRisk risk, string symbol, string detail, string evidence)
        {
            string relative = Path.GetRelativePath(root.FullPath, file).Replace('\\', '/');
            return new DomainReloadFinding
            {
                Kind = kind,
                Risk = risk,
                Symbol = symbol,
                Detail = detail,
                AssetPath = root.AssetPath.TrimEnd('/') + "/" + relative,
                FullPath = file,
                Line = line,
                Origin = root.Origin,
                PackageName = root.PackageName,
                Evidence = evidence?.Trim()
            };
        }

        private static ScanRoot FindRoot(string path, IReadOnlyList<ScanRoot> roots)
        {
            if (string.IsNullOrEmpty(path))
                return null;
            string full;
            try { full = Path.GetFullPath(path); }
            catch { return null; }
            return roots.OrderByDescending(root => root.FullPath.Length)
                .FirstOrDefault(root => IsUnder(full, root.FullPath));
        }

        private static bool IsUnder(string path, string root)
        {
            return path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                                   Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || PathsEqual(path, root);
        }

        private static bool PathsEqual(string left, string right)
        {
            return string.Equals(left.TrimEnd('\\', '/'), right.TrimEnd('\\', '/'),
                StringComparison.OrdinalIgnoreCase);
        }

        private static int[] BuildLineStarts(string text)
        {
            List<int> starts = new() { 0 };
            for (int i = 0; i < text.Length; i++)
                if (text[i] == '\n' && i + 1 < text.Length)
                    starts.Add(i + 1);
            return starts.ToArray();
        }

        private static int GetLineNumber(int[] lineStarts, int index)
        {
            int found = Array.BinarySearch(lineStarts, index);
            return found >= 0 ? found + 1 : ~found;
        }

        private static string GetSourceLine(string[] lines, int oneBasedLine)
        {
            if (oneBasedLine <= 0)
                return string.Empty;
            return oneBasedLine <= lines.Length ? lines[oneBasedLine - 1].Trim() : string.Empty;
        }

        private static string MaskCommentsAndStrings(string source)
        {
            StringBuilder result = new(source.Length);
            bool lineComment = false, blockComment = false, quoted = false, verbatim = false, character = false;
            for (int i = 0; i < source.Length; i++)
            {
                char c = source[i];
                char next = i + 1 < source.Length ? source[i + 1] : '\0';
                if (lineComment)
                {
                    if (c == '\n') { lineComment = false; result.Append('\n'); }
                    else result.Append(' ');
                    continue;
                }
                if (blockComment)
                {
                    if (c == '*' && next == '/') { result.Append("  "); i++; blockComment = false; }
                    else result.Append(c == '\n' ? '\n' : ' ');
                    continue;
                }
                if (quoted || character)
                {
                    if (!verbatim && c == '\\' && i + 1 < source.Length)
                    {
                        result.Append("  "); i++; continue;
                    }
                    if ((quoted && c == '"') || (character && c == '\''))
                    {
                        quoted = false; character = false; verbatim = false;
                    }
                    result.Append(c == '\n' ? '\n' : ' ');
                    continue;
                }
                if (c == '/' && next == '/') { result.Append("  "); i++; lineComment = true; continue; }
                if (c == '/' && next == '*') { result.Append("  "); i++; blockComment = true; continue; }
                if (c == '@' && next == '"') { result.Append("  "); i++; quoted = true; verbatim = true; continue; }
                if (c == '"') { result.Append(' '); quoted = true; continue; }
                if (c == '\'') { result.Append(' '); character = true; continue; }
                result.Append(c);
            }
            return result.ToString();
        }
    }
}
