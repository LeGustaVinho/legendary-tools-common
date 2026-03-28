using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace LegendaryTools.Editor
{
    public static class PrefabIconGenerator
    {
        private const string GenerateMenuPath = "Assets/Generate Thumbnail";

        private const int ThumbnailWidth = 512;
        private const int ThumbnailHeight = 512;

        private const float UiPlaneDistance = 10.0f;
        private const float UiFillPercent = 0.92f;
        private const float NonUiPadding = 1.08f;
        private const float MaxParticleWarmUpSeconds = 5.0f;

        public static ExecutionMode CurrentExecutionMode = ExecutionMode.TemporaryScene;

        public enum ExecutionMode
        {
            Scene = 0,
            TemporaryScene = 1
        }

        private struct BatchGenerationStats
        {
            public int generatedCount;
            public int skippedCount;
            public int failedCount;
        }

        [MenuItem(GenerateMenuPath, false, 2000)]
        private static void GenerateThumbnailFromProjectView()
        {
            GameObject[] prefabAssets = GetSelectedPrefabAssets();
            if (prefabAssets.Length == 0)
            {
                Debug.LogWarning("No prefab asset is selected.");
                return;
            }

            try
            {
                BatchGenerationStats stats = GenerateThumbnails(
                    prefabAssets,
                    CurrentExecutionMode,
                    true,
                    false);

                Debug.Log(
                    $"Prefab thumbnail generation finished. Generated: {stats.generatedCount}, skipped: {stats.skippedCount}, failed: {stats.failedCount}.");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                EditorApplication.RepaintProjectWindow();
            }
        }

        [MenuItem(GenerateMenuPath, true)]
        private static bool ValidateGenerateThumbnailFromProjectView()
        {
            GameObject[] selectedAssets = Selection.GetFiltered<GameObject>(SelectionMode.Assets);
            for (int i = 0; i < selectedAssets.Length; i++)
            {
                if (IsPrefabAsset(selectedAssets[i])) return true;
            }

            return false;
        }

        public static bool GenerateThumbnail(GameObject prefabAsset, bool forceRegenerate = false)
        {
            using (BatchSession batch = new(CurrentExecutionMode))
            {
                return batch.Generate(prefabAsset, forceRegenerate, out _);
            }
        }

        internal static bool GenerateThumbnail(
            GameObject prefabAsset,
            ExecutionMode mode,
            bool forceRegenerate,
            out bool skipped)
        {
            using (BatchSession batch = new(mode))
            {
                return batch.Generate(prefabAsset, forceRegenerate, out skipped);
            }
        }

        public static void GenerateThumbnails(IReadOnlyList<GameObject> prefabAssets, bool forceRegenerate = false)
        {
            GenerateThumbnails(prefabAssets, CurrentExecutionMode, false, forceRegenerate);
        }

        private static BatchGenerationStats GenerateThumbnails(
            IReadOnlyList<GameObject> prefabAssets,
            ExecutionMode mode,
            bool showProgressBar,
            bool forceRegenerate)
        {
            BatchGenerationStats stats = default;
            if (prefabAssets == null || prefabAssets.Count == 0) return stats;

            using (BatchSession batch = new(mode))
            {
                for (int i = 0; i < prefabAssets.Count; i++)
                {
                    GameObject prefabAsset = prefabAssets[i];
                    if (prefabAsset == null)
                    {
                        stats.failedCount++;
                        continue;
                    }

                    if (showProgressBar)
                    {
                        EditorUtility.DisplayProgressBar(
                            "Generate Thumbnail",
                            $"Generating thumbnail for {prefabAsset.name} ({i + 1}/{prefabAssets.Count})",
                            (float)(i + 1) / prefabAssets.Count);
                    }

                    bool skipped;
                    bool succeeded = batch.Generate(prefabAsset, forceRegenerate, out skipped);
                    if (!succeeded)
                    {
                        stats.failedCount++;
                        continue;
                    }

                    if (skipped)
                        stats.skippedCount++;
                    else
                        stats.generatedCount++;
                }
            }

            return stats;
        }

        internal sealed class BatchSession : IDisposable
        {
            private readonly bool _closeWorkingSceneOnDispose;
            private readonly ExecutionMode _mode;
            private readonly Scene _workingScene;
            private readonly List<Object> _temporaryObjects = new(8);

            private Camera _captureCamera;
            private RenderTexture _renderTexture;
            private Texture2D _readbackTexture;
            private GameObject _lightRig;

            internal BatchSession(ExecutionMode mode)
            {
                _mode = mode;
                _workingScene = GetWorkingScene(mode);
                _closeWorkingSceneOnDispose = mode == ExecutionMode.TemporaryScene;
                EnsureCaptureResources();
            }

            public bool Generate(GameObject prefabAsset, bool forceRegenerate, out bool skipped)
            {
                skipped = false;
                _temporaryObjects.Clear();
                bool isUiPrefab = false;

                try
                {
                    string assetGuid;
                    string prefabHash;
                    if (!TryGetPrefabInfo(prefabAsset, out assetGuid, out prefabHash)) return false;

                    if (!forceRegenerate && PrefabThumbnailCache.IsUpToDate(assetGuid, prefabHash))
                    {
                        skipped = true;
                        return true;
                    }

                    isUiPrefab = IsUiPrefabAsset(prefabAsset);
                    PrepareForGeneration(isUiPrefab);

                    GameObject instanceRoot = InstantiatePrefabInScene(prefabAsset, _workingScene);
                    if (instanceRoot == null)
                        throw new InvalidOperationException($"Failed to instantiate prefab '{prefabAsset.name}'.");

                    RegisterTemporaryObject(instanceRoot);

                    ConfigureCaptureCamera(_captureCamera, isUiPrefab);
                    SetLightRigActive(!isUiPrefab);

                    if (isUiPrefab)
                        PrepareUiCapture(instanceRoot, _captureCamera, _workingScene, RegisterTemporaryObject);
                    else
                    {
                        PreWarmParticleSystems(instanceRoot);
                        PrepareNonUiCapture(instanceRoot, _captureCamera, _workingScene, RegisterTemporaryObject);
                    }

                    SaveCameraRenderToPng(
                        _captureCamera,
                        _renderTexture,
                        _readbackTexture,
                        PrefabThumbnailCache.GetThumbnailPath(assetGuid),
                        isUiPrefab);

                    PrefabThumbnailCache.SetThumbnail(assetGuid, prefabHash);
                    return true;
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                    return false;
                }
                finally
                {
                    CleanupTemporaryObjects();
                    FinishGeneration(isUiPrefab);
                }
            }

            public void Dispose()
            {
                CleanupTemporaryObjects();
                DestroyCaptureResources();

                if (_lightRig != null) Object.DestroyImmediate(_lightRig);

                if (_closeWorkingSceneOnDispose && _workingScene.IsValid())
                    EditorSceneManager.CloseScene(_workingScene, true);
            }

            private void RegisterTemporaryObject(Object temporaryObject)
            {
                if (temporaryObject != null) _temporaryObjects.Add(temporaryObject);
            }

            private void EnsureCaptureResources()
            {
                if (_captureCamera == null) _captureCamera = CreateCaptureCamera(_workingScene, _mode);

                if (_renderTexture == null) _renderTexture = CreateRenderTexture();

                if (_readbackTexture == null) _readbackTexture = CreateReadbackTexture();

                _captureCamera.targetTexture = _renderTexture;
            }

            private void DestroyCaptureResources()
            {
                if (_captureCamera != null)
                {
                    _captureCamera.targetTexture = null;
                    Object.DestroyImmediate(_captureCamera.gameObject);
                    _captureCamera = null;
                }

                if (_renderTexture != null)
                {
                    _renderTexture.Release();
                    Object.DestroyImmediate(_renderTexture);
                    _renderTexture = null;
                }

                if (_readbackTexture != null)
                {
                    Object.DestroyImmediate(_readbackTexture);
                    _readbackTexture = null;
                }
            }

            private void ResetCaptureResources()
            {
                DestroyCaptureResources();

                if (_lightRig != null)
                {
                    Object.DestroyImmediate(_lightRig);
                    _lightRig = null;
                }

                EnsureCaptureResources();
                Canvas.ForceUpdateCanvases();
            }

            private void PrepareForGeneration(bool isUiPrefab)
            {
                EnsureCaptureResources();

                if (!isUiPrefab) return;

                // UI capture relies on global canvas rebuild state. Recreate the capture rig
                // so each UI render starts from the same state as an isolated BatchSession.
                ResetCaptureResources();
            }

            private void FinishGeneration(bool isUiPrefab)
            {
                if (isUiPrefab) Canvas.ForceUpdateCanvases();
            }

            private void CleanupTemporaryObjects()
            {
                for (int i = _temporaryObjects.Count - 1; i >= 0; i--)
                {
                    Object temporaryObject = _temporaryObjects[i];
                    if (temporaryObject != null) Object.DestroyImmediate(temporaryObject);
                }

                _temporaryObjects.Clear();
                Canvas.ForceUpdateCanvases();
            }

            private void SetLightRigActive(bool active)
            {
                if (active)
                    EnsureLightRig().SetActive(true);
                else if (_lightRig != null) _lightRig.SetActive(false);
            }

            private GameObject EnsureLightRig()
            {
                if (_lightRig == null) _lightRig = CreateDefaultLights(_workingScene);

                return _lightRig;
            }
        }

        private static void PrepareNonUiCapture(
            GameObject instanceRoot,
            Camera captureCamera,
            Scene scene,
            Action<Object> registerTemporaryObject)
        {
            GameObject frameRoot = CreateGameObjectInScene(scene, "__ThumbnailFrameRoot");
            registerTemporaryObject?.Invoke(frameRoot);

            frameRoot.transform.position = Vector3.zero;
            frameRoot.transform.rotation = Quaternion.identity;
            frameRoot.transform.localScale = Vector3.one;

            instanceRoot.transform.SetParent(frameRoot.transform, true);

            CenterHierarchyBoundsAtWorldOrigin(frameRoot.transform, instanceRoot);

            bool framed = CameraUtil.FrameGameObject(
                captureCamera,
                instanceRoot,
                NonUiPadding,
                45.0f,
                45.0f,
                default,
                true);

            if (!framed)
            {
                Debug.LogWarning(
                    $"Could not frame prefab '{instanceRoot.name}' because no renderer or collider bounds were found.");
            }
        }

        private static void PreWarmParticleSystems(GameObject root)
        {
            if (root == null) return;

            ParticleSystem[] particleSystems = root.GetComponentsInChildren<ParticleSystem>(true);
            if (particleSystems.Length == 0) return;

            for (int i = 0; i < particleSystems.Length; i++)
            {
                ParticleSystem particleSystem = particleSystems[i];
                if (particleSystem == null ||
                    HasParticleSystemAncestor(particleSystem.transform, root.transform)) continue;

                ParticleSystem.MainModule main = particleSystem.main;
                bool previousAutoRandomSeed = particleSystem.useAutoRandomSeed;
                uint previousRandomSeed = particleSystem.randomSeed;

                particleSystem.useAutoRandomSeed = false;
                particleSystem.randomSeed = (uint)(i + 1);

                particleSystem.Simulate(0.0f, true, true, true);

                float warmUpTime = GetParticleWarmUpTime(main);
                if (warmUpTime > 0.0f) particleSystem.Simulate(warmUpTime, true, true, true);

                particleSystem.Play(true);
                particleSystem.Simulate(0.0f, true, false, true);
                particleSystem.Pause(true);

                particleSystem.useAutoRandomSeed = previousAutoRandomSeed;
                particleSystem.randomSeed = previousRandomSeed;
            }
        }

        private static bool HasParticleSystemAncestor(Transform transform, Transform root)
        {
            if (transform == null || root == null) return false;

            Transform current = transform.parent;
            while (current != null)
            {
                if (current.GetComponent<ParticleSystem>() != null) return true;

                if (current == root) break;

                current = current.parent;
            }

            return false;
        }

        private static float GetParticleWarmUpTime(ParticleSystem.MainModule main)
        {
            float duration = Mathf.Max(0.0f, main.duration);
            float lifetime = GetParticleMax(main.startLifetime);

            if (main.loop && main.prewarm) return Mathf.Min(duration, MaxParticleWarmUpSeconds);

            return Mathf.Min(duration + lifetime, MaxParticleWarmUpSeconds);
        }

        private static float GetParticleMax(ParticleSystem.MinMaxCurve curve)
        {
            switch (curve.mode)
            {
                case ParticleSystemCurveMode.Constant:
                    return Mathf.Max(0.0f, curve.constant);
                case ParticleSystemCurveMode.TwoConstants:
                    return Mathf.Max(0.0f, curve.constantMax);
                case ParticleSystemCurveMode.Curve:
                    return Mathf.Max(0.0f, GetLastCurveKeyValue(curve.curveMax)) *
                           curve.curveMultiplier;
                case ParticleSystemCurveMode.TwoCurves:
                    float maxA = GetLastCurveKeyValue(curve.curveMax);
                    float maxB = GetLastCurveKeyValue(curve.curveMin);
                    return Mathf.Max(0.0f, Mathf.Max(maxA, maxB) * curve.curveMultiplier);
                default:
                    return 0.0f;
            }
        }

        private static float GetLastCurveKeyValue(AnimationCurve curve)
        {
            if (curve == null) return 0.0f;

            Keyframe[] keys = curve.keys;
            return keys != null && keys.Length > 0 ? keys[keys.Length - 1].value : 0.0f;
        }

        private static void PrepareUiCapture(
            GameObject instanceRoot,
            Camera captureCamera,
            Scene scene,
            Action<Object> registerTemporaryObject)
        {
            RectTransform originalRoot = instanceRoot.transform as RectTransform;
            if (originalRoot == null)
            {
                throw new InvalidOperationException(
                    "The prefab was identified as UI, but the root is not a RectTransform.");
            }

            RectTransform boundsTarget;
            RectTransform captureCanvasRect;

            Canvas rootCanvas = instanceRoot.GetComponent<Canvas>();
            if (rootCanvas == null)
            {
                Canvas captureCanvas = CreateCaptureCanvas(scene, captureCamera);
                registerTemporaryObject?.Invoke(captureCanvas.gameObject);
                captureCanvasRect = captureCanvas.transform as RectTransform;

                RectTransform wrapper = CreateUiContentWrapper(captureCanvasRect);
                originalRoot.SetParent(wrapper, false);

                ConfigureAllCanvasesForCamera(instanceRoot, captureCamera);
                WarmUpUiCanvas(captureCamera);

                boundsTarget = originalRoot;
                FitUiTargetIntoCanvas(captureCanvasRect, wrapper, boundsTarget);
            }
            else
            {
                ConfigureCanvasForCamera(rootCanvas, captureCamera);

                CanvasScaler scaler = rootCanvas.GetComponent<CanvasScaler>();
                if (scaler != null) ConfigureCanvasScaler(scaler);

                captureCanvasRect = rootCanvas.transform as RectTransform;
                if (captureCanvasRect == null)
                    throw new InvalidOperationException("Canvas root does not have a RectTransform.");

                RectTransform wrapper = CreateUiContentWrapper(captureCanvasRect);

                List<Transform> childrenToMove = new();
                for (int i = 0; i < captureCanvasRect.childCount; i++)
                {
                    Transform child = captureCanvasRect.GetChild(i);
                    if (child != wrapper) childrenToMove.Add(child);
                }

                for (int i = 0; i < childrenToMove.Count; i++)
                {
                    childrenToMove[i].SetParent(wrapper, false);
                }

                ConfigureAllCanvasesForCamera(instanceRoot, captureCamera);
                WarmUpUiCanvas(captureCamera);

                boundsTarget = wrapper;
                FitUiTargetIntoCanvas(captureCanvasRect, wrapper, boundsTarget);
            }

            ForceRebuildUi();
        }

        private static void FitUiTargetIntoCanvas(RectTransform canvasRect, RectTransform wrapper,
            RectTransform boundsTarget)
        {
            if (canvasRect == null || wrapper == null || boundsTarget == null) return;

            wrapper.anchorMin = Vector2.zero;
            wrapper.anchorMax = Vector2.one;
            wrapper.offsetMin = Vector2.zero;
            wrapper.offsetMax = Vector2.zero;
            wrapper.pivot = new Vector2(0.5f, 0.5f);
            wrapper.anchoredPosition = Vector2.zero;
            wrapper.localRotation = Quaternion.identity;
            wrapper.localScale = Vector3.one;

            for (int pass = 0; pass < 3; pass++)
            {
                ForceRebuildUi();

                Bounds bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(canvasRect, boundsTarget);
                if (bounds.size.sqrMagnitude < 0.000001f) return;

                Vector2 availableSize = canvasRect.rect.size * UiFillPercent;

                float scaleX = bounds.size.x > 0.0001f ? availableSize.x / bounds.size.x : 1.0f;
                float scaleY = bounds.size.y > 0.0001f ? availableSize.y / bounds.size.y : 1.0f;
                float uniformScale = Mathf.Min(scaleX, scaleY);

                if (!float.IsNaN(uniformScale) && !float.IsInfinity(uniformScale) && uniformScale > 0.0f)
                    wrapper.localScale *= uniformScale;

                ForceRebuildUi();

                bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(canvasRect, boundsTarget);
                wrapper.anchoredPosition -= (Vector2)bounds.center;
            }
        }

        private static void ForceRebuildUi()
        {
            Canvas.ForceUpdateCanvases();
        }

        private static void WarmUpUiCanvas(Camera captureCamera)
        {
            if (captureCamera == null || captureCamera.targetTexture == null) return;

            ForceRebuildUi();
            captureCamera.Render();
            ForceRebuildUi();
        }

        private static Canvas CreateCaptureCanvas(Scene scene, Camera captureCamera)
        {
            GameObject canvasObject = CreateGameObjectInScene(
                scene,
                "__ThumbnailCanvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));

            RectTransform canvasRect = canvasObject.GetComponent<RectTransform>();
            canvasRect.anchorMin = Vector2.zero;
            canvasRect.anchorMax = Vector2.one;
            canvasRect.offsetMin = Vector2.zero;
            canvasRect.offsetMax = Vector2.zero;
            canvasRect.pivot = new Vector2(0.5f, 0.5f);
            canvasRect.localPosition = Vector3.zero;
            canvasRect.localRotation = Quaternion.identity;
            canvasRect.localScale = Vector3.one;

            Canvas canvas = canvasObject.GetComponent<Canvas>();
            ConfigureCanvasForCamera(canvas, captureCamera);

            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            ConfigureCanvasScaler(scaler);

            return canvas;
        }

        private static void ConfigureCanvasScaler(CanvasScaler scaler)
        {
            if (scaler == null) return;

            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(ThumbnailWidth, ThumbnailHeight);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            scaler.referencePixelsPerUnit = 100.0f;
        }

        private static RectTransform CreateUiContentWrapper(RectTransform parent)
        {
            GameObject wrapperObject = new("__ThumbnailUiWrapper", typeof(RectTransform));
            RectTransform wrapper = wrapperObject.GetComponent<RectTransform>();

            wrapper.SetParent(parent, false);
            wrapper.anchorMin = Vector2.zero;
            wrapper.anchorMax = Vector2.one;
            wrapper.offsetMin = Vector2.zero;
            wrapper.offsetMax = Vector2.zero;
            wrapper.pivot = new Vector2(0.5f, 0.5f);
            wrapper.anchoredPosition = Vector2.zero;
            wrapper.localRotation = Quaternion.identity;
            wrapper.localScale = Vector3.one;

            return wrapper;
        }

        private static void ConfigureAllCanvasesForCamera(GameObject root, Camera captureCamera)
        {
            Canvas[] canvases = root.GetComponentsInChildren<Canvas>(true);
            for (int i = 0; i < canvases.Length; i++)
            {
                ConfigureCanvasForCamera(canvases[i], captureCamera);
            }
        }

        private static void ConfigureCanvasForCamera(Canvas canvas, Camera captureCamera)
        {
            if (canvas == null) return;

            if (canvas.renderMode == RenderMode.ScreenSpaceOverlay) canvas.renderMode = RenderMode.ScreenSpaceCamera;

            if (canvas.renderMode == RenderMode.WorldSpace) canvas.renderMode = RenderMode.ScreenSpaceCamera;

            canvas.worldCamera = captureCamera;
            canvas.planeDistance = UiPlaneDistance;
            canvas.sortingOrder = 0;
            canvas.overrideSorting = false;
            canvas.updateRectTransformForStandalone = StandaloneRenderResize.Enabled;
        }

        private static Camera CreateCaptureCamera(Scene scene, ExecutionMode mode)
        {
            GameObject cameraObject = CreateGameObjectInScene(scene, "__ThumbnailCamera", typeof(Camera));
            Camera camera = cameraObject.GetComponent<Camera>();

            EditorUtility.SetCameraAnimateMaterials(camera, true);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.0f, 0.0f, 0.0f, 0.0f);
            camera.allowHDR = false;
            camera.allowMSAA = true;
            camera.useOcclusionCulling = false;
            camera.renderingPath = RenderingPath.UsePlayerSettings;
            camera.cullingMask = ~0;
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = 1000.0f;
            camera.aspect = (float)ThumbnailWidth / ThumbnailHeight;
            camera.rect = new Rect(0.0f, 0.0f, 1.0f, 1.0f);
            camera.enabled = false;
            camera.cameraType = CameraType.Game;
            camera.scene = scene;
            camera.overrideSceneCullingMask = scene.IsValid()
                ? EditorSceneManager.GetSceneCullingMask(scene)
                : 0UL;

            ConfigureCaptureCamera(camera, false);
            return camera;
        }

        private static void ConfigureCaptureCamera(Camera camera, bool isUi)
        {
            if (isUi)
            {
                camera.orthographic = true;
                camera.orthographicSize = ThumbnailHeight * 0.5f;
                camera.transform.SetPositionAndRotation(
                    new Vector3(0.0f, 0.0f, -UiPlaneDistance),
                    Quaternion.identity);
            }
            else
            {
                camera.orthographic = false;
                camera.fieldOfView = 60.0f;
                camera.transform.SetPositionAndRotation(Vector3.back * 5.0f, Quaternion.identity);
            }
        }

        private static RenderTexture CreateRenderTexture()
        {
            RenderTexture renderTexture =
                new(ThumbnailWidth, ThumbnailHeight, 24, RenderTextureFormat.ARGB32)
                {
                    name = "__PrefabThumbnailRT",
                    antiAliasing = 1,
                    useMipMap = false,
                    autoGenerateMips = false
                };

            renderTexture.Create();
            return renderTexture;
        }

        private static Texture2D CreateReadbackTexture()
        {
            return new Texture2D(ThumbnailWidth, ThumbnailHeight, TextureFormat.RGBA32, false, false)
            {
                name = "__PrefabThumbnailReadback",
                hideFlags = HideFlags.HideAndDontSave
            };
        }

        private static GameObject CreateDefaultLights(Scene scene)
        {
            GameObject lightRig = CreateGameObjectInScene(scene, "__ThumbnailLightRig");

            GameObject keyLightObject = CreateGameObjectInScene(scene, "__ThumbnailKeyLight", typeof(Light));
            keyLightObject.transform.SetParent(lightRig.transform, false);

            Light keyLight = keyLightObject.GetComponent<Light>();
            keyLight.type = LightType.Directional;
            keyLight.intensity = 1.2f;
            keyLight.shadows = LightShadows.Soft;
            keyLight.transform.rotation = Quaternion.Euler(35.0f, -30.0f, 0.0f);

            GameObject fillLightObject = CreateGameObjectInScene(scene, "__ThumbnailFillLight", typeof(Light));
            fillLightObject.transform.SetParent(lightRig.transform, false);

            Light fillLight = fillLightObject.GetComponent<Light>();
            fillLight.type = LightType.Directional;
            fillLight.intensity = 0.6f;
            fillLight.shadows = LightShadows.None;
            fillLight.transform.rotation = Quaternion.Euler(340.0f, 210.0f, 0.0f);

            return lightRig;
        }

        private static void CenterHierarchyBoundsAtWorldOrigin(Transform frameRoot, GameObject target)
        {
            if (frameRoot == null || target == null) return;

            if (!TryGetHierarchyBounds(target, out Bounds bounds)) return;

            frameRoot.position -= bounds.center;
        }

        private static bool TryGetHierarchyBounds(GameObject target, out Bounds bounds)
        {
            bounds = new Bounds(target.transform.position, Vector3.zero);
            bool hasBounds = false;

            Renderer[] renderers = target.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (!hasBounds)
                {
                    bounds = renderers[i].bounds;
                    hasBounds = true;
                }
                else
                    bounds.Encapsulate(renderers[i].bounds);
            }

            if (hasBounds) return true;

            Collider[] colliders = target.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                if (!hasBounds)
                {
                    bounds = colliders[i].bounds;
                    hasBounds = true;
                }
                else
                    bounds.Encapsulate(colliders[i].bounds);
            }

            return hasBounds;
        }

        private static void SaveCameraRenderToPng(
            Camera camera,
            RenderTexture renderTexture,
            Texture2D readbackTexture,
            string outputPath,
            bool isUi)
        {
            RenderTexture previousActive = RenderTexture.active;

            try
            {
                camera.targetTexture = renderTexture;
                RenderTexture.active = renderTexture;
                GL.Clear(true, true, Color.clear);

                if (isUi)
                    RenderUiCameraWithWarmUp(camera);
                else
                    camera.Render();

                RenderTexture.active = renderTexture;

                readbackTexture.ReadPixels(new Rect(0.0f, 0.0f, renderTexture.width, renderTexture.height), 0, 0);
                readbackTexture.Apply(false, false);

                string outputDirectory = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(outputDirectory) && !Directory.Exists(outputDirectory))
                    Directory.CreateDirectory(outputDirectory);

                byte[] pngBytes = readbackTexture.EncodeToPNG();
                File.WriteAllBytes(outputPath, pngBytes);
            }
            finally
            {
                RenderTexture.active = previousActive;
            }
        }

        private static void RenderUiCameraWithWarmUp(Camera camera)
        {
            ForceRebuildUi();
            camera.Render();

            ForceRebuildUi();
            camera.Render();
        }

        private static Scene GetWorkingScene(ExecutionMode mode)
        {
            if (mode == ExecutionMode.TemporaryScene)
                return EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);

            Scene activeScene = SceneManager.GetActiveScene();
            if (!activeScene.IsValid()) throw new InvalidOperationException("There is no valid active scene.");

            return activeScene;
        }

        private static GameObject InstantiatePrefabInScene(GameObject prefabAsset, Scene scene)
        {
            return PrefabUtility.InstantiatePrefab(prefabAsset, scene) as GameObject;
        }

        private static GameObject[] GetSelectedPrefabAssets()
        {
            GameObject[] selectedAssets = Selection.GetFiltered<GameObject>(SelectionMode.Assets);
            List<GameObject> prefabAssets = new();

            for (int i = 0; i < selectedAssets.Length; i++)
            {
                if (IsPrefabAsset(selectedAssets[i])) prefabAssets.Add(selectedAssets[i]);
            }

            return prefabAssets.ToArray();
        }

        private static bool IsPrefabAsset(GameObject gameObject)
        {
            if (gameObject == null) return false;

            return PrefabUtility.GetPrefabAssetType(gameObject) != PrefabAssetType.NotAPrefab;
        }

        internal static bool IsUiPrefabAsset(GameObject gameObject)
        {
            return gameObject != null && gameObject.transform is RectTransform;
        }

        private static GameObject CreateGameObjectInScene(Scene scene, string name, params Type[] components)
        {
            GameObject gameObject = new(name, components);

            if (scene.IsValid()) SceneManager.MoveGameObjectToScene(gameObject, scene);

            return gameObject;
        }

        private static bool TryGetPrefabInfo(GameObject prefabAsset, out string assetGuid, out string prefabHash)
        {
            if (!IsPrefabAsset(prefabAsset))
            {
                assetGuid = string.Empty;
                prefabHash = string.Empty;
                return false;
            }

            string assetPath = AssetDatabase.GetAssetPath(prefabAsset);
            return TryGetPrefabInfo(assetPath, out assetGuid, out prefabHash);
        }

        internal static bool TryGetPrefabInfo(string assetPath, out string assetGuid, out string prefabHash)
        {
            assetGuid = string.Empty;
            prefabHash = string.Empty;

            if (string.IsNullOrEmpty(assetPath)) return false;

            assetGuid = AssetDatabase.AssetPathToGUID(assetPath);
            if (string.IsNullOrEmpty(assetGuid)) return false;

            Hash128 dependencyHash = AssetDatabase.GetAssetDependencyHash(assetPath);
            if (!dependencyHash.isValid) return false;

            prefabHash = dependencyHash.ToString();
            return !string.IsNullOrEmpty(prefabHash);
        }
    }
}