using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace LegendaryTools.Editor
{
    internal static class InstancingUtil
    {
        /// <summary>
        /// Attempts to detect whether a shader supports GPU Instancing.
        /// Uses UnityEditor.ShaderUtil.HasInstancing via reflection when available.
        /// Falls back to false if not available.
        /// </summary>
        public static bool ShaderSupportsInstancing(Shader s)
        {
#if UNITY_EDITOR
            if (s == null) return false;
            try
            {
                Type t = typeof(ShaderUtil);
                MethodInfo mi = t.GetMethod("HasInstancing",
                    BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                if (mi != null)
                {
                    object result = mi.Invoke(null, new object[] { s });
                    if (result is bool b) return b;
                }
            }
            catch
            {
                /* ignore */
            }
#endif
            return false; // Unknown => conservative
        }

        /// <summary>
        /// Computes per-renderer instancing flags based on its materials.
        /// A renderer is "eligible" if it has at least one material whose shader supports instancing.
        /// It is "enabled" if any compatible material has Material.enableInstancing = true.
        /// If eligible but none enabled, it's "supported but disabled".
        /// </summary>
        public static void ComputeInstancingForRenderer(
            Material[] uniqueMats,
            out bool eligible,
            out bool enabled,
            out bool supportedButDisabled)
        {
            eligible = false;
            enabled = false;
            supportedButDisabled = false;

            bool foundSupported = false;
            bool foundEnabledOnSupported = false;

            foreach (Material m in uniqueMats)
            {
                if (m == null || m.shader == null) continue;
                bool supports = ShaderSupportsInstancing(m.shader);
                if (!supports) continue;

                foundSupported = true;
                if (m.enableInstancing)
                    foundEnabledOnSupported = true;
            }

            if (foundSupported)
            {
                eligible = true;
                if (foundEnabledOnSupported)
                    enabled = true;
                else
                    supportedButDisabled = true;
            }
        }

        /// <summary>
        /// Returns a user-friendly single-line description for a renderer's instancing state.
        /// </summary>
        public static string GetRendererInstancingLabel(RendererEntry entry)
        {
            if (entry.IsSkinned || entry.IsParticleSystem)
                return "Instancing: Unsupported/Unknown";

            if (entry.InstancingEligible)
                return entry.InstancingEnabled ? "Instancing: Enabled" : "Instancing: Supported but Disabled";

            return "Instancing: Unsupported/Unknown";
        }

        /// <summary>
        /// Returns whether a renderer entry passes the provided instancing filter.
        /// </summary>
        public static bool PassesInstancingFilter(RendererEntry e, InstancingFilter filter)
        {
            switch (filter)
            {
                case InstancingFilter.All:
                    return true;
                case InstancingFilter.EligibleOnly:
                    return e.InstancingEligible;
                case InstancingFilter.EnabledOnly:
                    return e.InstancingEligible && e.InstancingEnabled;
                case InstancingFilter.SupportedButDisabledOnly:
                    return e.InstancingEligible && e.InstancingSupportedButDisabled;
                case InstancingFilter.UnsupportedOrUnknownOnly:
                    return !e.InstancingEligible;
                default:
                    return true;
            }
        }
    }
}