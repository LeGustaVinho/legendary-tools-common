// RenderingPerformanceHubWindow.cs
// Place under: Assets/Editor
// Unity 6 / URP
// Code in English; comments follow Microsoft style.

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;
#if USING_URP || UNITY_RENDER_PIPELINE_UNIVERSAL
using UnityEngine.Rendering.Universal;
#endif

public sealed class RenderingPerformanceHubWindow : EditorWindow
{
    // Foldout keys
    private const string kFold_Overview = "RPH_Overview";
    private const string kFold_Quality = "RPH_Quality";
    private const string kFold_Player = "RPH_Player";
    private const string kFold_Graphics = "RPH_Graphics";
    private const string kFold_Shaders = "RPH_Shaders";
    private const string kFold_LightingBake = "RPH_LightingBake";
    private const string kFold_Post = "RPH_PostProcessing";
    private const string kFold_Android = "RPH_Android";
    private const string kFold_Vulkan = "RPH_Vulkan";
#if USING_URP || UNITY_RENDER_PIPELINE_UNIVERSAL
    private const string kFold_URP = "RPH_URP";
    private const string kFold_URPLights = "RPH_URP_Lights";
    private const string kFold_URPRendererData = "RPH_URP_RendererData";
#endif

    private Vector2 _scroll;

    private BuildTarget _activeTarget;
    private BuildTargetGroup _activeGroup;

#if USING_URP || UNITY_RENDER_PIPELINE_UNIVERSAL
    private UniversalRenderPipelineAsset _urpAsset;
#endif

    [MenuItem("Tools/Rendering/Rendering Performance Hub")]
    public static void Open()
    {
        RenderingPerformanceHubWindow wnd = GetWindow<RenderingPerformanceHubWindow>();
        wnd.titleContent = new GUIContent("Rendering Performance Hub");
        wnd.minSize = new Vector2(520, 620);
        wnd.Show();
    }

    private void OnEnable()
    {
        _activeTarget = EditorUserBuildSettings.activeBuildTarget;
        _activeGroup = BuildPipeline.GetBuildTargetGroup(_activeTarget);
#if USING_URP || UNITY_RENDER_PIPELINE_UNIVERSAL
        _urpAsset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
#endif
    }

