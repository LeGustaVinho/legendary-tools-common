using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace LegendaryTools.Editor
{
    internal static class TextureUtil
    {
        /// <summary>
        /// Returns "Name (WxH)" with the name clamped to maxLen.
        /// </summary>
        public static string GetTextureDisplayName(Texture tex, int maxLen)
        {
            if (tex == null) return "(None)";
            string baseName = Clamp(tex.name, Math.Max(4, maxLen));
            return $"{baseName} {GetTextureSizeSuffix(tex)}";
        }

        /// <summary>
        /// Returns the size suffix for a texture, e.g., "(1024x1024)".
        /// </summary>
        public static string GetTextureSizeSuffix(Texture tex)
        {
            try
            {
                int w = tex.width;
                int h = tex.height;
                if (w > 0 && h > 0)
                    return $"({w}x{h})";
            }
            catch
            {
                /* ignore */
            }

            return string.Empty;
        }

        /// <summary>
        /// Clamps a string to maxLen characters, appending an ellipsis if it overflows.
        /// </summary>
        public static string Clamp(string s, int maxLen)
        {
            if (string.IsNullOrEmpty(s) || s.Length <= maxLen) return s;
            if (maxLen <= 1) return "…";
            return s.Substring(0, maxLen - 1) + "…";
        }

        /// <summary>
        /// Formats bytes as a human-friendly string (e.g., 12.3 MB).
        /// </summary>
        public static string FormatBytes(long bytes)
        {
            const long KB = 1024;
            const long MB = 1024 * 1024;
            const long GB = 1024L * 1024L * 1024L;
            if (bytes >= GB) return $"{bytes / (double)GB:0.##} GB";
            if (bytes >= MB) return $"{bytes / (double)MB:0.##} MB";
            if (bytes >= KB) return $"{bytes / (double)KB:0.##} KB";
            return $"{bytes} B";
        }

        /// <summary>
        /// Estimates the memory footprint of a texture including mipmaps (rough).
        /// Works best for Texture2D. Falls back to 4 bytes per pixel heuristic.
        /// </summary>
        public static long EstimateTextureMemory(Texture tex)
        {
            if (tex == null) return 0;

            int w, h;
            try
            {
                w = tex.width;
                h = tex.height;
            }
            catch
            {
                return 0;
            }

            if (w <= 0 || h <= 0) return 0;

            double bytesPerPixel = 4.0;
            double mipFactor = 1.33;

#if UNITY_EDITOR
            Texture2D tex2D = tex as Texture2D;
            if (tex2D != null)
            {
                double bpp = 4.0;
                switch (tex2D.format)
                {
                    // Uncompressed (kept conservative)
                    case TextureFormat.RGBA32:
                    case TextureFormat.ARGB32:
                    case TextureFormat.BGRA32:
                    case TextureFormat.RGB24:
                    case TextureFormat.RGBA4444:
                    case TextureFormat.RGBAFloat:
                        bpp = 4.0; break;

                    // BC / DXT
                    case TextureFormat.DXT1:
                        bpp = 0.5; break;
                    case TextureFormat.DXT5:
                        bpp = 1.0; break;

                    // ETC / ETC2
                    case TextureFormat.ETC_RGB4:
                        bpp = 0.5; break;
                    case TextureFormat.ETC2_RGBA8:
                        bpp = 1.0; break;

                    // ASTC (approximate)
                    case TextureFormat.ASTC_4x4:
                        bpp = 1.0; break;
                    case TextureFormat.ASTC_6x6:
                        bpp = 0.44; break;
                    case TextureFormat.ASTC_8x8:
                        bpp = 0.25; break;

                    // PVRTC (rough)
                    case TextureFormat.PVRTC_RGB2:
                    case TextureFormat.PVRTC_RGBA2:
                        bpp = 0.25; break;
                    case TextureFormat.PVRTC_RGB4:
                    case TextureFormat.PVRTC_RGBA4:
                        bpp = 0.5; break;

                    default:
                        bpp = 4.0; break;
                }

                bytesPerPixel = bpp;

                // Use real mip count if available; otherwise keep heuristic
                try
                {
                    int mips = tex2D.mipmapCount;
                    if (mips > 1)
                        // Approx: 1 + 1/4 + 1/16 + ... ≈ 1.33 (cap)
                        mipFactor = Math.Min(1.33, 1.0 + (mips - 1) * 0.25);
                    else
                        mipFactor = 1.0;
                }
                catch
                {
                    /* keep heuristic */
                }
            }
#endif
            long baseBytes = (long)Math.Round(w * h * bytesPerPixel);
            return (long)Math.Round(baseBytes * mipFactor);
        }

        /// <summary>
        /// Collects all textures referenced by the given materials across all texture properties.
        /// Unity 6 note: checks only TexEnv.
        /// </summary>
        public static Texture[] CollectAllTextures(Material[] materials)
        {
            HashSet<Texture> set = new();
#if UNITY_EDITOR
            foreach (Material mat in materials)
            {
                if (mat == null || mat.shader == null)
                    continue;

                Shader shader = mat.shader;
                int propCount = ShaderUtil.GetPropertyCount(shader);
                for (int i = 0; i < propCount; i++)
                {
                    ShaderUtil.ShaderPropertyType type = ShaderUtil.GetPropertyType(shader, i);
#if UNITY_6000_0_OR_NEWER
                    if (type == ShaderUtil.ShaderPropertyType.TexEnv)
#else
                    if (type == ShaderUtil.ShaderPropertyType.TexEnv
                        || type == ShaderUtil.ShaderPropertyType.Texture)
#endif
                    {
                        string propName = ShaderUtil.GetPropertyName(shader, i);
                        Texture tex = mat.GetTexture(propName);
                        if (tex != null) set.Add(tex);
                    }
                }

                // Fallback alias
                Texture mainTex = mat.HasProperty("_MainTex") ? mat.GetTexture("_MainTex") : null;
                if (mainTex != null) set.Add(mainTex);
            }
#endif
            return set.ToArray();
        }
    }
}