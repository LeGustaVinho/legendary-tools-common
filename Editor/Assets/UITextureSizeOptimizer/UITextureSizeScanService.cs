using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace LegendaryTools.Editor
{
    internal sealed class UITextureSizeScanService : IDisposable
    {
        private readonly Dictionary<string, UITextureOptimizationResult> _results =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _errors = new();
        private readonly Vector2 _simulatedScreenSize;
        private readonly UITextureRoundingMode _roundingMode;
        private readonly bool _useIncrementalCache;
        private readonly bool _forceRescan;
        private readonly string _configurationKey;
        private readonly UITextureScanCache _cache;
        private readonly Scene _originalActiveScene;
        private List<string> _assetPaths;
        private int _nextAssetIndex;
        private bool _cancelRequested;
        private bool _disposed;

        public UITextureSizeScanService(Vector2 simulatedScreenSize)
            : this(simulatedScreenSize, UITextureRoundingMode.Up, true, false)
        {
        }

        public UITextureSizeScanService(
            Vector2 simulatedScreenSize,
            UITextureRoundingMode roundingMode,
            bool useIncrementalCache,
            bool forceRescan)
        {
            _simulatedScreenSize = simulatedScreenSize;
            _roundingMode = roundingMode;
            _useIncrementalCache = useIncrementalCache;
            _forceRescan = forceRescan;
            _configurationKey = $"{Mathf.RoundToInt(simulatedScreenSize.x)}x{Mathf.RoundToInt(simulatedScreenSize.y)}:{roundingMode}";
            _cache = UITextureScanCache.Load();
            _originalActiveScene = EditorSceneManager.GetActiveScene();
            _assetPaths = FindAssetPaths();
        }

        public IReadOnlyList<UITextureOptimizationResult> Results => _results.Values
            .OrderByDescending(result => result.IsCandidate)
            .ThenBy(result => result.AssetPath, StringComparer.OrdinalIgnoreCase)
            .ToList();

        public IReadOnlyList<string> Errors => _errors;
        public int TotalAssets => _assetPaths.Count;
        public int ProcessedAssets => _nextAssetIndex;
        public float Progress => TotalAssets == 0 ? 1f : Mathf.Clamp01(_nextAssetIndex / (float)TotalAssets);
        public bool IsComplete => _cancelRequested || _nextAssetIndex >= TotalAssets;
        public bool WasCancelled => _cancelRequested;
        public int CacheHits { get; private set; }
        public int RescannedAssets { get; private set; }
        public string CurrentAssetPath { get; private set; } = string.Empty;

        public void Cancel()
        {
            _cancelRequested = true;
        }

        public void Tick()
        {
            if (_disposed || IsComplete)
            {
                return;
            }

            CurrentAssetPath = _assetPaths[_nextAssetIndex];
            try
            {
                string dependencyHash = AssetDatabase.GetAssetDependencyHash(CurrentAssetPath).ToString();
                if (_useIncrementalCache && !_forceRescan &&
                    _cache.TryGet(CurrentAssetPath, dependencyHash, _configurationKey, out IReadOnlyList<UITextureUsageRecord> cachedUsages))
                {
                    RestoreCachedUsages(cachedUsages);
                    CacheHits++;
                }
                else
                {
                    if (CurrentAssetPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                    {
                        ScanPrefab(CurrentAssetPath);
                    }
                    else
                    {
                        ScanScene(CurrentAssetPath);
                    }

                    RescannedAssets++;
                    if (_useIncrementalCache)
                    {
                        IEnumerable<UITextureUsageRecord> usages = _results.Values
                            .SelectMany(result => result.Usages)
                            .Where(usage => string.Equals(
                                usage.ContainerPath,
                                CurrentAssetPath,
                                StringComparison.OrdinalIgnoreCase));
                        _cache.Put(CurrentAssetPath, dependencyHash, _configurationKey, usages);
                    }
                }
            }
            catch (Exception exception)
            {
                _errors.Add($"{CurrentAssetPath}: {exception.Message}");
                Debug.LogException(exception);
            }
            finally
            {
                _nextAssetIndex++;
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (_useIncrementalCache)
            {
                _cache.Save(_assetPaths, !_cancelRequested && _nextAssetIndex >= TotalAssets);
            }
            RestoreActiveScene();
        }

        private static List<string> FindAssetPaths()
        {
            HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);
            foreach (string guid in AssetDatabase.FindAssets("t:Scene", new[] { "Assets" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.IsNullOrEmpty(path)) paths.Add(path);
            }

            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.IsNullOrEmpty(path)) paths.Add(path);
            }

            return paths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private void ScanPrefab(string prefabPath)
        {
            GameObject root = null;
            try
            {
                root = PrefabUtility.LoadPrefabContents(prefabPath);
                if (root != null)
                {
                    AnalyzeRoots(prefabPath, new[] { root });
                }
            }
            finally
            {
                if (root != null)
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }
        }

        private void ScanScene(string scenePath)
        {
            Scene scene = SceneManager.GetSceneByPath(scenePath);
            bool openedHere = !scene.IsValid() || !scene.isLoaded;
            try
            {
                if (openedHere)
                {
                    scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
                }

                if (scene.IsValid() && scene.isLoaded)
                {
                    AnalyzeRoots(scenePath, scene.GetRootGameObjects());
                }
            }
            finally
            {
                if (openedHere && scene.IsValid() && scene.isLoaded)
                {
                    EditorSceneManager.CloseScene(scene, true);
                }

                RestoreActiveScene();
            }
        }

        private void AnalyzeRoots(string containerPath, IReadOnlyList<GameObject> sourceRoots)
        {
            Scene previewScene = EditorSceneManager.NewPreviewScene();
            List<GameObject> clones = new();
            try
            {
                GameObject previewRoot = new("UI Texture Optimizer Preview Root");
                previewRoot.hideFlags = HideFlags.HideAndDontSave;
                SceneManager.MoveGameObjectToScene(previewRoot, previewScene);
                foreach (GameObject sourceRoot in sourceRoots)
                {
                    GameObject clone = Object.Instantiate(sourceRoot, previewRoot.transform, false);
                    clone.name = sourceRoot.name;
                    clone.hideFlags = HideFlags.HideAndDontSave;
                    DisableCustomBehaviours(clone);
                    SetHierarchyActive(clone.transform);
                    clones.Add(clone);
                }

                AddSyntheticCanvasesForCanvaslessRoots(clones);

                Canvas[] rootCanvases = clones
                    .SelectMany(root => root.GetComponentsInChildren<Canvas>(true)
                        .Concat(root.GetComponentsInParent<Canvas>(true)))
                    .Where(canvas => canvas.isRootCanvas)
                    .Distinct()
                    .ToArray();

                foreach (Canvas canvas in rootCanvases)
                {
                    ConfigureAndRebuildCanvas(canvas);
                }

                foreach (GameObject clone in clones)
                {
                    ScanGraphics(containerPath, clone, new UITextureDynamicLayoutDetector(clone));
                }
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(previewScene);
            }
        }

        private void AddSyntheticCanvasesForCanvaslessRoots(IEnumerable<GameObject> clones)
        {
            foreach (GameObject clone in clones)
            {
                bool hasGraphics = clone.GetComponentInChildren<Image>(true) != null ||
                                   clone.GetComponentInChildren<RawImage>(true) != null;
                if (!hasGraphics || clone.GetComponentInChildren<Canvas>(true) != null)
                {
                    continue;
                }

                GameObject canvasObject = new(
                    $"{clone.name} (Synthetic Canvas)",
                    typeof(RectTransform),
                    typeof(Canvas),
                    typeof(CanvasScaler));
                canvasObject.hideFlags = HideFlags.HideAndDontSave;
                SceneManager.MoveGameObjectToScene(canvasObject, clone.scene);
                canvasObject.transform.SetParent(clone.transform.parent, false);
                Canvas canvas = canvasObject.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = _simulatedScreenSize;
                clone.transform.SetParent(canvasObject.transform, false);
            }
        }

        private static void DisableCustomBehaviours(GameObject root)
        {
            foreach (MonoBehaviour behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour == null)
                {
                    continue;
                }

                string ns = behaviour.GetType().Namespace ?? string.Empty;
                if (!ns.StartsWith("UnityEngine.UI", StringComparison.Ordinal))
                {
                    behaviour.enabled = false;
                }
            }
        }

        private static void SetHierarchyActive(Transform transform)
        {
            transform.gameObject.SetActive(true);
            for (int i = 0; i < transform.childCount; i++)
            {
                SetHierarchyActive(transform.GetChild(i));
            }
        }

        private void ConfigureAndRebuildCanvas(Canvas canvas)
        {
            CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
            float scaleFactor = UITextureSizeCalculator.CalculateCanvasScaleFactor(scaler, _simulatedScreenSize);
            scaleFactor = Mathf.Max(0.01f, scaleFactor);

            if (scaler != null)
            {
                scaler.enabled = false;
            }

            canvas.scaleFactor = scaleFactor;
            if (canvas.renderMode != RenderMode.WorldSpace && canvas.transform is RectTransform canvasRect)
            {
                canvasRect.anchorMin = new Vector2(0.5f, 0.5f);
                canvasRect.anchorMax = new Vector2(0.5f, 0.5f);
                canvasRect.pivot = new Vector2(0.5f, 0.5f);
                canvasRect.sizeDelta = _simulatedScreenSize / scaleFactor;
                LayoutRebuilder.ForceRebuildLayoutImmediate(canvasRect);
                Canvas.ForceUpdateCanvases();
                LayoutRebuilder.ForceRebuildLayoutImmediate(canvasRect);
            }
        }

        private void ScanGraphics(
            string containerPath,
            GameObject root,
            UITextureDynamicLayoutDetector dynamicLayoutDetector)
        {
            foreach (Image image in root.GetComponentsInChildren<Image>(true))
            {
                Sprite sprite = image.overrideSprite != null ? image.overrideSprite : image.sprite;
                if (sprite == null)
                {
                    continue;
                }

                string texturePath = AssetDatabase.GetAssetPath(sprite);
                if (string.IsNullOrEmpty(texturePath))
                {
                    texturePath = AssetDatabase.GetAssetPath(sprite.texture);
                }

                bool specialMode = image.type is Image.Type.Sliced or Image.Type.Tiled;
                List<UITextureUsageReason> reasons = new();
                if (image.type == Image.Type.Sliced) reasons.Add(UITextureUsageReason.Sliced);
                if (image.type == Image.Type.Tiled) reasons.Add(UITextureUsageReason.Tiled);
                string warning = specialMode
                    ? $"Image mode {image.type} can change border or tile density after downscaling."
                    : string.Empty;
                AddUsage(
                    containerPath,
                    texturePath,
                    image.rectTransform,
                    image.canvas,
                    sprite.rect.size,
                    "Image",
                    sprite.name,
                    specialMode ? UITextureConfidence.Risky : UITextureConfidence.Safe,
                    reasons,
                    warning,
                    image.preserveAspect ? sprite.rect.width / Mathf.Max(1f, sprite.rect.height) : 0f,
                    dynamicLayoutDetector);
            }

            foreach (RawImage rawImage in root.GetComponentsInChildren<RawImage>(true))
            {
                if (rawImage.texture is not Texture2D texture)
                {
                    continue;
                }

                Rect uv = rawImage.uvRect;
                bool nonDefaultUv = !Approximately(uv, new Rect(0f, 0f, 1f, 1f));
                AddUsage(
                    containerPath,
                    AssetDatabase.GetAssetPath(texture),
                    rawImage.rectTransform,
                    rawImage.canvas,
                    new Vector2(texture.width, texture.height),
                    "RawImage",
                    texture.name,
                    nonDefaultUv ? UITextureConfidence.Risky : UITextureConfidence.Safe,
                    nonDefaultUv
                        ? new List<UITextureUsageReason> { UITextureUsageReason.RawImageUv }
                        : new List<UITextureUsageReason>(),
                    nonDefaultUv ? "RawImage uses UV crop or repeat; sampling density is context-sensitive." : string.Empty,
                    0f,
                    dynamicLayoutDetector);
            }
        }

        private void AddUsage(
            string containerPath,
            string texturePath,
            RectTransform rectTransform,
            Canvas canvas,
            Vector2 importedSampleSize,
            string componentType,
            string spriteName,
            UITextureConfidence confidence,
            List<UITextureUsageReason> reasons,
            string warning,
            float preserveAspect,
            UITextureDynamicLayoutDetector dynamicLayoutDetector)
        {
            if (!TryGetOrCreateResult(texturePath, componentType == "Image" ? "Sprite" : "Texture2D", out UITextureOptimizationResult result))
            {
                return;
            }

            Canvas rootCanvas = canvas != null ? canvas.rootCanvas : null;
            float scaleFactor = rootCanvas != null ? Mathf.Max(0.01f, rootCanvas.scaleFactor) : 1f;
            Vector2 renderedPixels = UITextureSizeCalculator.MeasureRenderedPixels(rectTransform, rootCanvas, scaleFactor);

            if (preserveAspect > 0f)
            {
                renderedPixels = UITextureSizeCalculator.ApplyPreserveAspect(renderedPixels, preserveAspect);
            }

            if (rootCanvas == null)
            {
                confidence = MaxConfidence(confidence, UITextureConfidence.Estimated);
                reasons.Add(UITextureUsageReason.MissingCanvas);
                warning = AppendWarning(warning, "No Canvas context; measured with fallback scale 1.");
            }
            else if (rootCanvas.name.EndsWith(" (Synthetic Canvas)", StringComparison.Ordinal))
            {
                confidence = MaxConfidence(confidence, UITextureConfidence.Estimated);
                reasons.Add(UITextureUsageReason.SyntheticCanvas);
                warning = AppendWarning(warning, "Prefab has no Canvas; measured against a synthetic Canvas at the simulated resolution.");
            }
            else if (rootCanvas.renderMode == RenderMode.WorldSpace)
            {
                confidence = UITextureConfidence.Unsupported;
                reasons.Add(UITextureUsageReason.WorldSpaceCanvas);
                warning = AppendWarning(warning, "World Space Canvas requires camera and distance context.");
            }

            if (dynamicLayoutDetector.TryGetReasons(rectTransform, out IReadOnlyCollection<UITextureUsageReason> dynamicReasons))
            {
                confidence = MaxConfidence(confidence, UITextureConfidence.Risky);
                reasons.AddRange(dynamicReasons);
                warning = AppendWarning(
                    warning,
                    "RectTransform size, anchors, or scale can be modified by Animation, Animator, or DOTween.");
            }

            int requiredMax = UITextureSizeCalculator.CalculateRequiredMaxSize(
                renderedPixels,
                importedSampleSize,
                new Vector2Int(result.SourceWidth, result.SourceHeight),
                result.CurrentMaxSize,
                _roundingMode);

            result.AddUsage(new UITextureUsageRecord
            {
                TexturePath = texturePath,
                AssetKind = componentType == "Image" ? "Sprite" : "Texture2D",
                ContainerPath = containerPath,
                HierarchyPath = GetHierarchyPath(rectTransform),
                ComponentType = componentType,
                SpriteName = spriteName,
                RenderedPixels = renderedPixels,
                RequiredMaxSize = requiredMax,
                Confidence = confidence,
                Reasons = reasons.Distinct().ToList(),
                Warning = warning
            });
        }

        private void RestoreCachedUsages(IEnumerable<UITextureUsageRecord> usages)
        {
            foreach (UITextureUsageRecord usage in usages)
            {
                if (usage == null ||
                    !TryGetOrCreateResult(usage.TexturePath, usage.AssetKind, out UITextureOptimizationResult result))
                {
                    continue;
                }

                result.AddUsage(usage);
            }
        }

        private static UITextureConfidence MaxConfidence(
            UITextureConfidence left,
            UITextureConfidence right)
        {
            return (UITextureConfidence)Mathf.Max((int)left, (int)right);
        }

        private bool TryGetOrCreateResult(string texturePath, string assetKind, out UITextureOptimizationResult result)
        {
            result = null;
            if (string.IsNullOrEmpty(texturePath) ||
                texturePath.EndsWith(".spriteatlas", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (_results.TryGetValue(texturePath, out result))
            {
                return true;
            }

            TextureImporter importer = AssetImporter.GetAtPath(texturePath) as TextureImporter;
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            if (importer == null || texture == null)
            {
                return false;
            }

            importer.GetSourceTextureWidthAndHeight(out int sourceWidth, out int sourceHeight);
            result = new UITextureOptimizationResult
            {
                AssetPath = texturePath,
                AssetName = Path.GetFileName(texturePath),
                AssetKind = assetKind,
                SourceWidth = sourceWidth,
                SourceHeight = sourceHeight,
                ImportedWidth = texture.width,
                ImportedHeight = texture.height,
                CurrentMaxSize = importer.maxTextureSize,
                RecommendedMaxSize = 0,
                IsEditable = texturePath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) &&
                             AssetDatabase.IsOpenForEdit(texturePath, StatusQueryOptions.UseCachedIfPossible),
                CurrentMemoryBytes = UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(texture)
            };
            _results.Add(texturePath, result);
            return true;
        }

        private static bool Approximately(Rect left, Rect right)
        {
            return Mathf.Approximately(left.x, right.x) &&
                   Mathf.Approximately(left.y, right.y) &&
                   Mathf.Approximately(left.width, right.width) &&
                   Mathf.Approximately(left.height, right.height);
        }

        private static string AppendWarning(string current, string extra)
        {
            return string.IsNullOrEmpty(current) ? extra : current + " " + extra;
        }

        private static string GetHierarchyPath(Component component)
        {
            List<string> parts = new();
            Transform current = component.transform;
            while (current != null)
            {
                parts.Add(current.name);
                current = current.parent;
            }

            parts.Reverse();
            return string.Join("/", parts);
        }

        private void RestoreActiveScene()
        {
            if (_originalActiveScene.IsValid() && _originalActiveScene.isLoaded)
            {
                try
                {
                    EditorSceneManager.SetActiveScene(_originalActiveScene);
                }
                catch
                {
                    // The original scene may have been closed by the user during the scan.
                }
            }
        }
    }
}