    private void OnGUI()
    {
        _scroll = EditorGUILayout.BeginScrollView(_scroll);

        DrawOverview();
        EditorGUILayout.Space(6);
        DrawQualitySection();
        EditorGUILayout.Space(6);
        DrawPlayerSection();
        EditorGUILayout.Space(6);
        DrawGraphicsSection();
        EditorGUILayout.Space(6);
        DrawAlwaysIncludedShadersSection();
        EditorGUILayout.Space(6);
        DrawLightingBakeSection();
        EditorGUILayout.Space(6);
        DrawPostProcessingSection();
        EditorGUILayout.Space(6);
        DrawAndroidSection();
        EditorGUILayout.Space(6);
#if USING_URP || UNITY_RENDER_PIPELINE_UNIVERSAL
        DrawURPSection();
        EditorGUILayout.Space(6);
        DrawURPLightsAndShadowsSection();
        EditorGUILayout.Space(6);
        DrawURPRendererDataSection();
        EditorGUILayout.Space(6);
#endif
        EditorGUILayout.EndScrollView();

        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Refresh", GUILayout.Width(100))) OnEnable();
        }
    }

    // Utility to build GUIContent with tooltip.
    private static GUIContent GC(string label, string tip)
    {
        return new GUIContent(label, tip);
    }

    #region Overview

    private void DrawOverview()
    {
        bool fold = GetFold(kFold_Overview, true);
        fold = EditorGUILayout.BeginFoldoutHeaderGroup(fold, "Overview (Active Platform Only)");
        SetFold(kFold_Overview, fold);
        if (fold)
        {
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.EnumPopup(GC("Active Build Target", "Platform you are currently building for."),
                    _activeTarget);
                EditorGUILayout.EnumPopup(GC("Build Target Group", "Logical platform group (Android, iOS, etc.)."),
                    _activeGroup);
                EditorGUILayout.ObjectField(
                    GC("Default Render Pipeline", "Project-wide render pipeline asset currently in use."),
                    GraphicsSettings.currentRenderPipeline, typeof(RenderPipelineAsset), false);
            }

            EditorGUILayout.LabelField("Active Quality Level",
                QualitySettings.names[QualitySettings.GetQualityLevel()]);
            EditorGUILayout.LabelField("Color Space", PlayerSettings.colorSpace.ToString());

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Open Graphics Settings"))
                    SettingsService.OpenProjectSettings("Project/Graphics");
                if (GUILayout.Button("Open Quality Settings"))
                    SettingsService.OpenProjectSettings("Project/Quality");
                if (GUILayout.Button("Open Player Settings"))
                    SettingsService.OpenProjectSettings("Project/Player");
                if (GUILayout.Button("Open Lighting Window"))
                    EditorApplication.ExecuteMenuItem("Window/Rendering/Lighting");
            }
        }

        EditorGUILayout.EndFoldoutHeaderGroup();
    }

    #endregion

    #region Quality

    private void DrawQualitySection()
    {
        bool fold = GetFold(kFold_Quality, true);
        fold = EditorGUILayout.BeginFoldoutHeaderGroup(fold, "Quality (Active Level)");
        SetFold(kFold_Quality, fold);
        if (!fold)
        {
            EditorGUILayout.EndFoldoutHeaderGroup();
            return;
        }

        // Current quality preset.
        int q = QualitySettings.GetQualityLevel();
        int newQ = EditorGUILayout.Popup(GC("Current Quality", "Quality preset that is currently active."), q,
            QualitySettings.names);
        if (newQ != q) QualitySettings.SetQualityLevel(newQ, true);

        // Per-quality pipeline asset.
        RenderPipelineAsset qualityRpa = (RenderPipelineAsset)EditorGUILayout.ObjectField(
            GC("Render Pipeline Asset (Quality)",
                "Pipeline asset assigned to this quality level. Lighter: mobile/stripped asset. Costly: feature-rich asset."),
            QualitySettings.renderPipeline, typeof(RenderPipelineAsset), false);
        if (qualityRpa != QualitySettings.renderPipeline)
            QualitySettings.renderPipeline = qualityRpa;

        // Rendering (common knobs).
        QualitySettings.realtimeReflectionProbes = EditorGUILayout.Toggle(
            GC("Realtime Reflection Probes",
                "Updates reflection probes at runtime. Lighter: Off. Costly: On (extra rendering work)."),
            QualitySettings.realtimeReflectionProbes);

        QualitySettings.resolutionScalingFixedDPIFactor = EditorGUILayout.Slider(
            GC("Resolution Scaling Fixed DPI Factor",
                "Scales render resolution for fixed-DPI devices. Lighter: < 1.0. Costly: ≥ 1.0."),
            QualitySettings.resolutionScalingFixedDPIFactor, 0.1f, 2.0f);

        EnumFieldViaReflection(typeof(QualitySettings), "realtimeGICPUUsage",
            GC("Realtime GI CPU Usage", "CPU time budget for Realtime GI updates. Lighter: Low. Costly: High."));

        // VSync: IntPopup with GUIContent[] to support tooltip.
        GUIContent[] vsLabels = new[]
            { new GUIContent("Don't Sync"), new GUIContent("Every V Blank"), new GUIContent("Every Second V Blank") };
        int[] vsVals = new[] { 0, 1, 2 };
        QualitySettings.vSyncCount = EditorGUILayout.IntPopup(
            GC("VSync Count",
                "Synchronize frames to display refresh. Lighter: Don't Sync (tearing possible). Costly: Every V-Blank/Second V-Blank."),
            QualitySettings.vSyncCount, vsLabels, vsVals);

        // Texture & streaming.
        IntFieldViaReflection(typeof(QualitySettings), "globalTextureMipmapLimit",
            GC("Global Mipmap Limit",
                "Forces lower mip globally. Lighter: Higher limit (more downscale). Costly: 0 (full resolution)."),
            0, 6);

        QualitySettings.anisotropicFiltering = (AnisotropicFiltering)EditorGUILayout.EnumPopup(
            GC("Anisotropic Textures",
                "Improves texture sharpness at grazing angles. Lighter: Disabled/low. Costly: Forced/high."),
            QualitySettings.anisotropicFiltering);

        QualitySettings.streamingMipmapsActive = EditorGUILayout.Toggle(
            GC("Mipmap Streaming", "Streams mip levels based on size. Lighter: On with proper budgets. Costly: Off."),
            QualitySettings.streamingMipmapsActive);

        // Particles.
        QualitySettings.particleRaycastBudget = Mathf.Max(4, EditorGUILayout.IntField(
            GC("Particle Raycast Budget", "Max particle raycasts per frame. Lighter: Lower. Costly: Higher."),
            QualitySettings.particleRaycastBudget));

        // Shadows.
#if UNITY_2020_2_OR_NEWER
        QualitySettings.shadowmaskMode = (ShadowmaskMode)EditorGUILayout.EnumPopup(
            GC("Shadowmask Mode",
                "How mixed lighting handles static shadows. Lighter: Distance Shadowmask. Costly: Shadowmask."),
            QualitySettings.shadowmaskMode);
#endif

        // Async asset upload.
        EditorGUILayout.Space(2);
        EditorGUILayout.LabelField("Async Asset Upload", EditorStyles.boldLabel);
        QualitySettings.asyncUploadTimeSlice = Mathf.Max(1, EditorGUILayout.IntField(
            GC("Time Slice (ms)", "CPU time per frame for uploads. Lighter: Lower. Costly: Higher."),
            QualitySettings.asyncUploadTimeSlice));
        QualitySettings.asyncUploadBufferSize = Mathf.Clamp(EditorGUILayout.IntField(
            GC("Buffer Size (MB)", "Staging memory for uploads. Lighter: Lower MB. Costly: Higher MB."),
            QualitySettings.asyncUploadBufferSize), 1, 1024);
        QualitySettings.asyncUploadPersistentBuffer = EditorGUILayout.Toggle(
            GC("Persistent Buffer", "Keeps staging buffer allocated. Lighter: Off. Costly: On."),
            QualitySettings.asyncUploadPersistentBuffer);

        // LOD / Skin weights.
        QualitySettings.lodBias = EditorGUILayout.Slider(
            GC("LOD Bias", "Scales LOD selection. Lighter: Lower (uses lower-detail meshes sooner). Costly: Higher."),
            QualitySettings.lodBias, 0.1f, 5f);
        QualitySettings.maximumLODLevel = EditorGUILayout.IntField(
            GC("Maximum LOD Level", "Forces use of LODs ≥ value. Lighter: Higher number. Costly: 0."),
            QualitySettings.maximumLODLevel);
        QualitySettings.skinWeights = (SkinWeights)EditorGUILayout.EnumPopup(
            GC("Skin Weights", "Max bones per vertex. Lighter: 1–2 bones. Costly: 4+ bones."),
            QualitySettings.skinWeights);

        EditorGUILayout.HelpBox(
            "Terrain override sliders from the screenshot are not exposed via the public API. Adjust them in Terrain settings.",
            MessageType.Info);

        EditorGUILayout.EndFoldoutHeaderGroup();
    }

    #endregion

    #region Player (active platform only)

    private void DrawPlayerSection()
    {
        bool fold = GetFold(kFold_Player, true);
        fold = EditorGUILayout.BeginFoldoutHeaderGroup(fold, $"Player Settings ({_activeGroup})");
        SetFold(kFold_Player, fold);
        if (!fold)
        {
            EditorGUILayout.EndFoldoutHeaderGroup();
            return;
        }

        PlayerSettings.colorSpace = (ColorSpace)EditorGUILayout.EnumPopup(
            GC("Color Space", "Rendering color pipeline. Lighter: Gamma. Costly: Linear."),
            PlayerSettings.colorSpace);

        // Auto Graphics API — Unity 6 expects BuildTarget (not BuildTargetGroup).
        bool useDefault = PlayerSettings.GetUseDefaultGraphicsAPIs(_activeTarget);
        bool newUseDefault = EditorGUILayout.Toggle(
            GC("Auto Graphics API", "Let Unity choose APIs. Lighter: Enabled. Costly: Manual with older APIs."),
            useDefault);
        if (newUseDefault != useDefault)
            PlayerSettings.SetUseDefaultGraphicsAPIs(_activeTarget, newUseDefault);

        // Custom Graphics API order when Auto is disabled.
        if (!newUseDefault)
        {
            List<GraphicsDeviceType> apis = new(PlayerSettings.GetGraphicsAPIs(_activeTarget));
            using (new EditorGUI.IndentLevelScope())
            {
                for (int i = 0; i < apis.Count; i++)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        apis[i] = (GraphicsDeviceType)EditorGUILayout.EnumPopup(
                            GC($"API {i + 1}", "Graphics backend order. Prefer modern APIs (Vulkan/Metal)."),
                            apis[i]);
                        if (GUILayout.Button("Remove", GUILayout.Width(70)))
                        {
                            apis.RemoveAt(i);
                            i--;
                        }
                    }
                }

                if (GUILayout.Button("Add API")) apis.Add(GraphicsDeviceType.Vulkan);
            }

            PlayerSettings.SetGraphicsAPIs(_activeTarget, apis.ToArray());
        }

        // Multithreaded rendering (mobile-only guard).
        TryMobileMTRToggle(_activeGroup,
            "Multithreaded Rendering",
            "Uses multiple CPU threads to feed the GPU. Lighter: Off on very low-end CPUs. Costly: On.");

        // Static/Dynamic batching via internal API (reflection).
        DrawBatchingForPlatform();

        // GPU skinning.
        PlayerSettings.gpuSkinning = EditorGUILayout.Toggle(
            GC("GPU Skinning", "Animate skinned meshes on GPU. Lighter: On (frees CPU). Costly: Off."),
            PlayerSettings.gpuSkinning);

        // Graphics jobs (plus mode via reflection).
        PlayerSettings.graphicsJobs = EditorGUILayout.Toggle(
            GC("Graphics Jobs", "Multithreaded command buffer recording. Lighter: On. Costly: Off."),
            PlayerSettings.graphicsJobs);
        EnumFieldViaReflection(typeof(PlayerSettings), "graphicsJobMode",
            GC("Graphics Jobs Mode", "Scheduling strategy for graphics jobs. Lighter: Native/Auto."));

        // Lightmap streaming (not public in U6; expose only if present).
        bool hasLmStream = HasPublicStatic(typeof(PlayerSettings), "lightmapStreamingEnabled", typeof(bool));
        bool hasLmPrio = HasPublicStatic(typeof(PlayerSettings), "lightmapStreamingPriority", typeof(int));
        if (hasLmStream || hasLmPrio)
        {
            EditorGUILayout.Space(2);
            EditorGUILayout.LabelField("Lightmap Streaming", EditorStyles.boldLabel);
            BoolFieldViaReflection(typeof(PlayerSettings), "lightmapStreamingEnabled",
                GC("Enable Streaming", "Stream lightmaps by needed mip. Lighter: On. Costly: Off."));
            IntFieldViaReflection(typeof(PlayerSettings), "lightmapStreamingPriority",
                GC("Streaming Priority", "Higher keeps higher mips loaded. Lighter: Lower. Costly: Higher."),
                -128, 127);
        }

        // Sprite batching knobs via reflection (if present).
        IntFieldViaReflection(typeof(PlayerSettings), "spriteBatchVertexThreshold",
            GC("Sprite Batching Threshold",
                "Minimum vertex count for a sprite batch. Lighter: Higher threshold. Costly: Lower."),
            0, 65535);
        IntFieldViaReflection(typeof(PlayerSettings), "spriteBatchMaxVertexCount",
            GC("Sprite Batching Max Vertex Count", "Max vertices per batch. Lighter: Lower. Costly: Higher."),
            64, 65535);

        // Frame timing stats.
        BoolFieldViaReflection(typeof(PlayerSettings), "enableFrameTimingStats",
            GC("Frame Timing Stats", "Collects GPU/CPU frame timing. Lighter: Off. Costly: On (tiny overhead)."));

        EditorGUILayout.EndFoldoutHeaderGroup();
    }

    // Uses reflection because batching API is internal in Unity 6.
    private void DrawBatchingForPlatform()
    {
        if (TryGetBatchingForPlatform(_activeGroup, out bool enableStatic, out bool enableDynamic))
        {
            bool newStatic = EditorGUILayout.Toggle(
                GC("Static Batching",
                    "Combine static meshes to reduce draw calls. Lighter: On (more memory). Costly: Off."),
                enableStatic);

            bool newDynamic = EditorGUILayout.Toggle(
                GC("Dynamic Batching", "Batch small dynamic meshes. Lighter: On for many tiny meshes. Costly: Off."),
                enableDynamic);

            if (newStatic != enableStatic || newDynamic != enableDynamic)
                if (!TrySetBatchingForPlatform(_activeGroup, newStatic, newDynamic))
                    EditorGUILayout.HelpBox("Failed to set batching flags via reflection.", MessageType.Warning);
        }
        else
        {
            EditorGUILayout.HelpBox(
                "Batching controls are not available on this Unity version/API (internal API not found).",
                MessageType.Info);
        }
    }

    private static void TryMobileMTRToggle(BuildTargetGroup group, string label, string tip)
    {
        try
        {
            bool mt = PlayerSettings.GetMobileMTRendering(group);
            bool newMt = EditorGUILayout.Toggle(GC(label, tip), mt);
            if (mt != newMt) PlayerSettings.SetMobileMTRendering(group, newMt);
        }
        catch
        {
            /* Safe on non-mobile. */
        }
    }

    #endregion

    #region Graphics (Project)

    private void DrawGraphicsSection()
    {
        bool fold = GetFold(kFold_Graphics, true);
        fold = EditorGUILayout.BeginFoldoutHeaderGroup(fold, "Graphics Settings");
        SetFold(kFold_Graphics, fold);
        if (!fold)
        {
            EditorGUILayout.EndFoldoutHeaderGroup();
            return;
        }

        RenderPipelineAsset rpa = (RenderPipelineAsset)EditorGUILayout.ObjectField(
            GC("Default Render Pipeline",
                "Project-wide pipeline asset. Lighter: mobile-optimized URP asset. Costly: feature-rich."),
            GraphicsSettings.defaultRenderPipeline, typeof(RenderPipelineAsset), false);
        if (rpa != GraphicsSettings.defaultRenderPipeline)
        {
            GraphicsSettings.defaultRenderPipeline = rpa;
#if USING_URP || UNITY_RENDER_PIPELINE_UNIVERSAL
            _urpAsset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
#endif
        }

        GraphicsSettings.logWhenShaderIsCompiled = EditorGUILayout.Toggle(
            GC("Log Shader Compilation",
                "Log when a shader variant compiles at runtime. Lighter: Off. Costly: On (debug only)."),
            GraphicsSettings.logWhenShaderIsCompiled);

        // Safe extras via reflection (if present).
        BoolFieldViaReflection(typeof(GraphicsSettings), "enableCompilationCaching",
            GC("Enable Compilation Caching", "Caches shader compilation results. Lighter: On. Costly: Off."));
        BoolFieldViaReflection(typeof(GraphicsSettings), "enableValidityChecks",
            GC("Enable Validity Checks", "Extra validation of rendering data. Lighter: Off. Costly: On."));

        EditorGUILayout.EndFoldoutHeaderGroup();
    }

    #endregion

    #region Always Included Shaders (Project) via SerializedObject

    private void DrawAlwaysIncludedShadersSection()
    {
        bool fold = GetFold(kFold_Shaders, true);
        fold = EditorGUILayout.BeginFoldoutHeaderGroup(fold, "Always Included Shaders");
        SetFold(kFold_Shaders, fold);
        if (!fold)
        {
            EditorGUILayout.EndFoldoutHeaderGroup();
            return;
        }

        SerializedObject so = GetGraphicsSettingsSO();
        if (so == null)
        {
            EditorGUILayout.HelpBox("Could not access ProjectSettings/GraphicsSettings.asset.", MessageType.Warning);
            EditorGUILayout.EndFoldoutHeaderGroup();
            return;
        }

        SerializedProperty arr = so.FindProperty("m_AlwaysIncludedShaders");
        if (arr == null || !arr.isArray)
        {
            EditorGUILayout.HelpBox("Property 'm_AlwaysIncludedShaders' not found (Unity 6).", MessageType.Info);
            EditorGUILayout.EndFoldoutHeaderGroup();
            return;
        }

        // Show and edit array
        for (int i = 0; i < arr.arraySize; i++)
        {
            SerializedProperty elem = arr.GetArrayElementAtIndex(i);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                Shader shader = (Shader)EditorGUILayout.ObjectField(
                    GC($"Shader {i + 1}", "Always included in builds; too many increases build size/memory."),
                    elem.objectReferenceValue, typeof(Shader), false);
                if (EditorGUI.EndChangeCheck()) elem.objectReferenceValue = shader;
                if (GUILayout.Button("Remove", GUILayout.Width(70)))
                {
                    arr.DeleteArrayElementAtIndex(i);
                    break;
                }
            }
        }

        Shader newShader = (Shader)EditorGUILayout.ObjectField(
            GC("Add Shader", "Add a shader that must be included in builds."),
            null, typeof(Shader), false);
        if (newShader != null)
        {
            // Prevent duplicates
            bool exists = false;
            for (int i = 0; i < arr.arraySize; i++)
            {
                exists |= arr.GetArrayElementAtIndex(i).objectReferenceValue == newShader;
            }

            if (!exists)
            {
                arr.InsertArrayElementAtIndex(arr.arraySize);
                arr.GetArrayElementAtIndex(arr.arraySize - 1).objectReferenceValue = newShader;
            }
        }

        if (GUILayout.Button("Apply Changes"))
        {
            so.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();
        }

        EditorGUILayout.EndFoldoutHeaderGroup();
    }

    private static SerializedObject GetGraphicsSettingsSO()
    {
        // Load the GraphicsSettings asset from ProjectSettings
        Object[] objs = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset");
        if (objs != null && objs.Length > 0 && objs[0] != null)
            return new SerializedObject(objs[0]);
        return null;
    }

    #endregion

    #region Lighting (Bake - Editor)

    private void DrawLightingBakeSection()
    {
        bool fold = GetFold(kFold_LightingBake, false);
        fold = EditorGUILayout.BeginFoldoutHeaderGroup(fold, "Lighting (Bake - Editor)");
        SetFold(kFold_LightingBake, fold);
        if (!fold)
        {
            EditorGUILayout.EndFoldoutHeaderGroup();
            return;
        }

        // Realtime / Baked GI toggles (editor-only)
        try
        {
            Lightmapping.realtimeGI = EditorGUILayout.Toggle(
                GC("Realtime GI", "Realtime Global Illumination. Lighter: Off. Costly: On."),
                Lightmapping.realtimeGI);
            Lightmapping.bakedGI = EditorGUILayout.Toggle(
                GC("Baked GI", "Baked Global Illumination for static lighting. Usually On."),
                Lightmapping.bakedGI);
        }
        catch
        {
            /* API may differ */
        }

        // Progressive lightmapper parameters
        try
        {
            LightmapEditorSettings.Lightmapper lm = LightmapEditorSettings.lightmapper;
            lm = (LightmapEditorSettings.Lightmapper)EditorGUILayout.EnumPopup(
                GC("Lightmapper", "Progressive CPU/GPU. GPU is faster for bakes if available."),
                lm);
            if (lm != LightmapEditorSettings.lightmapper) LightmapEditorSettings.lightmapper = lm;

            // Bake Resolution (always present)
            LightmapEditorSettings.bakeResolution = EditorGUILayout.FloatField(
                GC("Bake Resolution (texels/m)", "Higher = sharper lightmaps, higher memory/time. Lighter: 10–20."),
                LightmapEditorSettings.bakeResolution);

            // Indirect Resolution (Unity 6 may not expose; try reflection)
            if (!FloatFieldViaReflection(typeof(LightmapEditorSettings), "indirectResolution",
                    GC("Indirect Resolution", "Texel density for indirect lighting. Lighter: 0.5–1.")))
                EditorGUILayout.HelpBox(
                    "Indirect Resolution is not exposed in this Unity version. Using Bake Resolution instead.",
                    MessageType.None);

            // Max atlas size
            int[] atlasVals = { 512, 1024, 2048, 4096 };
            GUIContent[] atlasLabels = atlasVals.Select(v => new GUIContent(v.ToString())).ToArray();
            LightmapEditorSettings.maxAtlasSize = EditorGUILayout.IntPopup(
                GC("Max Atlas Size", "Bigger atlases pack more but increase memory. Lighter: 1024–2048."),
                LightmapEditorSettings.maxAtlasSize, atlasLabels, atlasVals);

            // Prioritize view
            LightmapEditorSettings.prioritizeView = EditorGUILayout.Toggle(
                GC("Prioritize View", "Focuses bake on the Scene view area first. Usually Off for uniform bakes."),
                LightmapEditorSettings.prioritizeView);
        }
        catch
        {
            /* ignore if API moves */
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Open Lighting Window"))
                EditorApplication.ExecuteMenuItem("Window/Rendering/Lighting");
            if (GUILayout.Button("Clear Baked Data"))
                Lightmapping.Clear();
        }

        EditorGUILayout.HelpBox("These settings affect baking quality/time and runtime memory usage of lightmaps.",
            MessageType.Info);
        EditorGUILayout.EndFoldoutHeaderGroup();
    }

    #endregion

    #region Volumes (Post-processing)

    private void DrawPostProcessingSection()
    {
        bool fold = GetFold(kFold_Post, false);
        fold = EditorGUILayout.BeginFoldoutHeaderGroup(fold, "Volumes (Post-processing)");
        SetFold(kFold_Post, fold);
        if (!fold)
        {
            EditorGUILayout.EndFoldoutHeaderGroup();
            return;
        }

#if USING_URP || UNITY_RENDER_PIPELINE_UNIVERSAL
        // Find all VolumeProfile assets in project
        string[] guids = AssetDatabase.FindAssets("t:VolumeProfile");
        if (guids.Length == 0)
        {
            EditorGUILayout.HelpBox("No VolumeProfile assets found in project.", MessageType.Info);
            EditorGUILayout.EndFoldoutHeaderGroup();
            return;
        }

        EditorGUILayout.LabelField($"Profiles found: {guids.Length}");

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Disable ALL Post"))
                BulkPostAction(guids, PostPreset.DisableAll);
            if (GUILayout.Button("Mobile-friendly Post"))
                BulkPostAction(guids, PostPreset.MobileFriendly);
        }

        EditorGUILayout.Space(4);
        foreach (var guid in guids.Take(20)) // show first 20 to avoid huge UIs
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (profile == null) continue;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.ObjectField(GC("Profile", "Volume profile asset."), profile, typeof(VolumeProfile), false);

                // Common overrides
                ToggleOverride<Bloom>(profile, "Bloom", "Image bloom/glow. Lighter: Off or low thresholds.", weakMobile: true);
                ToggleOverride<DepthOfField>(profile, "Depth Of Field", "Depth blur. Lighter: Off.");
                ToggleOverride<MotionBlur>(profile, "Motion Blur", "Perceptual blur during movement. Lighter: Off.");
                ToggleOverride<Vignette>(profile, "Vignette", "Darkens image borders. Small cost.");
                ToggleOverride<FilmGrain>(profile, "Film Grain", "Noise overlay. Lighter: Off.");
                ToggleOverride<ChromaticAberration>(profile, "Chromatic Aberration", "Color fringing at edges. Lighter: Off.");

                if (GUI.changed)
                {
                    EditorUtility.SetDirty(profile);
                }
            }
        }
        AssetDatabase.SaveAssets();
