using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;

namespace LegendaryTools.Editor
{
    internal sealed class SceneScanner
    {
        private readonly bool _includeInactive;
        private readonly StaticFilter _staticFilter;
        private readonly bool _includeMesh, _includeSkinned, _includePS;

        // Shadows heuristics
        private readonly float _shadowSmallSizeThreshold;
        private readonly float _shadowFarDistanceThreshold;

        // Camera info (also used for transparency overdraw)
        private readonly bool _hasCamera;
        private readonly Vector3 _cameraPos;

        // transparency overdraw heuristic
        private readonly float _transpSizeOverDistThreshold;

        public SceneScanner(
            bool includeInactive,
            StaticFilter staticFilter,
            bool includeMeshRenderer,
            bool includeSkinnedMeshRenderer,
            bool includeParticleSystem,
            float shadowSmallSizeThreshold,
            float shadowFarDistanceThreshold,
            bool hasCamera,
            Vector3 cameraPos,
            float transpSizeOverDistThreshold)
        {
            _includeInactive = includeInactive;
            _staticFilter = staticFilter;
            _includeMesh = includeMeshRenderer;
            _includeSkinned = includeSkinnedMeshRenderer;
            _includePS = includeParticleSystem;

            _shadowSmallSizeThreshold = Mathf.Max(0.0001f, shadowSmallSizeThreshold);
            _shadowFarDistanceThreshold = Mathf.Max(0.0f, shadowFarDistanceThreshold);
            _hasCamera = hasCamera;
            _cameraPos = cameraPos;

            _transpSizeOverDistThreshold = Mathf.Max(0.0f, transpSizeOverDistThreshold);
        }

