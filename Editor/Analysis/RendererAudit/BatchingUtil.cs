using System;
using System.Collections.Generic;
using UnityEngine;

namespace LegendaryTools.Editor
{
    internal static class BatchingUtil
    {
        /// <summary>
        /// Evaluates Static and Dynamic batching eligibility for a renderer.
        /// Adds human-readable reasons when not eligible.
        /// Notes:
        /// - Dynamic batching constraints are approximated following Unity docs heuristics.
        /// - Static batching requires: MeshRenderer, mesh present, GameObject marked static.
        /// </summary>
        public static void Evaluate(Renderer r, Material[] uniqueMats,
            out bool staticEligible, out List<string> staticReasons,
            out bool dynamicEligible, out List<string> dynamicReasons)
        {
            staticReasons = new List<string>();
            dynamicReasons = new List<string>();

            // Common flags
            bool isSkinned = r is SkinnedMeshRenderer;
            bool isPSR = r is ParticleSystemRenderer;
            bool isMesh = !isSkinned && !isPSR;

            // ---------- Static batching ----------
            // Heuristic: MeshRenderer + mesh present + object marked static.
            if (!isMesh) staticReasons.Add(isSkinned ? "SkinnedMeshRenderer" : "ParticleSystemRenderer");

            Mesh mesh = null;
            if (isMesh)
            {
                MeshFilter mf = r.GetComponent<MeshFilter>();
                mesh = mf != null ? mf.sharedMesh : null;
                if (mesh == null) staticReasons.Add("No Mesh");
            }

            if (!r.gameObject.isStatic) staticReasons.Add("Not marked Static");

            staticEligible = staticReasons.Count == 0;

            // ---------- Dynamic batching ----------
            // Heuristics (approx based on Unity rules):
            // - MeshRenderer
            // - One material
            // - Uniform scale (no non-uniform)
            // - Vertex count under a limit proportional to attribute count (~900 attributes budget)
            // - Instancing disabled on the material
            // - Shader not multi-pass (best-effort)
            if (!isMesh) dynamicReasons.Add(isSkinned ? "SkinnedMeshRenderer" : "ParticleSystemRenderer");

            if (uniqueMats == null || uniqueMats.Length == 0)
                dynamicReasons.Add("No Material");
            else if (uniqueMats.Length > 1)
                dynamicReasons.Add("Multiple Materials");

            // Uniform scale check
            Vector3 ls = r.transform.lossyScale;
            if (!IsUniformScale(ls)) dynamicReasons.Add("Non-uniform scale");

            // Mesh + vertex/attribute budget
            if (mesh == null)
            {
                dynamicReasons.Add("No Mesh");
            }
            else
            {
                int attrCount = EstimateAttributeCount(mesh); // pos=1 + normals? + tangents? + color? + uv0? + uv1?
                int limit = Mathf.Max(1, Mathf.FloorToInt(900f / Mathf.Max(1, attrCount))); // ~Unity guideline
                if (mesh.vertexCount > limit) dynamicReasons.Add($"Too many vertices ({mesh.vertexCount}>{limit})");
            }

            // Instancing must be OFF for dynamic batching
            if (uniqueMats != null)
                foreach (Material m in uniqueMats)
                {
                    if (m == null) continue;
                    if (m.enableInstancing)
                    {
                        dynamicReasons.Add("Instancing enabled");
                        break;
                    }

                    // Shader multi-pass check (best-effort)
                    try
                    {
                        Shader sh = m.shader;
                        if (sh != null && sh.passCount > 1)
                        {
                            dynamicReasons.Add("Multi-pass shader");
                            break;
                        }
                    }
                    catch
                    {
                        /* ignore */
                    }

                    // Shader tag "DisableBatching" (if any)
                    try
                    {
                        string tag = m.GetTag("DisableBatching", false);
                        if (!string.IsNullOrEmpty(tag) &&
                            !string.Equals(tag, "False", StringComparison.OrdinalIgnoreCase))
                        {
                            dynamicReasons.Add($"DisableBatching tag: {tag}");
                            break;
                        }
                    }
                    catch
                    {
                        /* ignore */
                    }
                }

            dynamicEligible = dynamicReasons.Count == 0;
        }

        /// <summary>
        /// Returns true when scale is approximately uniform across axes.
        /// </summary>
        private static bool IsUniformScale(Vector3 s)
        {
            const float eps = 0.001f;
            return Mathf.Abs(s.x - s.y) < eps && Mathf.Abs(s.x - s.z) < eps;
        }

        /// <summary>
        /// Very rough attribute count estimate for dynamic batching budget (~900 attributes rule).
        /// Position counts as 1, then we add presence of normals, tangents, colors, uv0, uv1.
        /// </summary>
        private static int EstimateAttributeCount(Mesh mesh)
        {
            int count = 1; // position
            if (mesh == null) return count;
            if (mesh.normals != null && mesh.normals.Length == mesh.vertexCount) count++;
            if (mesh.tangents != null && mesh.tangents.Length == mesh.vertexCount) count++;
            if (mesh.colors != null && mesh.colors.Length == mesh.vertexCount) count++;
            if (mesh.uv != null && mesh.uv.Length == mesh.vertexCount) count++;
            if (mesh.uv2 != null && mesh.uv2.Length == mesh.vertexCount) count++;
            return count;
        }
    }
}