#else
        EditorGUILayout.HelpBox("URP not detected; post-processing volumes are part of URP's Volume system.",
            MessageType.Info);
#endif
        EditorGUILayout.EndFoldoutHeaderGroup();
    }

#if USING_URP || UNITY_RENDER_PIPELINE_UNIVERSAL
    private enum PostPreset { DisableAll, MobileFriendly }

    private void BulkPostAction(string[] guids, PostPreset preset)
    {
        foreach (var guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (profile == null) continue;

            switch (preset)
            {
                case PostPreset.DisableAll:
                    DisableAllOverrides(profile);
                    break;
                case PostPreset.MobileFriendly:
                    ApplyMobileFriendly(profile);
                    break;
            }
            EditorUtility.SetDirty(profile);
        }
        AssetDatabase.SaveAssets();
    }

    private void DisableAllOverrides(VolumeProfile profile)
    {
        foreach (var comp in profile.components)
        {
            comp.active = false;
        }
    }

    private void ApplyMobileFriendly(VolumeProfile profile)
    {
        // Disable heavy ones, keep light ones minimal
        SetOverrideActive<DepthOfField>(profile, false);
        SetOverrideActive<MotionBlur>(profile, false);
        SetOverrideActive<ChromaticAberration>(profile, false);
        SetOverrideActive<FilmGrain>(profile, false);

        // Bloom: keep but reduce intensity/threshold if present
        var bloom = GetOrAdd<Bloom>(profile);
        bloom.active = true;
        if (!bloom.intensity.overrideState) bloom.intensity.overrideState = true;
        bloom.intensity.value = Mathf.Min(bloom.intensity.value, 0.2f);
        if (!bloom.threshold.overrideState) bloom.threshold.overrideState = true;
        bloom.threshold.value = Mathf.Max(bloom.threshold.value, 1.2f);

        // Vignette: small cost
        var vig = GetOrAdd<Vignette>(profile);
        vig.active = true;
        if (!vig.intensity.overrideState) vig.intensity.overrideState = true;
        vig.intensity.value = Mathf.Min(vig.intensity.value, 0.2f);
    }

    private void ToggleOverride<T>(VolumeProfile profile, string label, string tip, bool weakMobile =
 false) where T : VolumeComponent
    {
        var comp = GetOrAdd<T>(profile);
        bool newActive =
 EditorGUILayout.Toggle(GC(label, tip + (weakMobile ? " Recommended low for mobile." : "")), comp.active);
        if (newActive != comp.active) comp.active = newActive;
    }

    private T GetOrAdd<T>(VolumeProfile profile) where T : VolumeComponent
    {
        if (!profile.TryGet<T>(out var comp))
        {
            comp = profile.Add<T>();
        }
        return comp;
    }

    private void SetOverrideActive<T>(VolumeProfile profile, bool active) where T : VolumeComponent
    {
        if (profile.TryGet<T>(out var comp))
            comp.active = active;
    }
