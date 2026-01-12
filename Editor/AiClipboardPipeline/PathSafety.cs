#if UNITY_EDITOR_WIN
using System;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;

namespace AiClipboardPipeline.Editor
{
    internal sealed class PathSafety
    {
        public string ToAbsolutePathStrict(string assetPath)
        {
            string p = (assetPath ?? string.Empty).Replace("\\", "/").Trim();

            if (!p.StartsWith("Assets/", StringComparison.Ordinal) &&
                !string.Equals(p, "Assets", StringComparison.Ordinal))
                throw new InvalidOperationException($"Invalid asset path: {assetPath}");

            string assetsAbs = Application.dataPath.Replace("\\", "/");
            string assetsAbsFull = Path.GetFullPath(assetsAbs);

            string rel = p.Length > "Assets".Length ? p.Substring("Assets".Length).TrimStart('/') : string.Empty;
            if (string.IsNullOrEmpty(rel))
                throw new InvalidOperationException("Cannot write to Assets root.");

            if (ContainsPathTraversal(rel))
                throw new InvalidOperationException($"Path traversal is not allowed: {assetPath}");

            string combined = Path.GetFullPath(Path.Combine(assetsAbsFull, rel));

            string prefix = assetsAbsFull.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                            Path.DirectorySeparatorChar;

            if (!combined.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Write blocked (outside Unity project Assets): {combined}");

            return combined.Replace("\\", "/");
        }

        public bool ContainsPathTraversal(string rel)
        {
            if (string.IsNullOrEmpty(rel))
                return false;

            string r = rel.Replace("\\", "/");

            if (r.StartsWith("../", StringComparison.Ordinal) || r.Contains("/../") ||
                r.EndsWith("/..", StringComparison.Ordinal))
                return true;

            string[] segs = r.Split('/');
            for (int i = 0; i < segs.Length; i++)
            {
                if (string.Equals(segs[i], "..", StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        public bool IsAbsoluteLike(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            string p = path.Replace("\\", "/").Trim();
            return p.StartsWith("/", StringComparison.Ordinal) || Regex.IsMatch(p, @"^[A-Za-z]:/");
        }
    }
}
#endif