        public (List<RendererEntry> entries, ScanSummary summary) Scan()
        {
            List<RendererEntry> entries = new(256);
            ScanSummary sum = new();

            int sceneCount = SceneManager.sceneCount;
            for (int si = 0; si < sceneCount; si++)
            {
                Scene scene = SceneManager.GetSceneAt(si);
                if (!scene.isLoaded) continue;

                GameObject[] roots = scene.GetRootGameObjects();
                foreach (GameObject root in roots)
                {
                    Renderer[] renderers = _includeInactive
                        ? root.GetComponentsInChildren<Renderer>(true)
                        : root.GetComponentsInChildren<Renderer>(false);

                    foreach (Renderer r in renderers)
                    {
                        if (r == null) continue;

                        bool isSkinned = r is SkinnedMeshRenderer;
                        bool isPSR = r is ParticleSystemRenderer;
                        bool isMesh = !isSkinned && !isPSR;

                        if (isMesh && !_includeMesh) continue;
                        if (isSkinned && !_includeSkinned) continue;
                        if (isPSR && !_includePS) continue;

                        bool isStatic = r.gameObject.isStatic;
                        if (_staticFilter == StaticFilter.OnlyStatic && !isStatic) continue;
                        if (_staticFilter == StaticFilter.OnlyNonStatic && isStatic) continue;

                        sum.RendererTotal++;
                        if (isSkinned) sum.SkinnedRendererCount++;
                        else if (isPSR) sum.ParticleSystemRendererCount++;
                        else sum.MeshRendererCount++;

                        Material[] sharedMats = r.sharedMaterials ?? Array.Empty<Material>();
                        Material[] uniqueMats = sharedMats.Where(m => m != null).Distinct().ToArray();
                        Shader[] shaders = uniqueMats.Select(m => m.shader).Where(s => s != null).Distinct().ToArray();
                        Texture[] textures = TextureUtil.CollectAllTextures(uniqueMats);

                        foreach (Material m in uniqueMats)
                        {
                            sum.UniqueMaterials.Add(m);
                        }

                        foreach (Shader s in shaders)
                        {
                            sum.UniqueShaders.Add(s);
                        }

                        foreach (Texture t in textures)
                        {
                            sum.UniqueTextures.Add(t);
                        }

                        // Instancing
                        InstancingUtil.ComputeInstancingForRenderer(uniqueMats,
                            out bool instEligible, out bool instEnabled, out bool instSupportedButDisabled);

                        if (instEligible) sum.InstancingEligible++;
                        if (instEnabled) sum.InstancingEnabled++;
                        else if (instSupportedButDisabled) sum.InstancingSupportedButDisabled++;
                        else sum.InstancingUnsupportedOrUnknown++;

                        BatchingUtil.Evaluate(
                            r, uniqueMats,
                            out bool staticBatchEligible, out List<string> staticBatchReasons,
                            out bool dynamicBatchEligible, out List<string> dynamicBatchReasons);
                        // Shadows
                        bool casts = r.shadowCastingMode != ShadowCastingMode.Off;
                        bool receives = r.receiveShadows;
                        bool receiveProbablyUnnecessary = false;
                        if (receives)
                        {
                            Bounds b = r.bounds;
                            // Heuristic de tamanho
                            bool tooSmall = b.size.magnitude <= _shadowSmallSizeThreshold;
                            // Heuristic de distância (se tivermos câmera)
                            bool tooFar = false;
                            if (_hasCamera)
                                tooFar = Vector3.Distance(b.center, _cameraPos) >= _shadowFarDistanceThreshold;

                            receiveProbablyUnnecessary = tooSmall || tooFar;
                        }

                        if (casts) sum.CastShadowCount++;
                        if (receives) sum.ReceiveShadowCount++;
                        if (receiveProbablyUnnecessary) sum.ReceiveProbablyUnnecessaryCount++;

                        // Transparency detection
                        int maxQueue = -1;
                        int transpCount = 0;
                        bool anyTransparent = false;

                        foreach (Material m in uniqueMats)
                        {
                            if (m == null) continue;
                            int rq = m.renderQueue;
                            if (rq > maxQueue) maxQueue = rq;

                            // detect by queue or RenderType tag
                            bool tagTransparent = false;
                            try
                            {
                                string rt = m.GetTag("RenderType", false);
                                tagTransparent = string.Equals(rt, "Transparent", StringComparison.OrdinalIgnoreCase);
                            }
                            catch
                            {
                                /* ignore */
                            }

                            bool isTransparent = rq >= 3000 || tagTransparent;
                            if (isTransparent)
                            {
                                anyTransparent = true;
                                transpCount++;
                            }
                        }

                        bool highQueue = anyTransparent && maxQueue >= 3000;

                        // Overdraw risk heuristic (size/distance ratio)
                        bool overdrawRisk = false;
                        if (anyTransparent && _hasCamera)
                        {
                            Bounds b = r.bounds;
                            float dist = Mathf.Max(0.001f, Vector3.Distance(b.center, _cameraPos));
                            float ratio = b.size.magnitude / dist; // rough "screen impact"
                            overdrawRisk = ratio >= _transpSizeOverDistThreshold;
                        }

                        if (anyTransparent) sum.TransparentRendererCount++;
                        if (highQueue) sum.HighQueueRendererCount++;
                        if (overdrawRisk) sum.OverdrawRiskRendererCount++;

                        ParticleSystem ps = isPSR ? r.GetComponent<ParticleSystem>() : null;

                        entries.Add(new RendererEntry
                        {
                            Renderer = r,
                            SceneName = scene.name,
                            GameObjectPath = GetGameObjectPath(r.gameObject),
                            IsStatic = isStatic,
                            IsSkinned = isSkinned,
                            IsParticleSystem = isPSR,
                            Materials = uniqueMats,
                            Shaders = shaders,
                            Textures = textures,
                            ParticleSystem = ps,

                            InstancingEligible = instEligible,
                            InstancingEnabled = instEnabled,
                            InstancingSupportedButDisabled = instSupportedButDisabled,

                            StaticBatchEligible = staticBatchEligible,
                            StaticBatchReasons = staticBatchReasons,
                            DynamicBatchEligible = dynamicBatchEligible,
                            DynamicBatchReasons = dynamicBatchReasons,
                            CastsShadows = casts,
                            ReceivesShadows = receives,
                            ReceivesShadowProbablyUnnecessary = receiveProbablyUnnecessary,

                            HasTransparentMaterial = anyTransparent,
                            MaxRenderQueue = maxQueue,
                            TransparentMaterialCount = transpCount,
                            TransparencyOverdrawRisk = overdrawRisk
                        });
                    }
                }
            }

            return (entries, sum);
        }

        private static string GetGameObjectPath(GameObject go)
        {
            Stack<string> stack = new();
            Transform t = go.transform;
            while (t != null)
            {
                stack.Push(t.name);
                t = t.parent;
            }

            return string.Join("/", stack);
        }
    }
}