#endif

    #endregion

    #region Android (Platform tweaks)

    private void DrawAndroidSection()
    {
        bool fold = GetFold(kFold_Android, false);
        fold = EditorGUILayout.BeginFoldoutHeaderGroup(fold, "Android (Platform tweaks)");
        SetFold(kFold_Android, fold);
        if (!fold)
        {
            EditorGUILayout.EndFoldoutHeaderGroup();
            return;
        }

#if UNITY_ANDROID
        // Blit type can impact a blit pass on some paths.
        try
        {
            AndroidBlitType blit = PlayerSettings.Android.blitType;
            blit = (AndroidBlitType)EditorGUILayout.EnumPopup(
                GC("Blit Type",
                    "Controls whether a blit is used before presenting. Lighter: Never (depending on pipeline/device)."),
                blit);
            if (blit != PlayerSettings.Android.blitType) PlayerSettings.Android.blitType = blit;
        }
        catch
        {
            /* older editor or API moved */
        }
#else
        EditorGUILayout.HelpBox("Switch to Android build target to edit Android-specific options.", MessageType.Info);
#endif

        // Vulkan advanced (guard + reflection) – shown only on Android BuildTargetGroup
        if (_activeGroup == BuildTargetGroup.Android)
        {
            bool vkFold = GetFold(kFold_Vulkan, false);
            vkFold = EditorGUILayout.Foldout(vkFold, "Android Vulkan (advanced)", true);
            SetFold(kFold_Vulkan, vkFold);
            if (vkFold)
            {
                BoolFieldViaReflection(typeof(PlayerSettings), "vulkanSRGBWriteMode",
                    GC("sRGB Write Mode", "Enable sRGB write control. Lighter: Off. Costly: On."));
                IntFieldViaReflection(typeof(PlayerSettings), "vulkanNumSwapchainBuffers",
                    GC("Number of swapchain buffers",
                        "More buffers reduce stalls but add latency/memory. Lighter: 2–3. Costly: 3–4+."),
                    2, 6);
                BoolFieldViaReflection(typeof(PlayerSettings), "vulkanEnableLateAcquireNextImage",
                    GC("Acquire image late", "Acquire next swap image as late as possible. Lighter: On. Costly: Off."));
                BoolFieldViaReflection(typeof(PlayerSettings), "vulkanUseSWCommandBuffers",
                    GC("Recycle command buffers", "Reuse software command buffers. Lighter: On. Costly: Off."));
                BoolFieldViaReflection(typeof(PlayerSettings), "vulkanEnablePreTransform",
                    GC("Apply display rotation during rendering",
                        "Pre-rotates rendering to match display. Lighter: Off if not needed. Costly: On."));
            }
        }

        EditorGUILayout.EndFoldoutHeaderGroup();
    }

    #endregion

