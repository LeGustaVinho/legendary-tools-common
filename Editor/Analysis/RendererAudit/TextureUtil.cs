using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

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
                // Intentionally ignored.
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
        /// Uses GraphicsFormat for better Unity 6 compatibility.
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
            double mipFactor = 1.0;

            // Prefer Texture2D for mip info and accurate format info.
            if (tex is Texture2D tex2D)
            {
                bytesPerPixel = GetBytesPerPixelSafe(tex2D.graphicsFormat);

                int mips = 1;
                try
                {
                    mips = tex2D.mipmapCount;
                }
                catch
                {
                    // Intentionally ignored.
                }

                mipFactor = GetMipChainFactor(mips);
            }
            else
            {
                // Fallback heuristic for non-Texture2D types.
                mipFactor = 1.33;
            }

            double baseBytes = w * (double)h * bytesPerPixel;
            return (long)Math.Round(baseBytes * mipFactor);
        }

        /// <summary>
        /// Collects all textures referenced by the given materials across all texture properties.
        /// Unity 6: uses Shader.GetProperty* and UnityEngine.Rendering.ShaderPropertyType.
        /// </summary>
        public static Texture[] CollectAllTextures(Material[] materials)
        {
            if (materials == null || materials.Length == 0)
                return Array.Empty<Texture>();

            HashSet<Texture> set = new();

            foreach (Material mat in materials)
            {
                if (mat == null) continue;

                Shader shader = mat.shader;
                if (shader == null) continue;

                int propCount;
                try
                {
                    propCount = shader.GetPropertyCount();
                }
                catch
                {
                    continue;
                }

                for (int i = 0; i < propCount; i++)
                {
                    ShaderPropertyType type;
                    try
                    {
                        type = shader.GetPropertyType(i);
                    }
                    catch
                    {
                        continue;
                    }

                    if (type != ShaderPropertyType.Texture)
                        continue;

                    string propName;
                    try
                    {
                        propName = shader.GetPropertyName(i);
                    }
                    catch
                    {
                        continue;
                    }

                    Texture tex = mat.GetTexture(propName);
                    if (tex != null) set.Add(tex);
                }

                // Fallback alias.
                if (mat.HasProperty("_MainTex"))
                {
                    Texture mainTex = mat.GetTexture("_MainTex");
                    if (mainTex != null) set.Add(mainTex);
                }
            }

            return set.ToArray();
        }

        private static double GetBytesPerPixelSafe(GraphicsFormat format)
        {
            // GraphicsFormatUtility provides block metrics. For uncompressed formats,
            // block width/height are 1, so this also yields bytes-per-pixel.
            try
            {
                uint blockWidth = GraphicsFormatUtility.GetBlockWidth(format);
                uint blockHeight = GraphicsFormatUtility.GetBlockHeight(format);
                uint blockSizeBytes = GraphicsFormatUtility.GetBlockSize(format);

                if (blockWidth > 0 && blockHeight > 0 && blockSizeBytes > 0)
                    return blockSizeBytes / (double)(blockWidth * blockHeight);
            }
            catch
            {
                // Intentionally ignored.
            }

            // Conservative fallback.
            return 4.0;
        }

        private static double GetMipChainFactor(int mipCount)
        {
            if (mipCount <= 1) return 1.0;

            // Sum of geometric series: 1 + 1/4 + 1/16 + ... for mipCount terms.
            // factor = (1 - 0.25^mipCount) / (1 - 0.25) = (4/3) * (1 - 0.25^mipCount)
            return (4.0 / 3.0) * (1.0 - Math.Pow(0.25, mipCount));
        }
    }
}
