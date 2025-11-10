using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LegendaryTools.Editor
{
    internal sealed class GroupIndex
    {
        public readonly Dictionary<Material, List<RendererEntry>> ByMaterial = new();

        public readonly Dictionary<Shader, List<RendererEntry>> ByShader = new();

        public readonly Dictionary<Texture, List<RendererEntry>> ByTexture = new();

        public readonly Dictionary<ParticleSystem, List<RendererEntry>> ByParticleSystem = new();

        public readonly List<RendererEntry> NoMaterial = new();
        public readonly List<RendererEntry> NoShader = new();
        public readonly List<RendererEntry> NoTexture = new();
        public readonly List<RendererEntry> NoParticleSystem = new();

        public GroupIndex(IEnumerable<RendererEntry> entries)
        {
            foreach (RendererEntry e in entries)
            {
                // Material
                if (e.Materials == null || e.Materials.Length == 0)
                    NoMaterial.Add(e);
                else
                    foreach (Material m in e.Materials)
                    {
                        if (m == null) continue;
                        if (!ByMaterial.TryGetValue(m, out List<RendererEntry> list))
                            ByMaterial[m] = list = new List<RendererEntry>();
                        list.Add(e);
                    }

                // Shader
                if (e.Shaders == null || e.Shaders.Length == 0)
                    NoShader.Add(e);
                else
                    foreach (Shader s in e.Shaders)
                    {
                        if (s == null) continue;
                        if (!ByShader.TryGetValue(s, out List<RendererEntry> list))
                            ByShader[s] = list = new List<RendererEntry>();
                        list.Add(e);
                    }

                // Texture
                if (e.Textures == null || e.Textures.Length == 0)
                    NoTexture.Add(e);
                else
                    foreach (Texture t in e.Textures)
                    {
                        if (t == null) continue;
                        if (!ByTexture.TryGetValue(t, out List<RendererEntry> list))
                            ByTexture[t] = list = new List<RendererEntry>();
                        list.Add(e);
                    }

                // ParticleSystem
                if (e.IsParticleSystem)
                {
                    if (e.ParticleSystem == null)
                    {
                        NoParticleSystem.Add(e);
                    }
                    else
                    {
                        if (!ByParticleSystem.TryGetValue(e.ParticleSystem, out List<RendererEntry> list))
                            ByParticleSystem[e.ParticleSystem] = list = new List<RendererEntry>();
                        list.Add(e);
                    }
                }
            }
        }

        public static IEnumerable<KeyValuePair<T, List<RendererEntry>>> ApplyFilter<T>(
            Dictionary<T, List<RendererEntry>> dict,
            System.Func<RendererEntry, bool> predicate,
            System.Func<T, string> nameSelector)
            where T : Object
        {
            return dict
                .Select(kv => new KeyValuePair<T, List<RendererEntry>>(kv.Key, kv.Value.Where(predicate).ToList()))
                .Where(kv => kv.Value.Count > 0)
                .OrderBy(kv => nameSelector(kv.Key));
        }
    }
}