#if USING_URP || UNITY_RENDER_PIPELINE_UNIVERSAL
    #region URP (core)
    private void DrawURPSection()
    {
        bool fold = GetFold(kFold_URP, true);
        fold = EditorGUILayout.BeginFoldoutHeaderGroup(fold, "URP (Universal Render Pipeline)");
        SetFold(kFold_URP, fold);
        if (!fold) { EditorGUILayout.EndFoldoutHeaderGroup(); return; }

        _urpAsset = (UniversalRenderPipelineAsset)EditorGUILayout.ObjectField(
            GC("URP Asset", "URP configuration used by the project. Choose a mobile/stripped variant for lighter builds."),
            _urpAsset, typeof(UniversalRenderPipelineAsset), false);
        if (_urpAsset != (GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset))
        {
            GraphicsSettings.defaultRenderPipeline = _urpAsset;
            _urpAsset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        }

        if (_urpAsset == null)
        {
            EditorGUILayout.HelpBox("Assign a URP Asset to edit pipeline-specific performance settings.", MessageType.Warning);
            EditorGUILayout.EndFoldoutHeaderGroup();
            return;
        }

        _urpAsset.supportsHDR = EditorGUILayout.Toggle(
            GC("HDR", "High Dynamic Range. Lighter: Off. Costly: On."),
            _urpAsset.supportsHDR);

        _urpAsset.msaaSampleCount = (int)(MSAASamples)EditorGUILayout.EnumPopup(
            GC("MSAA", "Multisample anti-aliasing. Lighter: Disabled/2x. Costly: 4x/8x."),
            (MSAASamples)_urpAsset.msaaSampleCount);

        _urpAsset.renderScale = EditorGUILayout.Slider(
            GC("Render Scale", "Scales camera render resolution. Lighter: < 1.0 (e.g., 0.8). Costly: > 1.0 (supersample)."),
            _urpAsset.renderScale, 0.1f, 2.0f);

        _urpAsset.supportsCameraDepthTexture = EditorGUILayout.Toggle(
            GC("Depth Texture", "Generates _CameraDepthTexture. Lighter: Off if not used. Costly: On."),
            _urpAsset.supportsCameraDepthTexture);

        _urpAsset.supportsCameraOpaqueTexture = EditorGUILayout.Toggle(
            GC("Opaque Texture", "Copies color buffer for post effects. Lighter: Off. Costly: On."),
            _urpAsset.supportsCameraOpaqueTexture);

        _urpAsset.shadowDistance = EditorGUILayout.Slider(
            GC("Max Shadow Distance", "How far shadows are rendered. Lighter: Smaller. Costly: Larger."),
            _urpAsset.shadowDistance, 0f, 200f);

        _urpAsset.shadowCascadeCount = EditorGUILayout.IntSlider(
            GC("Cascade Count", "Directional shadow splits. Lighter: 1. Costly: 2–4."),
            _urpAsset.shadowCascadeCount, 1, 4);

        _urpAsset.supportsDynamicBatching = EditorGUILayout.Toggle(
            GC("Dynamic Batching", "Batch small dynamic meshes. Lighter: On for many tiny meshes. Costly: Off."),
            _urpAsset.supportsDynamicBatching);

#if UNITY_2022_2_OR_NEWER
        _urpAsset.useSRPBatcher = EditorGUILayout.Toggle(
            GC("SRP Batcher", "Optimizes material/shader binding. Lighter: On. Costly: Off."),
            _urpAsset.useSRPBatcher);
#endif

        if (GUI.changed)
        {
            EditorUtility.SetDirty(_urpAsset);
            AssetDatabase.SaveAssets();
        }

        EditorGUILayout.EndFoldoutHeaderGroup();
    }
    #endregion

    #region URP Lights & Shadows (detailed)
    private void DrawURPLightsAndShadowsSection()
    {
        bool fold = GetFold(kFold_URPLights, true);
        fold = EditorGUILayout.BeginFoldoutHeaderGroup(fold, "URP: Lights & Shadows (Detailed)");
        SetFold(kFold_URPLights, fold);
        if (!fold) { EditorGUILayout.EndFoldoutHeaderGroup(); return; }
        if (_urpAsset == null) { EditorGUILayout.HelpBox("Assign a URP Asset.", MessageType.Warning); EditorGUILayout.EndFoldoutHeaderGroup(); return; }

        // Use reflection to be version-safe across URP variants.
        // Additional lights mode: PerPixel / PerVertex
        EnumFieldViaReflectionInst(_urpAsset, "additionalLightsRenderingMode",
            GC("Additional Lights Mode", "Per Vertex is lighter; Per Pixel is costlier (per-fragment lighting)."));

        IntFieldViaReflectionInst(_urpAsset, "maxAdditionalLights",
            GC("Max Additional Lights", "Caps total additional lights affecting rendering. Lighter: Lower."),
            0, 32);

        IntFieldViaReflectionInst(_urpAsset, "additionalLightsPerObjectLimit",
            GC("Additional Lights / Object", "Per-object light cap. Lighter: Lower."),
            0, 8);

        BoolFieldViaReflectionInst(_urpAsset, "mainLightShadowsSupported",
            GC("Main Light Shadows", "Enable shadows for main directional light. Lighter: Off."));

        IntFieldViaReflectionInst(_urpAsset, "mainLightShadowmapResolution",
            GC("Main Light Shadowmap Resolution", "Shadowmap resolution. Lighter: 512–1024; Costly: 2048+."), 256, 8192);

        BoolFieldViaReflectionInst(_urpAsset, "additionalLightsShadowsSupported",
            GC("Additional Lights Shadows", "Enable shadows for additional lights. Lighter: Off."));

        IntFieldViaReflectionInst(_urpAsset, "additionalLightsShadowmapResolution",
            GC("Additional Lights Shadowmap Resolution", "Resolution for additional lights. Lighter: 512–1024."), 256, 4096);

        // Soft shadows toggle (name varies by version)
        BoolFieldViaReflectionInst(_urpAsset, "supportsSoftShadows",
            GC("Soft Shadows", "PCF/soft shadows. Lighter: Off."));

        // Opaque Downsampling (enum)
        EnumFieldViaReflectionInst(_urpAsset, "opaqueDownsampling",
            GC("Opaque Downsampling", "Downsamples Opaque Texture. Lighter: 2x/4x."));

        // Accurate gbuffer normals (when applicable to deferred)
        BoolFieldViaReflectionInst(_urpAsset, "supportsAccurateGbufferNormals",
            GC("Accurate GBuffer Normals", "Improves normal precision. Lighter: Off."));

        if (GUI.changed)
        {
            EditorUtility.SetDirty(_urpAsset);
            AssetDatabase.SaveAssets();
        }

        EditorGUILayout.EndFoldoutHeaderGroup();
    }
    #endregion

    #region URP Renderer Data
    private void DrawURPRendererDataSection()
    {
        bool fold = GetFold(kFold_URPRendererData, false);
        fold = EditorGUILayout.BeginFoldoutHeaderGroup(fold, "URP: Renderer Data");
        SetFold(kFold_URPRendererData, fold);
        if (!fold) { EditorGUILayout.EndFoldoutHeaderGroup(); return; }
        if (_urpAsset == null) { EditorGUILayout.HelpBox("Assign a URP Asset.", MessageType.Warning); EditorGUILayout.EndFoldoutHeaderGroup(); return; }

        var rd = _urpAsset.scriptableRendererData; // may be null
        rd = (ScriptableRendererData)EditorGUILayout.ObjectField(
            GC("Renderer Data", "The active renderer configuration. Different renderers expose extra toggles."),
            rd, typeof(ScriptableRendererData), false);

        if (rd == null)
        {
            EditorGUILayout.HelpBox("No Renderer Data assigned in URP Asset.", MessageType.Info);
            EditorGUILayout.EndFoldoutHeaderGroup();
            return;
        }

        // Common toggles via reflection (version-safe)
        BoolFieldViaReflectionInst(rd, "accurateGbufferNormals",
            GC("Accurate GBuffer Normals (Renderer)", "If available in your Renderer. Lighter: Off."));
        BoolFieldViaReflectionInst(rd, "transparentReceiveShadows",
            GC("Transparent Receive Shadows", "Transparent objects receive shadows. Lighter: Off."));
        EnumFieldViaReflectionInst(rd, "depthPrimingMode",
            GC("Depth Priming Mode", "Priming may reduce overdraw at some cost. Lighter: Disabled/Auto (depending on version)."));
        EnumFieldViaReflectionInst(rd, "copyDepthMode",
            GC("Copy Depth Mode", "Controls when/how depth is copied. Lighter: After Opaque / Minimal copies."));

        if (GUI.changed)
        {
            EditorUtility.SetDirty(rd);
            AssetDatabase.SaveAssets();
        }

        EditorGUILayout.EndFoldoutHeaderGroup();
    }
    #endregion
