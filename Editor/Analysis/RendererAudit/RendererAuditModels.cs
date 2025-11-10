using System;
using System.Collections.Generic;
using UnityEngine;

namespace LegendaryTools.Editor
{
    internal enum GroupMode
    {
        Material,
        Shader,
        Texture,
        ParticleSystem
    }

    internal enum StaticFilter
    {
        All,
        OnlyStatic,
        OnlyNonStatic
    }

    internal enum InstancingFilter
    {
        All,
        EligibleOnly,
        EnabledOnly,
        SupportedButDisabledOnly,
        UnsupportedOrUnknownOnly
    }

    internal enum BatchingFilter
    {
        All,
        DynamicEligibleOnly,
        StaticEligibleOnly,
        DynamicNotEligibleOnly,
        StaticNotEligibleOnly
    }

    // Shadow filter options
    internal enum ShadowFilter
    {
        All,
        CastOnly,
        ReceiveOnly,
        CastAndReceive,
        NoShadows,
        ReceiveProbablyUnnecessaryOnly
    }

    // Transparency filter
    internal enum TransparencyFilter
    {
        All,
        TransparentOnly,
        OpaqueOnly,
        HighQueueOnly, // renderQueue >= 3000
        OverdrawRiskOnly
    }

    internal enum GroupSortKey
    {
        Count,
        DrawCalls,
        TextureMemory
    }

    internal sealed class RendererEntry
    {
        public Renderer Renderer;
        public string SceneName;
        public string GameObjectPath;
        public bool IsStatic;
        public bool IsSkinned;
        public bool IsParticleSystem;

        public Material[] Materials;
        public Shader[] Shaders;
        public Texture[] Textures;
        public ParticleSystem ParticleSystem;

        // GPU Instancing
        public bool InstancingEligible;
        public bool InstancingEnabled;
        public bool InstancingSupportedButDisabled;

        // Batching
        public bool DynamicBatchEligible;
        public List<string> DynamicBatchReasons = new();
        public bool StaticBatchEligible;
        public List<string> StaticBatchReasons = new();

        // Shadows
        public bool CastsShadows;
        public bool ReceivesShadows;
        public bool ReceivesShadowProbablyUnnecessary;

        // Transparency
        public bool HasTransparentMaterial;
        public int MaxRenderQueue; // max among materials
        public int TransparentMaterialCount;
        public bool TransparencyOverdrawRisk; // large on screen with transparent
    }

    internal struct GroupCost
    {
        public int RendererCount;
        public int PotentialDrawCalls;
        public long TextureBytes;
    }

    internal struct GroupRow<T> where T : UnityEngine.Object
    {
        public T Key;
        public List<RendererEntry> VisibleList;
        public GroupCost Cost;
    }

    internal sealed class ScanSummary
    {
        public int RendererTotal;
        public int MeshRendererCount;
        public int SkinnedRendererCount;
        public int ParticleSystemRendererCount;

        public int InstancingEligible;
        public int InstancingEnabled;
        public int InstancingSupportedButDisabled;
        public int InstancingUnsupportedOrUnknown;

        // Shadows
        public int CastShadowCount;
        public int ReceiveShadowCount;
        public int ReceiveProbablyUnnecessaryCount;

        // Transparency
        public int TransparentRendererCount;
        public int HighQueueRendererCount;
        public int OverdrawRiskRendererCount;

        public HashSet<Material> UniqueMaterials = new();
        public HashSet<Shader> UniqueShaders = new();
        public HashSet<Texture> UniqueTextures = new();
    }
}