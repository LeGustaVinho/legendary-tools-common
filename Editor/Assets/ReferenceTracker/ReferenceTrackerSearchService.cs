using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LegendaryTools.Editor
{
    internal sealed class ReferenceTrackerSearchService
    {
        private static readonly string[] SupportedIndexExtensions =
        {
            ".prefab",
            ".asset",
            ".unity",
            ".mat",
            ".anim",
            ".controller",
            ".overrideController",
            ".shader",
            ".compute",
            ".playable",
        };

        private readonly ReferenceTrackerScopeResolver _scopeResolver;
        private readonly AssetGuidMapper _guidMapper = new AssetGuidMapper();
        private bool _indexBuilt;

        public static string GuidCachePath
        {
            get
            {
                return Path.Combine(
                    "Library",
                    "LegendaryTools",
                    "ReferenceTracker",
                    "AssetGuidMapperCache.json").Replace("\\", "/");
            }
        }

        public bool GuidCacheExists
        {
            get { return File.Exists(GuidCachePath); }
        }

        public ReferenceTrackerSearchService(ReferenceTrackerScopeResolver scopeResolver)
        {
            _scopeResolver = scopeResolver;
        }

        public static bool IsSupportedTarget(UnityEngine.Object target)
        {
            if (target == null)
            {
                return false;
            }

            if (target is GameObject || target is Component)
            {
                return true;
            }

            return !string.IsNullOrEmpty(AssetDatabase.GetAssetPath(target));
        }

        public async Task<ReferenceTrackerSearchResult> SearchAsync(
            UnityEngine.Object target,
            ReferenceTrackerSearchScope scopes,
            bool rebuildIndex,
            CancellationToken cancellationToken)
        {
            ReferenceTrackerSearchResult result = new ReferenceTrackerSearchResult
            {
                Status = "Select an asset, script, GameObject, or Component target.",
                DurationMs = 0d,
            };

            if (!IsSupportedTarget(target))
            {
                return result;
            }

            scopes = _scopeResolver.Normalize(scopes);

            if (scopes == ReferenceTrackerSearchScope.None)
            {
                result.Status = "Select at least one search scope.";
                return result;
            }

            ReferenceTrackerSearchTargetContext targetContext = BuildTargetContext(target);
            if (targetContext == null)
            {
                result.Status = "Unable to build the search target context.";
                return result;
            }

            Stopwatch stopwatch = Stopwatch.StartNew();
            HashSet<string> resultKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<string> notes = new List<string>();

            if ((scopes & ReferenceTrackerSearchScope.CurrentScene) != 0)
            {
                Scene currentScene = ResolveCurrentOpenScene();
                if (currentScene.IsValid() && currentScene.isLoaded)
                {
                    ScanSceneInstance(currentScene, targetContext, ReferenceTrackerSearchScope.CurrentScene, true,
                        result.Usages, resultKeys);
                }
                else
                {
                    notes.Add("Current Scene is not loaded.");
                }
            }

            if ((scopes & ReferenceTrackerSearchScope.PrefabMode) != 0)
            {
                PrefabStage prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
                if (prefabStage != null && prefabStage.prefabContentsRoot != null)
                {
                    ScanGameObjectHierarchy(
                        prefabStage.assetPath,
                        AssetDatabase.LoadMainAssetAtPath(prefabStage.assetPath),
                        ReferenceTrackerSearchScope.PrefabMode,
                        true,
                        targetContext,
                        prefabStage.prefabContentsRoot,
                        result.Usages,
                        resultKeys);
                }
                else
                {
                    notes.Add("Prefab Mode is not currently open.");
                }
            }

            if (targetContext.IsAssetTarget && _scopeResolver.HasAnyProjectScope(scopes))
            {
                if (rebuildIndex || !_indexBuilt)
                {
                    if (rebuildIndex || !TryLoadGuidCache())
                    {
                        await _guidMapper.MapProjectGUIDsAsync(SupportedIndexExtensions, null, cancellationToken);
                        cancellationToken.ThrowIfCancellationRequested();
                        _indexBuilt = true;
                    }
                }

                List<string> files = await _guidMapper.FindFilesContainingGuidAsync(targetContext.TargetGuid);
                cancellationToken.ThrowIfCancellationRequested();
                files.Sort(StringComparer.OrdinalIgnoreCase);

                for (int i = 0; i < files.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    string assetPath = NormalizeAssetPath(files[i]);
                    if (assetPath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (!_scopeResolver.IsPathInSelectedProjectScope(assetPath, scopes))
                    {
                        continue;
                    }

                    ScanAssetPath(assetPath, targetContext, result.Usages, resultKeys, true);
                }

                AddPrefabVariantParentResult(targetContext, scopes, result.Usages, resultKeys);
            }
            else if (!targetContext.IsAssetTarget && _scopeResolver.HasAnyProjectScope(scopes))
            {
                notes.Add("Project asset scopes require an asset target with a GUID.");
            }

            stopwatch.Stop();
            result.DurationMs = stopwatch.Elapsed.TotalMilliseconds;

            SortResults(result.Usages);

            string scopeLabel = FormatScopeLabel(scopes);
            string suffix = notes.Count > 0 ? " " + string.Join(" ", notes.ToArray()) : string.Empty;
            result.Status = result.Usages.Count == 0
                ? string.Format("No references found in {0}.{1}", scopeLabel, suffix)
                : string.Format("{0} reference(s) found in {1}.{2}", result.Usages.Count, scopeLabel, suffix);

            return result;
        }

        public async Task GenerateGuidCacheAsync(CancellationToken cancellationToken)
        {
            await _guidMapper.MapProjectGUIDsAsync(SupportedIndexExtensions, null, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            string directory = Path.GetDirectoryName(GuidCachePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            _guidMapper.SaveMappingToJson(GuidCachePath);
            _indexBuilt = true;
        }

        public bool DeleteGuidCache()
        {
            _guidMapper.ClearMapping();
            _indexBuilt = false;

            if (!File.Exists(GuidCachePath))
            {
                return false;
            }

            File.Delete(GuidCachePath);
            return true;
        }

        private bool TryLoadGuidCache()
        {
            if (_indexBuilt)
            {
                return true;
            }

            if (!File.Exists(GuidCachePath))
            {
                return false;
            }

            _indexBuilt = _guidMapper.LoadMappingFromJson(GuidCachePath);
            return _indexBuilt;
        }

        private static ReferenceTrackerSearchTargetContext BuildTargetContext(UnityEngine.Object target)
        {
            UnityEngine.Object effectiveTarget = ResolveEffectiveSearchTarget(target);

            ReferenceTrackerSearchTargetContext context = new ReferenceTrackerSearchTargetContext
            {
                OriginalTarget = effectiveTarget,
                TargetGameObject = effectiveTarget as GameObject,
                TargetComponent = effectiveTarget as Component,
                TargetAsset = effectiveTarget,
            };

            if (context.TargetComponent != null)
            {
                context.TargetGameObject = context.TargetComponent.gameObject;
            }

            context.TargetAssetPath = AssetDatabase.GetAssetPath(effectiveTarget);
            context.IsMonoScriptTarget = effectiveTarget is MonoScript;

            if (!string.IsNullOrEmpty(context.TargetAssetPath))
            {
                context.IsAssetTarget = AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
                    effectiveTarget,
                    out string guid,
                    out long localFileId);

                context.TargetGuid = guid;
                context.TargetLocalFileId = localFileId;

                UnityEngine.Object mainAsset = AssetDatabase.LoadMainAssetAtPath(context.TargetAssetPath);
                if (mainAsset != null)
                {
                    context.TargetAsset = mainAsset;
                }
            }

            return context;
        }

        private static UnityEngine.Object ResolveEffectiveSearchTarget(UnityEngine.Object target)
        {
            Texture2D texture = target as Texture2D;
            if (texture == null)
            {
                return target;
            }

            Sprite singleSprite = TryGetSingleSpriteFromTexture(texture);
            return singleSprite != null ? singleSprite : target;
        }

        private static Sprite TryGetSingleSpriteFromTexture(Texture2D texture)
        {
            string assetPath = AssetDatabase.GetAssetPath(texture);
            if (string.IsNullOrEmpty(assetPath))
            {
                return null;
            }

            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null ||
                importer.textureType != TextureImporterType.Sprite ||
                importer.spriteImportMode != SpriteImportMode.Single)
            {
                return null;
            }

            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
            if (sprite != null)
            {
                return sprite;
            }

            UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
            for (int i = 0; i < assets.Length; i++)
            {
                sprite = assets[i] as Sprite;
                if (sprite != null)
                {
                    return sprite;
                }
            }

            return null;
        }

        private void ScanAssetPath(
            string assetPath,
            ReferenceTrackerSearchTargetContext targetContext,
            List<ReferenceTrackerUsageResult> results,
            HashSet<string> resultKeys,
            bool addFallbackWhenNoSerializedMatch)
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                return;
            }

            int countBefore = results.Count;
            string extension = Path.GetExtension(assetPath);
            ReferenceTrackerSearchScope sourceScope = _scopeResolver.GetProjectScopeForPath(assetPath);

            if (string.Equals(extension, ".unity", StringComparison.OrdinalIgnoreCase))
            {
                ScanSceneAsset(assetPath, targetContext, sourceScope, results, resultKeys);
            }
            else if (string.Equals(extension, ".prefab", StringComparison.OrdinalIgnoreCase))
            {
                ScanPrefabAsset(assetPath, targetContext, sourceScope, results, resultKeys);
            }
            else if (!assetPath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
            {
                ScanSerializedAssetFile(assetPath, targetContext, sourceScope, results, resultKeys);
            }

            if (addFallbackWhenNoSerializedMatch && results.Count == countBefore)
            {
                if (!ShouldSuppressFallbackGuidResult(assetPath, targetContext.TargetGuid))
                {
                    AddFallbackGuidResult(assetPath, targetContext, sourceScope, results, resultKeys);
                }
            }
        }

        private void ScanSerializedAssetFile(
            string assetPath,
            ReferenceTrackerSearchTargetContext targetContext,
            ReferenceTrackerSearchScope sourceScope,
            List<ReferenceTrackerUsageResult> results,
            HashSet<string> resultKeys)
        {
            UnityEngine.Object assetObject = AssetDatabase.LoadMainAssetAtPath(assetPath);
            UnityEngine.Object[] objects = AssetDatabase.LoadAllAssetsAtPath(assetPath);
            if (objects == null || objects.Length == 0)
            {
                return;
            }

            for (int i = 0; i < objects.Length; i++)
            {
                UnityEngine.Object obj = objects[i];
                if (obj == null)
                {
                    continue;
                }

                ScanSerializedObject(
                    assetPath,
                    assetObject,
                    sourceScope,
                    true,
                    targetContext,
                    obj,
                    null,
                    null,
                    obj.name,
                    results,
                    resultKeys);
            }
        }

        private void ScanPrefabAsset(
            string prefabPath,
            ReferenceTrackerSearchTargetContext targetContext,
            ReferenceTrackerSearchScope sourceScope,
            List<ReferenceTrackerUsageResult> results,
            HashSet<string> resultKeys)
        {
            GameObject root = null;
            UnityEngine.Object assetObject = AssetDatabase.LoadMainAssetAtPath(prefabPath);

            try
            {
                root = PrefabUtility.LoadPrefabContents(prefabPath);
                if (root == null)
                {
                    return;
                }

                ScanGameObjectHierarchy(prefabPath, assetObject, sourceScope, false, targetContext, root, results,
                    resultKeys);
            }
            finally
            {
                if (root != null)
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }
        }

        private void ScanSceneAsset(
            string scenePath,
            ReferenceTrackerSearchTargetContext targetContext,
            ReferenceTrackerSearchScope sourceScope,
            List<ReferenceTrackerUsageResult> results,
            HashSet<string> resultKeys)
        {
            if (!File.Exists(scenePath))
            {
                return;
            }

            Scene scene = SceneManager.GetSceneByPath(scenePath);
            bool openedHere = !scene.IsValid() || !scene.isLoaded;
            Scene activeScene = EditorSceneManager.GetActiveScene();

            try
            {
                if (openedHere)
                {
                    scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
                }

                if (!scene.IsValid() || !scene.isLoaded)
                {
                    return;
                }

                ScanSceneInstance(scene, targetContext, sourceScope, !openedHere, results, resultKeys);
            }
            finally
            {
                if (openedHere && scene.IsValid() && scene.isLoaded)
                {
                    try
                    {
                        EditorSceneManager.CloseScene(scene, true);
                    }
                    catch
                    {
                        // ignored
                    }
                }

                if (activeScene.IsValid() && activeScene.isLoaded)
                {
                    try
                    {
                        EditorSceneManager.SetActiveScene(activeScene);
                    }
                    catch
                    {
                        // ignored
                    }
                }
            }
        }

        private void ScanSceneInstance(
            Scene scene,
            ReferenceTrackerSearchTargetContext targetContext,
            ReferenceTrackerSearchScope sourceScope,
            bool isLiveContext,
            List<ReferenceTrackerUsageResult> results,
            HashSet<string> resultKeys)
        {
            UnityEngine.Object sceneAsset = string.IsNullOrEmpty(scene.path)
                ? null
                : AssetDatabase.LoadAssetAtPath<SceneAsset>(scene.path);

            string assetPath = string.IsNullOrEmpty(scene.path)
                ? string.Format("<Unsaved Scene: {0}>", scene.name)
                : scene.path;

            GameObject[] rootObjects = scene.GetRootGameObjects();
            for (int i = 0; i < rootObjects.Length; i++)
            {
                GameObject root = rootObjects[i];
                if (root == null)
                {
                    continue;
                }

                ScanGameObjectHierarchy(assetPath, sceneAsset, sourceScope, isLiveContext, targetContext, root, results,
                    resultKeys);
            }
        }

        private void ScanGameObjectHierarchy(
            string assetPath,
            UnityEngine.Object assetObject,
            ReferenceTrackerSearchScope sourceScope,
            bool isLiveContext,
            ReferenceTrackerSearchTargetContext targetContext,
            GameObject root,
            List<ReferenceTrackerUsageResult> results,
            HashSet<string> resultKeys)
        {
            if (root == null)
            {
                return;
            }

            string hierarchyPath = ReferenceTrackerFormatting.GetHierarchyPath(root);

            TryAddPrefabInstanceUsage(assetPath, assetObject, sourceScope, isLiveContext, targetContext, root,
                hierarchyPath, results, resultKeys);

            ScanSerializedObject(
                assetPath,
                assetObject,
                sourceScope,
                isLiveContext,
                targetContext,
                root,
                root,
                null,
                hierarchyPath,
                results,
                resultKeys);

            Component[] components = root.GetComponents<Component>();
            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (component == null)
                {
                    continue;
                }

                ScanSerializedObject(
                    assetPath,
                    assetObject,
                    sourceScope,
                    isLiveContext,
                    targetContext,
                    component,
                    root,
                    component,
                    hierarchyPath,
                    results,
                    resultKeys);
            }

            foreach (Transform child in root.transform)
            {
                if (child == null)
                {
                    continue;
                }

                ScanGameObjectHierarchy(assetPath, assetObject, sourceScope, isLiveContext, targetContext,
                    child.gameObject, results, resultKeys);
            }
        }

        private static void TryAddPrefabInstanceUsage(
            string assetPath,
            UnityEngine.Object assetObject,
            ReferenceTrackerSearchScope sourceScope,
            bool isLiveContext,
            ReferenceTrackerSearchTargetContext targetContext,
            GameObject candidate,
            string hierarchyPath,
            List<ReferenceTrackerUsageResult> results,
            HashSet<string> resultKeys)
        {
            if (!targetContext.IsAssetTarget ||
                string.IsNullOrEmpty(targetContext.TargetAssetPath) ||
                !targetContext.TargetAssetPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase) ||
                candidate == null ||
                !PrefabUtility.IsPartOfPrefabInstance(candidate))
            {
                return;
            }

            GameObject instanceRoot = PrefabUtility.GetNearestPrefabInstanceRoot(candidate);
            if (instanceRoot != candidate)
            {
                return;
            }

            GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(instanceRoot) as GameObject;
            string sourcePath = source != null ? AssetDatabase.GetAssetPath(source) : string.Empty;
            if (!string.Equals(sourcePath, targetContext.TargetAssetPath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            AddResult(results, resultKeys, new ReferenceTrackerUsageResult
            {
                AssetPath = assetPath,
                AssetLabel = GetAssetLabel(assetPath, assetObject),
                AssetKindLabel = GetAssetKindLabel(sourceScope, assetPath),
                AssetObject = assetObject,
                HostObject = isLiveContext ? candidate : null,
                HostGameObject = isLiveContext ? candidate : null,
                HostGameObjectPath = hierarchyPath,
                HostComponentLabel = "Prefab Instance",
                PropertyPath = "m_SourcePrefab",
                PropertyDisplayName = "Source Prefab",
                ReferenceTypeLabel = "Prefab Instance",
                ReferencedObject = source,
                SourceScope = sourceScope,
                IsLiveContext = isLiveContext,
            });
        }

        private void ScanSerializedObject(
            string assetPath,
            UnityEngine.Object assetObject,
            ReferenceTrackerSearchScope sourceScope,
            bool isLiveContext,
            ReferenceTrackerSearchTargetContext targetContext,
            UnityEngine.Object hostObject,
            GameObject hostGameObject,
            Component hostComponent,
            string hostPath,
            List<ReferenceTrackerUsageResult> results,
            HashSet<string> resultKeys)
        {
            SerializedObject serializedObject;

            try
            {
                serializedObject = new SerializedObject(hostObject);
            }
            catch
            {
                return;
            }

            SerializedProperty iterator = serializedObject.GetIterator();
            bool enterChildren = true;

            while (iterator.Next(enterChildren))
            {
                enterChildren = false;

                if (iterator.propertyType != SerializedPropertyType.ObjectReference)
                {
                    continue;
                }

                if (ShouldSkipUnityInternalReferenceProperty(iterator.propertyPath))
                {
                    continue;
                }

                UnityEngine.Object referencedObject = iterator.objectReferenceValue;
                if (referencedObject == null)
                {
                    continue;
                }

                if (!IsMatch(targetContext, referencedObject, out string referenceTypeLabel))
                {
                    continue;
                }

                ReferenceTrackerUsageResult usage = new ReferenceTrackerUsageResult
                {
                    AssetPath = assetPath,
                    AssetLabel = GetAssetLabel(assetPath, assetObject),
                    AssetKindLabel = GetAssetKindLabel(sourceScope, assetPath),
                    AssetObject = assetObject,
                    HostObject = isLiveContext || !(hostObject is GameObject) && !(hostObject is Component)
                        ? hostObject
                        : null,
                    HostGameObject = isLiveContext ? hostGameObject : null,
                    HostComponent = isLiveContext ? hostComponent : null,
                    HostGameObjectPath = hostPath,
                    HostComponentLabel = hostComponent != null
                        ? ReferenceTrackerFormatting.GetComponentLabel(hostComponent)
                        : GetHostObjectLabel(hostObject),
                    PropertyPath = iterator.propertyPath,
                    PropertyDisplayName = ReferenceTrackerFormatting.GetSerializedPropertyReferenceLabel(
                        iterator.propertyPath,
                        iterator.displayName),
                    ReferenceTypeLabel = referenceTypeLabel,
                    ReferencedObject = referencedObject,
                    SourceScope = sourceScope,
                    IsLiveContext = isLiveContext,
                };

                AddResult(results, resultKeys, usage);
            }
        }

        private static bool IsMatch(
            ReferenceTrackerSearchTargetContext targetContext,
            UnityEngine.Object referencedObject,
            out string referenceTypeLabel)
        {
            referenceTypeLabel = string.Empty;

            if (targetContext.IsAssetTarget)
            {
                if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(referencedObject, out string referencedGuid,
                        out long referencedLocalFileId) &&
                    string.Equals(referencedGuid, targetContext.TargetGuid, StringComparison.OrdinalIgnoreCase) &&
                    referencedLocalFileId == targetContext.TargetLocalFileId)
                {
                    referenceTypeLabel = targetContext.IsMonoScriptTarget
                        ? "Script"
                        : GetReferenceObjectTypeLabel(referencedObject);
                    return true;
                }

                return false;
            }

            if (targetContext.TargetComponent != null)
            {
                if (referencedObject == targetContext.TargetComponent)
                {
                    referenceTypeLabel = string.Format("Component ({0})", targetContext.TargetComponent.GetType().Name);
                    return true;
                }

                return false;
            }

            if (targetContext.TargetGameObject != null)
            {
                if (referencedObject == targetContext.TargetGameObject)
                {
                    referenceTypeLabel = "GameObject";
                    return true;
                }

                Component referencedComponent = referencedObject as Component;
                if (referencedComponent != null && referencedComponent.gameObject == targetContext.TargetGameObject)
                {
                    referenceTypeLabel = string.Format("Component ({0})", referencedComponent.GetType().Name);
                    return true;
                }
            }

            return false;
        }

        private static bool ShouldSkipUnityInternalReferenceProperty(string propertyPath)
        {
            if (string.IsNullOrEmpty(propertyPath))
            {
                return false;
            }

            return ContainsPropertySegment(propertyPath, "m_CorrespondingSourceObject") ||
                   ContainsPropertySegment(propertyPath, "m_PrefabInstance") ||
                   ContainsPropertySegment(propertyPath, "m_PrefabAsset") ||
                   ContainsPropertySegment(propertyPath, "m_GameObject") ||
                   ContainsPropertySegment(propertyPath, "m_Father") ||
                   ContainsPropertySegment(propertyPath, "m_Children");
        }

        private static bool ContainsPropertySegment(string propertyPath, string segment)
        {
            if (string.Equals(propertyPath, segment, StringComparison.Ordinal))
            {
                return true;
            }

            return propertyPath.StartsWith(segment + ".", StringComparison.Ordinal) ||
                   propertyPath.EndsWith("." + segment, StringComparison.Ordinal) ||
                   propertyPath.IndexOf("." + segment + ".", StringComparison.Ordinal) >= 0;
        }

        private void AddPrefabVariantParentResult(
            ReferenceTrackerSearchTargetContext targetContext,
            ReferenceTrackerSearchScope scopes,
            List<ReferenceTrackerUsageResult> results,
            HashSet<string> resultKeys)
        {
            if (!targetContext.IsAssetTarget ||
                string.IsNullOrEmpty(targetContext.TargetAssetPath) ||
                !targetContext.TargetAssetPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            GameObject prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(targetContext.TargetAssetPath);
            if (prefabAsset == null || PrefabUtility.GetPrefabAssetType(prefabAsset) != PrefabAssetType.Variant)
            {
                return;
            }

            GameObject parentPrefab = PrefabUtility.GetCorrespondingObjectFromSource(prefabAsset) as GameObject;
            string parentPath = parentPrefab != null ? AssetDatabase.GetAssetPath(parentPrefab) : string.Empty;
            if (string.IsNullOrEmpty(parentPath) || !_scopeResolver.IsPathInSelectedProjectScope(parentPath, scopes))
            {
                return;
            }

            ReferenceTrackerSearchScope sourceScope = _scopeResolver.GetProjectScopeForPath(parentPath);
            AddResult(results, resultKeys, new ReferenceTrackerUsageResult
            {
                AssetPath = parentPath,
                AssetLabel = GetAssetLabel(parentPath, parentPrefab),
                AssetKindLabel = GetAssetKindLabel(sourceScope, parentPath),
                AssetObject = parentPrefab,
                HostObject = parentPrefab,
                HostGameObjectPath = parentPrefab.name,
                HostComponentLabel = "Prefab Asset",
                PropertyPath = "<prefab variant parent>",
                PropertyDisplayName = "Prefab Variant Parent",
                ReferenceTypeLabel = "Parent Prefab",
                ReferencedObject = parentPrefab,
                SourceScope = sourceScope,
                IsLiveContext = true,
            });
        }

        private void AddFallbackGuidResult(
            string assetPath,
            ReferenceTrackerSearchTargetContext targetContext,
            ReferenceTrackerSearchScope sourceScope,
            List<ReferenceTrackerUsageResult> results,
            HashSet<string> resultKeys)
        {
            string objectAssetPath = assetPath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)
                ? assetPath.Substring(0, assetPath.Length - ".meta".Length)
                : assetPath;

            UnityEngine.Object assetObject = AssetDatabase.LoadMainAssetAtPath(objectAssetPath);

            AddResult(results, resultKeys, new ReferenceTrackerUsageResult
            {
                AssetPath = assetPath,
                AssetLabel = GetAssetLabel(assetPath, assetObject),
                AssetKindLabel = GetAssetKindLabel(sourceScope, assetPath),
                AssetObject = assetObject,
                HostObject = assetObject,
                HostGameObjectPath = string.Empty,
                HostComponentLabel = assetObject != null ? assetObject.GetType().Name : "Serialized File",
                PropertyPath = "<guid>",
                PropertyDisplayName = "GUID reference",
                ReferenceTypeLabel = targetContext.IsMonoScriptTarget ? "Script GUID" : "Asset GUID",
                ReferencedObject = targetContext.OriginalTarget,
                SourceScope = sourceScope,
                IsLiveContext = assetObject != null,
                IsFallback = true,
            });
        }

        private static bool ShouldSuppressFallbackGuidResult(string assetPath, string guid)
        {
            if (string.IsNullOrEmpty(assetPath) ||
                string.IsNullOrEmpty(guid) ||
                !File.Exists(assetPath))
            {
                return false;
            }

            bool foundGuid = false;

            try
            {
                using (StreamReader reader = new StreamReader(assetPath))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (line.IndexOf(guid, StringComparison.OrdinalIgnoreCase) < 0)
                        {
                            continue;
                        }

                        foundGuid = true;
                        if (!IsUnityInternalReferenceLine(line))
                        {
                            return false;
                        }
                    }
                }
            }
            catch
            {
                return false;
            }

            return foundGuid;
        }

        private static bool IsUnityInternalReferenceLine(string line)
        {
            return !string.IsNullOrEmpty(line) &&
                   (line.IndexOf("m_CorrespondingSourceObject", StringComparison.Ordinal) >= 0 ||
                    line.IndexOf("m_PrefabInstance", StringComparison.Ordinal) >= 0 ||
                    line.IndexOf("m_PrefabAsset", StringComparison.Ordinal) >= 0 ||
                    line.IndexOf("m_GameObject", StringComparison.Ordinal) >= 0 ||
                    line.IndexOf("m_Father", StringComparison.Ordinal) >= 0 ||
                    line.IndexOf("m_Children", StringComparison.Ordinal) >= 0);
        }

        private static bool AddResult(
            List<ReferenceTrackerUsageResult> results,
            HashSet<string> resultKeys,
            ReferenceTrackerUsageResult usage)
        {
            string key = string.Join("|", new[]
            {
                usage.AssetPath ?? string.Empty,
                usage.HostGameObjectPath ?? string.Empty,
                usage.HostComponentLabel ?? string.Empty,
                usage.PropertyPath ?? string.Empty,
                usage.ReferenceTypeLabel ?? string.Empty,
            });

            if (!resultKeys.Add(key))
            {
                return false;
            }

            results.Add(usage);
            return true;
        }

        private static Scene ResolveCurrentOpenScene()
        {
            Scene activeScene = EditorSceneManager.GetActiveScene();
            PrefabStage prefabStage = PrefabStageUtility.GetCurrentPrefabStage();

            if (prefabStage == null || !prefabStage.scene.IsValid() || activeScene.handle != prefabStage.scene.handle)
            {
                return activeScene;
            }

            Scene fallback = default;
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene candidate = SceneManager.GetSceneAt(i);
                if (!candidate.IsValid() || !candidate.isLoaded || candidate.handle == prefabStage.scene.handle)
                {
                    continue;
                }

                if (!fallback.IsValid() ||
                    (!string.IsNullOrEmpty(candidate.path) && string.IsNullOrEmpty(fallback.path)))
                {
                    fallback = candidate;
                }
            }

            return fallback;
        }

        private static void SortResults(List<ReferenceTrackerUsageResult> results)
        {
            results.Sort(CompareResults);
        }

        internal static int CompareResults(ReferenceTrackerUsageResult a, ReferenceTrackerUsageResult b)
        {
            int byAsset = string.Compare(a.AssetPath, b.AssetPath, StringComparison.OrdinalIgnoreCase);
            if (byAsset != 0)
            {
                return byAsset;
            }

            int byPath = string.Compare(a.HostGameObjectPath, b.HostGameObjectPath, StringComparison.OrdinalIgnoreCase);
            if (byPath != 0)
            {
                return byPath;
            }

            int byComponent = string.Compare(a.HostComponentLabel, b.HostComponentLabel, StringComparison.OrdinalIgnoreCase);
            if (byComponent != 0)
            {
                return byComponent;
            }

            return string.Compare(a.PropertyPath, b.PropertyPath, StringComparison.OrdinalIgnoreCase);
        }

        private static string FormatScopeLabel(ReferenceTrackerSearchScope scopes)
        {
            List<string> labels = new List<string>();

            if ((scopes & ReferenceTrackerSearchScope.CurrentScene) != 0)
            {
                labels.Add("Current Scene");
            }

            if ((scopes & ReferenceTrackerSearchScope.ScenesInProject) != 0)
            {
                labels.Add("Scenes");
            }

            if ((scopes & ReferenceTrackerSearchScope.PrefabMode) != 0)
            {
                labels.Add("Prefab Mode");
            }

            if ((scopes & ReferenceTrackerSearchScope.Prefabs) != 0)
            {
                labels.Add("Prefabs");
            }

            if ((scopes & ReferenceTrackerSearchScope.Materials) != 0)
            {
                labels.Add("Materials");
            }

            if ((scopes & ReferenceTrackerSearchScope.ScriptableObjects) != 0)
            {
                labels.Add("ScriptableObject");
            }

            if ((scopes & ReferenceTrackerSearchScope.Others) != 0)
            {
                labels.Add("Others");
            }

            if ((scopes & ReferenceTrackerSearchScope.AnimatorControllersAndAnimationClips) != 0)
            {
                labels.Add("Animator Controllers / Animation Clips");
            }

            if ((scopes & ReferenceTrackerSearchScope.TimelineAssets) != 0)
            {
                labels.Add("Timeline assets");
            }

            if ((scopes & ReferenceTrackerSearchScope.AddressablesGroups) != 0)
            {
                labels.Add("Addressables groups");
            }

            if ((scopes & ReferenceTrackerSearchScope.ResourcesFolders) != 0)
            {
                labels.Add("Resources folders");
            }

            if ((scopes & ReferenceTrackerSearchScope.AssetBundles) != 0)
            {
                labels.Add("Asset Bundles");
            }

            return labels.Count == 0 ? "selected scopes" : string.Join(", ", labels.ToArray());
        }

        private static string GetAssetKindLabel(ReferenceTrackerSearchScope sourceScope, string assetPath)
        {
            if (assetPath != null && assetPath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
            {
                return "Meta";
            }

            switch (sourceScope)
            {
                case ReferenceTrackerSearchScope.CurrentScene:
                    return "Current Scene";
                case ReferenceTrackerSearchScope.ScenesInProject:
                    return "Scene";
                case ReferenceTrackerSearchScope.PrefabMode:
                    return "Prefab Mode";
                case ReferenceTrackerSearchScope.Prefabs:
                    return "Prefab";
                case ReferenceTrackerSearchScope.Materials:
                    return "Material";
                case ReferenceTrackerSearchScope.ScriptableObjects:
                    return "ScriptableObject";
                case ReferenceTrackerSearchScope.Others:
                    return "Other";
                case ReferenceTrackerSearchScope.AnimatorControllersAndAnimationClips:
                    return "Animation";
                case ReferenceTrackerSearchScope.TimelineAssets:
                    return "Timeline";
                case ReferenceTrackerSearchScope.AddressablesGroups:
                    return "Addressables Group";
                case ReferenceTrackerSearchScope.ResourcesFolders:
                    return "Resources";
                case ReferenceTrackerSearchScope.AssetBundles:
                    return "Asset Bundle";
                default:
                    return "Asset";
            }
        }

        private static string GetAssetLabel(string assetPath, UnityEngine.Object assetObject)
        {
            if (assetObject != null)
            {
                return string.Format("{0} ({1})", assetObject.name, assetPath);
            }

            return string.IsNullOrEmpty(assetPath) ? "<unknown>" : assetPath;
        }

        private static string GetHostObjectLabel(UnityEngine.Object hostObject)
        {
            if (hostObject == null)
            {
                return "<none>";
            }

            return hostObject.GetType().Name;
        }

        private static string GetReferenceObjectTypeLabel(UnityEngine.Object referencedObject)
        {
            return referencedObject == null ? "Object" : referencedObject.GetType().Name;
        }

        private static string NormalizeAssetPath(string assetPath)
        {
            return string.IsNullOrEmpty(assetPath) ? string.Empty : assetPath.Replace("\\", "/");
        }
    }
}