#endif

    #region Helpers (reflection-safe static)

    private static void EnumFieldViaReflection(Type owner, string propertyOrField, GUIContent gc)
    {
        try
        {
            PropertyInfo p = owner.GetProperty(propertyOrField, BindingFlags.Public | BindingFlags.Static);
            if (p != null && p.PropertyType.IsEnum)
            {
                object val = p.GetValue(null, null);
                Enum newVal = EditorGUILayout.EnumPopup(gc, (Enum)val);
                if (!Equals(newVal, val)) p.SetValue(null, newVal, null);
                return;
            }

            FieldInfo f = owner.GetField(propertyOrField, BindingFlags.Public | BindingFlags.Static);
            if (f != null && f.FieldType.IsEnum)
            {
                Enum val = (Enum)f.GetValue(null);
                Enum newVal = EditorGUILayout.EnumPopup(gc, val);
                if (!Equals(newVal, val)) f.SetValue(null, newVal);
            }
        }
        catch
        {
            /* no-op */
        }
    }

    private static void BoolFieldViaReflection(Type owner, string propertyOrField, GUIContent gc)
    {
        try
        {
            PropertyInfo p = owner.GetProperty(propertyOrField, BindingFlags.Public | BindingFlags.Static);
            if (p != null && p.PropertyType == typeof(bool))
            {
                bool val = (bool)p.GetValue(null, null);
                bool newVal = EditorGUILayout.Toggle(gc, val);
                if (newVal != val) p.SetValue(null, newVal, null);
                return;
            }

            FieldInfo f = owner.GetField(propertyOrField, BindingFlags.Public | BindingFlags.Static);
            if (f != null && f.FieldType == typeof(bool))
            {
                bool val = (bool)f.GetValue(null);
                bool newVal = EditorGUILayout.Toggle(gc, val);
                if (newVal != val) f.SetValue(null, newVal);
            }
        }
        catch
        {
            /* no-op */
        }
    }

    private static void IntFieldViaReflection(Type owner, string propertyOrField, GUIContent gc, int min, int max)
    {
        try
        {
            PropertyInfo p = owner.GetProperty(propertyOrField, BindingFlags.Public | BindingFlags.Static);
            if (p != null && p.PropertyType == typeof(int))
            {
                int val = (int)p.GetValue(null, null);
                int newVal = Mathf.Clamp(EditorGUILayout.IntField(gc, val), min, max);
                if (newVal != val) p.SetValue(null, newVal, null);
                return;
            }

            FieldInfo f = owner.GetField(propertyOrField, BindingFlags.Public | BindingFlags.Static);
            if (f != null && f.FieldType == typeof(int))
            {
                int val = (int)f.GetValue(null);
                int newVal = Mathf.Clamp(EditorGUILayout.IntField(gc, val), min, max);
                if (newVal != val) f.SetValue(null, newVal);
            }
        }
        catch
        {
            /* no-op */
        }
    }

    private static bool FloatFieldViaReflection(Type owner, string propertyOrField, GUIContent gc)
    {
        try
        {
            PropertyInfo p = owner.GetProperty(propertyOrField, BindingFlags.Public | BindingFlags.Static);
            if (p != null && p.PropertyType == typeof(float))
            {
                float val = (float)p.GetValue(null, null);
                float newVal = EditorGUILayout.FloatField(gc, val);
                if (!Mathf.Approximately(newVal, val)) p.SetValue(null, newVal, null);
                return true;
            }

            FieldInfo f = owner.GetField(propertyOrField, BindingFlags.Public | BindingFlags.Static);
            if (f != null && f.FieldType == typeof(float))
            {
                float val = (float)f.GetValue(null);
                float newVal = EditorGUILayout.FloatField(gc, val);
                if (!Mathf.Approximately(newVal, val)) f.SetValue(null, newVal);
                return true;
            }
        }
        catch
        {
            /* no-op */
        }

        return false;
    }

    private static bool HasPublicStatic(Type owner, string propertyName, Type type)
    {
        PropertyInfo p = owner.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Static);
        return p != null && p.PropertyType == type;
    }

    #endregion

