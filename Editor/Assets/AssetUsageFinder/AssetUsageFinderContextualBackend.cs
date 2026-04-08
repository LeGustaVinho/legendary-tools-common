using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace LegendaryTools.Editor
{
    public sealed class AssetUsageFinderContextualBackend
    {
        private static readonly string[] SupportedExtensions =
        {
            ".asset", ".mat", ".controller", ".anim", ".overrideController", ".shader", ".compute", ".playable",
            ".prefab", ".unity"
        };

        private ScanSession _session;

        public void StartScan(
            AssetUsageFinderContextualRequest request,
            AssetUsageFinderSearchScope searchScope,
            Action<float, string> progressCallback,
            Action<List<AssetUsageFinderContextualResult>> completedCallback,
            Action canceledCallback,
            Action<Exception> errorCallback,
            CancellationToken cancellationToken)
        {
            CancelScan();

            if (request == null)
            {
                completedCallback?.Invoke(new List<AssetUsageFinderContextualResult>());
                return;
            }

            List<AssetUsageFinderScopeTarget> targets =
                AssetUsageFinderSearchScopeUtility.CollectTargets(searchScope, SupportedExtensions);

            _session = new ScanSession(
                request,
                targets,
                progressCallback,
                completedCallback,
                canceledCallback,
                errorCallback,
                cancellationToken);

            EditorApplication.update += _session.Tick;
        }

        public void CancelScan()
        {
            if (_session == null)
                return;

            _session.RequestCancel();
        }

        private sealed class ScanSession
        {
            private readonly AssetUsageFinderContextualRequest _request;
            private readonly List<AssetUsageFinderScopeTarget> _targets;
            private readonly Action<float, string> _progress;
            private readonly Action<List<AssetUsageFinderContextualResult>> _completed;
            private readonly Action _canceled;
            private readonly Action<Exception> _error;
            private readonly CancellationToken _token;

            private readonly List<AssetUsageFinderContextualResult> _results = new();
            private readonly HashSet<string> _seenResults = new(StringComparer.OrdinalIgnoreCase);

            private readonly Scene _activeScene;
            private int _index;
            private bool _cancelRequested;

            public ScanSession(
                AssetUsageFinderContextualRequest request,
                List<AssetUsageFinderScopeTarget> targets,
                Action<float, string> progress,
                Action<List<AssetUsageFinderContextualResult>> completed,
                Action canceled,
                Action<Exception> error,
                CancellationToken token)
            {
                _request = request;
                _targets = targets ?? new List<AssetUsageFinderScopeTarget>();
                _progress = progress;
                _completed = completed;
                _canceled = canceled;
                _error = error;
                _token = token;
                _activeScene = EditorSceneManager.GetActiveScene();
            }

            public void RequestCancel()
            {
                _cancelRequested = true;
            }

            public void Tick()
            {
                try
                {
                    if (_cancelRequested || _token.IsCancellationRequested)
                    {
                        Cancel();
                        return;
                    }

                    if (_targets.Count == 0)
                    {
                        Complete();
                        return;
                    }

                    const int targetsPerTick = 2;
                    int processedThisTick = 0;

                    while (processedThisTick < targetsPerTick && _index < _targets.Count)
                    {
                        if (_cancelRequested || _token.IsCancellationRequested)
                            break;

                        AssetUsageFinderScopeTarget target = _targets[_index++];
                        processedThisTick++;

                        float progress = Mathf.Clamp01((float)_index / _targets.Count);
                        _progress?.Invoke(progress, $"Scanning {target.GetProgressLabel()}");

                        ScanSingleTarget(target);
                    }

                    if (_cancelRequested || _token.IsCancellationRequested)
                    {
                        Cancel();
                        return;
                    }

                    if (_index >= _targets.Count)
                        Complete();
                }
                catch (Exception ex)
                {
                    CleanupAndDetach();
                    RestoreActiveSceneBestEffort();
                    _error?.Invoke(ex);
                }
            }

            private void Cancel()
            {
                CleanupAndDetach();
                RestoreActiveSceneBestEffort();
                _canceled?.Invoke();
            }

            private void Complete()
            {
                CleanupAndDetach();
                RestoreActiveSceneBestEffort();
                _completed?.Invoke(_results);
            }

            private void CleanupAndDetach()
            {
                EditorApplication.update -= Tick;
            }

            private void RestoreActiveSceneBestEffort()
            {
                if (!_activeScene.IsValid() || !_activeScene.isLoaded)
                    return;

                Scene current = EditorSceneManager.GetActiveScene();
                if (current == _activeScene)
                    return;

                try
                {
                    EditorSceneManager.SetActiveScene(_activeScene);
                }
                catch
                {
                    // ignored
                }
            }

            private void ScanSingleTarget(AssetUsageFinderScopeTarget target)
            {
                if (target == null)
                    return;

                switch (target.Kind)
                {
                    case AssetUsageFinderScopeTargetKind.OpenScene:
                        ScanOpenScene(target.AssetPath);
                        return;
                    case AssetUsageFinderScopeTargetKind.OpenPrefabStage:
                        ScanOpenPrefabStage(target.AssetPath);
                        return;
                }

                string assetPath = target.AssetPath;
                string extension = Path.GetExtension(assetPath);

                if (string.Equals(extension, ".unity", StringComparison.OrdinalIgnoreCase))
                {
                    ScanScene(assetPath, false);
                    return;
                }

                if (string.Equals(extension, ".prefab", StringComparison.OrdinalIgnoreCase))
                {
                    ScanPrefab(assetPath);
                    return;
                }

                ScanAssetFile(assetPath);
            }

            private void ScanAssetFile(string assetPath)
            {
                Object[] objects = AssetDatabase.LoadAllAssetsAtPath(assetPath);
                if (objects == null || objects.Length == 0)
                    return;

                foreach (Object obj in objects)
                {
                    if (obj == null)
                        continue;

                    ScanSerializedObject(assetPath, obj, obj.name);
                }
            }

            private void ScanPrefab(string prefabPath)
            {
                GameObject root = null;

                try
                {
                    root = PrefabUtility.LoadPrefabContents(prefabPath);
                    if (root == null)
                        return;

                    ScanGameObjectHierarchy(prefabPath, root);
                }
                finally
                {
                    if (root != null)
                        PrefabUtility.UnloadPrefabContents(root);
                }
            }

            private void ScanScene(string scenePath, bool useLoadedSceneOnly)
            {
                if (!File.Exists(scenePath))
                    return;

                Scene scene = SceneManager.GetSceneByPath(scenePath);
                bool openedHere = !scene.IsValid() || !scene.isLoaded;

                try
                {
                    if (useLoadedSceneOnly && openedHere)
                        return;

                    if (openedHere)
                        scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);

                    if (!scene.IsValid() || !scene.isLoaded)
                        return;

                    foreach (GameObject root in scene.GetRootGameObjects())
                    {
                        if (root != null)
                            ScanGameObjectHierarchy(scenePath, root);
                    }
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
                }
            }

            private void ScanOpenScene(string sceneLabel)
            {
                if (!AssetUsageFinderSearchScopeUtility.TryGetCurrentOpenScene(out Scene scene))
                    return;

                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    if (root != null)
                        ScanGameObjectHierarchy(sceneLabel, root);
                }
            }

            private void ScanOpenPrefabStage(string prefabPath)
            {
                PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
                if (stage == null || stage.prefabContentsRoot == null)
                    return;

                if (!string.Equals(stage.assetPath, prefabPath, StringComparison.OrdinalIgnoreCase))
                    return;

                ScanGameObjectHierarchy(prefabPath, stage.prefabContentsRoot);
            }

            private void ScanGameObjectHierarchy(string fileAssetPath, GameObject root)
            {
                if (root == null)
                    return;

                string hierarchyPath = GetHierarchyPath(root);
                ScanSerializedObject(fileAssetPath, root, hierarchyPath);

                foreach (Component component in root.GetComponents<Component>())
                {
                    if (component == null)
                        continue;

                    string objectPath = $"{hierarchyPath} ({component.GetType().Name})";
                    ScanSerializedObject(fileAssetPath, component, objectPath);
                }

                foreach (Transform child in root.transform)
                {
                    if (child != null)
                        ScanGameObjectHierarchy(fileAssetPath, child.gameObject);
                }
            }

            private void ScanSerializedObject(string fileAssetPath, Object owner, string objectPath)
            {
                if (owner == null)
                    return;

                SerializedObject serializedObject;
                try
                {
                    serializedObject = new SerializedObject(owner);
                }
                catch
                {
                    return;
                }

                switch (_request.TargetKind)
                {
                    case AssetUsageFinderContextualTargetKind.GameObject:
                    case AssetUsageFinderContextualTargetKind.Component:
                        ScanObjectReferenceUsage(fileAssetPath, owner, objectPath, serializedObject);
                        break;
                    case AssetUsageFinderContextualTargetKind.SerializedProperty:
                        ScanSerializedPropertyUsage(fileAssetPath, owner, objectPath, serializedObject);
                        break;
                }
            }

            private void ScanObjectReferenceUsage(
                string fileAssetPath,
                Object owner,
                string objectPath,
                SerializedObject serializedObject)
            {
                HashSet<string> matchedPropertyPaths = new(StringComparer.Ordinal);

                SerializedProperty iterator = serializedObject.GetIterator();
                bool enterChildren = true;

                while (iterator.NextVisible(enterChildren))
                {
                    enterChildren = false;

                    if (iterator.propertyPath == "m_Script" ||
                        iterator.propertyType != SerializedPropertyType.ObjectReference)
                        continue;

                    if (!_request.MatchesTargetReference(iterator.objectReferenceValue))
                        continue;

                    AddObjectReferenceResult(fileAssetPath, owner, objectPath, iterator.Copy(), matchedPropertyPaths,
                        false);
                }

                ScanHiddenUnityEventTargetReferences(fileAssetPath, owner, objectPath, serializedObject,
                    matchedPropertyPaths);
            }

            private void ScanSerializedPropertyUsage(
                string fileAssetPath,
                Object owner,
                string objectPath,
                SerializedObject serializedObject)
            {
                if (_request.OwnerType == null ||
                    !_request.OwnerType.IsAssignableFrom(owner.GetType()))
                    return;

                if (string.IsNullOrEmpty(_request.PropertyPath))
                    return;

                SerializedProperty property = serializedObject.FindProperty(_request.PropertyPath);
                if (property == null)
                    return;

                if (!_request.MatchesSerializedPropertyValue(property))
                    return;

                string matchDescription = _request.MatchValue
                    ? "Matches property path and value"
                    : "Matches property path";
                AssetUsageFinderContextualReferenceInfo referenceInfo = _request.MatchValue
                    ? new AssetUsageFinderContextualReferenceInfo(
                        AssetUsageFinderContextualReferenceKind.SerializedPropertyValueMatch,
                        "Property Path + Value",
                        "The owner type, property path, and current serialized value all match the query.")
                    : new AssetUsageFinderContextualReferenceInfo(
                        AssetUsageFinderContextualReferenceKind.SerializedPropertyPathMatch,
                        "Property Path",
                        "The owner type and property path match the query. The current value is not part of the match.");

                AddResult(
                    fileAssetPath,
                    objectPath,
                    owner.GetType().FullName,
                    property.propertyPath,
                    PropertyToString(property),
                    matchDescription,
                    referenceInfo,
                    BuildPrefabProvenance(owner, fileAssetPath));
            }

            private void AddResult(
                string fileAssetPath,
                string objectPath,
                string objectTypeName,
                string propertyPath,
                string currentValue,
                string matchDescription,
                AssetUsageFinderContextualReferenceInfo referenceInfo,
                AssetUsageFinderPrefabProvenanceInfo provenanceInfo)
            {
                AssetUsageFinderUsageType usageType = GetUsageType(fileAssetPath);
                string dedupeKey = string.Join("|",
                    fileAssetPath ?? string.Empty,
                    objectPath ?? string.Empty,
                    propertyPath ?? string.Empty,
                    matchDescription ?? string.Empty);

                if (!_seenResults.Add(dedupeKey))
                    return;

                _results.Add(new AssetUsageFinderContextualResult(
                    fileAssetPath,
                    objectPath,
                    objectTypeName,
                    propertyPath,
                    currentValue,
                    matchDescription,
                    usageType,
                    referenceInfo,
                    provenanceInfo));
            }

            private void ScanHiddenUnityEventTargetReferences(
                string fileAssetPath,
                Object owner,
                string objectPath,
                SerializedObject serializedObject,
                HashSet<string> matchedPropertyPaths)
            {
                SerializedProperty iterator = serializedObject.GetIterator();
                if (!iterator.Next(true))
                    return;

                do
                {
                    if (iterator.propertyType != SerializedPropertyType.ObjectReference)
                        continue;

                    if (!IsHiddenUnityEventTargetProperty(iterator.propertyPath))
                        continue;

                    if (!_request.MatchesTargetReference(iterator.objectReferenceValue))
                        continue;

                    AddObjectReferenceResult(
                        fileAssetPath,
                        owner,
                        objectPath,
                        iterator.Copy(),
                        matchedPropertyPaths,
                        true);
                } while (iterator.Next(true));
            }

            private void AddObjectReferenceResult(
                string fileAssetPath,
                Object owner,
                string objectPath,
                SerializedProperty property,
                HashSet<string> matchedPropertyPaths,
                bool isUnityEventTarget)
            {
                if (property == null)
                    return;

                string propertyPath = property.propertyPath;
                if (!matchedPropertyPaths.Add(propertyPath))
                    return;

                AssetUsageFinderContextualReferenceInfo referenceInfo =
                    BuildObjectReferenceClassification(property, isUnityEventTarget);

                string matchDescription = isUnityEventTarget
                    ? "References target via UnityEvent"
                    : _request.TargetKind == AssetUsageFinderContextualTargetKind.GameObject
                        ? "References target GameObject or one of its components"
                        : "References target Component";

                AddResult(
                    fileAssetPath,
                    objectPath,
                    owner.GetType().FullName,
                    propertyPath,
                    PropertyToString(property),
                    matchDescription,
                    referenceInfo,
                    BuildPrefabProvenance(owner, fileAssetPath));
            }

            private AssetUsageFinderContextualReferenceInfo BuildObjectReferenceClassification(
                SerializedProperty property,
                bool isUnityEventTarget)
            {
                if (isUnityEventTarget)
                {
                    return new AssetUsageFinderContextualReferenceInfo(
                        AssetUsageFinderContextualReferenceKind.UnityEventTarget,
                        "UnityEvent Target",
                        "Persistent UnityEvent call target serialized by the UnityEvent system.");
                }

                Object referencedObject = property != null ? property.objectReferenceValue : null;
                if (_request.TargetKind == AssetUsageFinderContextualTargetKind.GameObject)
                {
                    bool isDirectGameObjectReference = _request.TargetSnapshot != null &&
                                                       _request.TargetSnapshot.Matches(referencedObject);

                    if (isDirectGameObjectReference)
                    {
                        return new AssetUsageFinderContextualReferenceInfo(
                            AssetUsageFinderContextualReferenceKind.GameObjectReference,
                            "GameObject Reference",
                            "Serialized object reference that points directly to the target GameObject.");
                    }

                    return new AssetUsageFinderContextualReferenceInfo(
                        AssetUsageFinderContextualReferenceKind.AttachedComponentReference,
                        "Attached Component Reference",
                        "Serialized object reference that points to one of the components attached to the target GameObject.");
                }

                return new AssetUsageFinderContextualReferenceInfo(
                    AssetUsageFinderContextualReferenceKind.ComponentReference,
                    "Component Reference",
                    "Serialized object reference that points directly to the target component.");
            }

            private static AssetUsageFinderPrefabProvenanceInfo BuildPrefabProvenance(Object owner, string fileAssetPath)
            {
                GameObject ownerGameObject = owner switch
                {
                    GameObject gameObject => gameObject,
                    Component component => component.gameObject,
                    _ => null
                };

                if (ownerGameObject == null)
                {
                    return new AssetUsageFinderPrefabProvenanceInfo(
                        AssetUsageFinderPrefabProvenanceKind.AssetObject,
                        "Asset Object",
                        "Serialized object stored directly in the asset file.");
                }

                if (owner is Component componentOwner && PrefabUtility.IsAddedComponentOverride(componentOwner))
                {
                    string sourcePath = GetPrefabSourcePath(ownerGameObject);
                    return new AssetUsageFinderPrefabProvenanceInfo(
                        AssetUsageFinderPrefabProvenanceKind.AddedComponentOverride,
                        "Added Component Override",
                        "Component added as an override on top of a prefab instance.",
                        sourcePath);
                }

                if (PrefabUtility.IsAddedGameObjectOverride(ownerGameObject))
                {
                    string sourcePath = GetPrefabSourcePath(ownerGameObject);
                    return new AssetUsageFinderPrefabProvenanceInfo(
                        AssetUsageFinderPrefabProvenanceKind.AddedGameObjectOverride,
                        "Added GameObject Override",
                        "GameObject added as an override inside a prefab instance hierarchy.",
                        sourcePath);
                }

                if (PrefabUtility.IsPartOfPrefabInstance(ownerGameObject))
                {
                    GameObject nearestRoot = PrefabUtility.GetNearestPrefabInstanceRoot(ownerGameObject);
                    GameObject outermostRoot = PrefabUtility.GetOutermostPrefabInstanceRoot(ownerGameObject);
                    string sourcePath = GetPrefabSourcePath(nearestRoot != null ? nearestRoot : ownerGameObject);

                    if (ownerGameObject == nearestRoot &&
                        outermostRoot != null &&
                        nearestRoot != outermostRoot)
                    {
                        return new AssetUsageFinderPrefabProvenanceInfo(
                            AssetUsageFinderPrefabProvenanceKind.NestedPrefabInstanceRoot,
                            "Nested Prefab Instance Root",
                            "Root object of a nested prefab instance inside another prefab or scene hierarchy.",
                            sourcePath);
                    }

                    if (ownerGameObject == nearestRoot)
                    {
                        return new AssetUsageFinderPrefabProvenanceInfo(
                            AssetUsageFinderPrefabProvenanceKind.PrefabInstanceRoot,
                            "Prefab Instance Root",
                            "Root object of a prefab instance.",
                            sourcePath);
                    }

                    return new AssetUsageFinderPrefabProvenanceInfo(
                        AssetUsageFinderPrefabProvenanceKind.PrefabInstanceChild,
                        "Prefab Instance Child",
                        "Child object that belongs to a prefab instance hierarchy.",
                        sourcePath);
                }

                if (PrefabUtility.IsPartOfPrefabAsset(ownerGameObject))
                {
                    PrefabAssetType assetType = PrefabUtility.GetPrefabAssetType(ownerGameObject);
                    string sourcePath = GetPrefabVariantBasePath(ownerGameObject, fileAssetPath);

                    if (assetType == PrefabAssetType.Variant)
                    {
                        return new AssetUsageFinderPrefabProvenanceInfo(
                            AssetUsageFinderPrefabProvenanceKind.PrefabVariantDefinition,
                            "Prefab Variant Definition",
                            "Object stored in a prefab variant asset definition.",
                            sourcePath);
                    }

                    return new AssetUsageFinderPrefabProvenanceInfo(
                        AssetUsageFinderPrefabProvenanceKind.PrefabAssetDefinition,
                        "Prefab Asset Definition",
                        "Object stored in a prefab asset definition.",
                        sourcePath);
                }

                bool isSceneUsage = AssetUsageFinderSearchScopeUtility.IsUnsavedOpenSceneKey(fileAssetPath) ||
                                    string.Equals(Path.GetExtension(fileAssetPath), ".unity",
                                        StringComparison.OrdinalIgnoreCase);

                return isSceneUsage
                    ? new AssetUsageFinderPrefabProvenanceInfo(
                        AssetUsageFinderPrefabProvenanceKind.SceneObject,
                        "Scene Object",
                        "Scene-local object that is not part of a prefab instance.")
                    : new AssetUsageFinderPrefabProvenanceInfo(
                        AssetUsageFinderPrefabProvenanceKind.AssetObject,
                        "Asset Object",
                        "Serialized object stored directly in the asset file.");
            }

            private static string GetPrefabSourcePath(GameObject gameObject)
            {
                if (gameObject == null)
                    return string.Empty;

                GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(gameObject);
                return source != null ? AssetDatabase.GetAssetPath(source) ?? string.Empty : string.Empty;
            }

            private static string GetPrefabVariantBasePath(GameObject gameObject, string fileAssetPath)
            {
                if (gameObject == null)
                    return string.Empty;

                GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(gameObject);
                if (source == null)
                    return string.Empty;

                string sourcePath = AssetDatabase.GetAssetPath(source) ?? string.Empty;
                return string.Equals(sourcePath, fileAssetPath, StringComparison.OrdinalIgnoreCase)
                    ? string.Empty
                    : sourcePath;
            }

            private static bool IsHiddenUnityEventTargetProperty(string propertyPath)
            {
                if (string.IsNullOrEmpty(propertyPath))
                    return false;

                return propertyPath.EndsWith(".m_Target", StringComparison.Ordinal) &&
                       propertyPath.Contains(".m_PersistentCalls.m_Calls.Array.data[", StringComparison.Ordinal);
            }

            private static AssetUsageFinderUsageType GetUsageType(string fileAssetPath)
            {
                if (AssetUsageFinderSearchScopeUtility.IsUnsavedOpenSceneKey(fileAssetPath))
                    return AssetUsageFinderUsageType.Scene;

                string extension = Path.GetExtension(fileAssetPath).ToLowerInvariant();
                return extension switch
                {
                    ".unity" => AssetUsageFinderUsageType.Scene,
                    ".prefab" => AssetUsageFinderUsageType.Prefab,
                    ".mat" => AssetUsageFinderUsageType.Material,
                    ".asset" => AssetDatabase.LoadAssetAtPath<Object>(fileAssetPath) is ScriptableObject
                        ? AssetUsageFinderUsageType.ScriptableObject
                        : AssetUsageFinderUsageType.Other,
                    _ => AssetUsageFinderUsageType.Other
                };
            }

            private static string PropertyToString(SerializedProperty property)
            {
                if (property == null)
                    return string.Empty;

                try
                {
                    return property.propertyType switch
                    {
                        SerializedPropertyType.Integer => property.longValue.ToString(),
                        SerializedPropertyType.Boolean => property.boolValue ? "true" : "false",
                        SerializedPropertyType.Float => property.doubleValue.ToString("R"),
                        SerializedPropertyType.String => property.stringValue ?? string.Empty,
                        SerializedPropertyType.ObjectReference => DescribeObjectValue(property.objectReferenceValue),
                        SerializedPropertyType.Enum => property.enumDisplayNames != null &&
                                                       property.enumValueIndex >= 0 &&
                                                       property.enumValueIndex < property.enumDisplayNames.Length
                            ? property.enumDisplayNames[property.enumValueIndex]
                            : property.enumValueIndex.ToString(),
                        SerializedPropertyType.Vector2 => property.vector2Value.ToString(),
                        SerializedPropertyType.Vector3 => property.vector3Value.ToString(),
                        SerializedPropertyType.Vector4 => property.vector4Value.ToString(),
                        SerializedPropertyType.Color => property.colorValue.ToString(),
                        SerializedPropertyType.Rect => property.rectValue.ToString(),
                        SerializedPropertyType.Bounds => property.boundsValue.ToString(),
                        SerializedPropertyType.AnimationCurve => property.animationCurveValue != null
                            ? $"Keys={property.animationCurveValue.length}"
                            : "null",
                        SerializedPropertyType.Quaternion => property.quaternionValue.ToString(),
                        _ => property.propertyPath
                    };
                }
                catch
                {
                    return property.propertyPath;
                }
            }

            private static string DescribeObjectValue(Object value)
            {
                if (value == null)
                    return "null";

                string assetPath = AssetDatabase.GetAssetPath(value);
                if (!string.IsNullOrEmpty(assetPath))
                    return assetPath;

                return value switch
                {
                    GameObject gameObject => GetHierarchyPath(gameObject),
                    Component component => $"{GetHierarchyPath(component.gameObject)} ({component.GetType().Name})",
                    _ => value.name
                };
            }

            private static string GetHierarchyPath(GameObject gameObject)
            {
                if (gameObject == null)
                    return string.Empty;

                string path = gameObject.name;
                Transform current = gameObject.transform;

                while (current.parent != null)
                {
                    current = current.parent;
                    path = current.name + "/" + path;
                }

                return path;
            }
        }
    }
}