#if USING_URP || UNITY_RENDER_PIPELINE_UNIVERSAL
    #region Helpers (reflection-safe instance for URP assets / renderer data)
    private static void BoolFieldViaReflectionInst(UnityEngine.Object obj, string member, GUIContent gc)
    {
        if (obj == null) return;
        var t = obj.GetType();
        try
        {
            var p = t.GetProperty(member, BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
            if (p != null && p.PropertyType == typeof(bool) && p.CanRead && p.CanWrite)
            {
                bool val = (bool)p.GetValue(obj);
                bool newVal = EditorGUILayout.Toggle(gc, val);
                if (newVal != val) p.SetValue(obj, newVal);
                return;
            }
            var f = t.GetField(member, BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
            if (f != null && f.FieldType == typeof(bool))
            {
                bool val = (bool)f.GetValue(obj);
                bool newVal = EditorGUILayout.Toggle(gc, val);
                if (newVal != val) f.SetValue(obj, newVal);
            }
        }
        catch { /* no-op */ }
    }

    private static void IntFieldViaReflectionInst(UnityEngine.Object obj, string member, GUIContent gc, int min, int max)
    {
        if (obj == null) return;
        var t = obj.GetType();
        try
        {
            var p = t.GetProperty(member, BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
            if (p != null && p.PropertyType == typeof(int) && p.CanRead && p.CanWrite)
            {
                int val = (int)p.GetValue(obj);
                int newVal = Mathf.Clamp(EditorGUILayout.IntField(gc, val), min, max);
                if (newVal != val) p.SetValue(obj, newVal);
                return;
            }
            var f = t.GetField(member, BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
            if (f != null && f.FieldType == typeof(int))
            {
                int val = (int)f.GetValue(obj);
                int newVal = Mathf.Clamp(EditorGUILayout.IntField(gc, val), min, max);
                if (newVal != val) f.SetValue(obj, newVal);
            }
        }
        catch { /* no-op */ }
    }

    private static void EnumFieldViaReflectionInst(UnityEngine.Object obj, string member, GUIContent gc)
    {
        if (obj == null) return;
        var t = obj.GetType();
        try
        {
            var p = t.GetProperty(member, BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
            if (p != null && p.PropertyType.IsEnum && p.CanRead && p.CanWrite)
            {
                var val = (Enum)p.GetValue(obj);
                var newVal = EditorGUILayout.EnumPopup(gc, val);
                if (!Equals(newVal, val)) p.SetValue(obj, newVal);
                return;
            }
            var f = t.GetField(member, BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
            if (f != null && f.FieldType.IsEnum)
            {
                var val = (Enum)f.GetValue(obj);
                var newVal = EditorGUILayout.EnumPopup(gc, val);
                if (!Equals(newVal, val)) f.SetValue(obj, newVal);
            }
        }
        catch { /* no-op */ }
    }
    #endregion
#endif

    #region Reflection for internal PlayerSettings batching API (Unity 6)

    private static bool TryGetBatchingForPlatform(BuildTargetGroup group, out bool staticBatching,
        out bool dynamicBatching)
    {
        staticBatching = false;
        dynamicBatching = false;

        Type t = typeof(PlayerSettings);
        MethodInfo method = t.GetMethod(
            "GetBatchingForPlatform",
            BindingFlags.NonPublic | BindingFlags.Static,
            null,
            new[] { typeof(BuildTargetGroup), typeof(bool).MakeByRefType(), typeof(bool).MakeByRefType() },
            null);

        if (method == null) return false;

        object[] args = { group, staticBatching, dynamicBatching };
        try
        {
            method.Invoke(null, args);
            staticBatching = (bool)args[1];
            dynamicBatching = (bool)args[2];
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TrySetBatchingForPlatform(BuildTargetGroup group, bool staticBatching, bool dynamicBatching)
    {
        Type t = typeof(PlayerSettings);
        MethodInfo method = t.GetMethod(
            "SetBatchingForPlatform",
            BindingFlags.NonPublic | BindingFlags.Static,
            null,
            new[] { typeof(BuildTargetGroup), typeof(bool), typeof(bool) },
            null);

        if (method == null) return false;

        try
        {
            method.Invoke(null, new object[] { group, staticBatching, dynamicBatching });
            return true;
        }
        catch
        {
            return false;
        }
    }

    #endregion

    #region Foldout state

    private static bool GetFold(string key, bool defVal)
    {
        return EditorPrefs.GetBool(key, defVal);
    }

    private static void SetFold(string key, bool val)
    {
        EditorPrefs.SetBool(key, val);
    }

    #endregion
}

// Dummy to allow menu focus; Lighting window exists via menu in Unity 6.
internal class LightingWindow : EditorWindow
{
}
#endif