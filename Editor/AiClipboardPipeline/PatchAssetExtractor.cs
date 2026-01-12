#if UNITY_EDITOR_WIN
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace AiClipboardPipeline.Editor
{
    internal sealed class PatchAssetExtractor
    {
        private static readonly Regex DiffHeaderRegex =
            new(@"^diff --git a\/(.+?) b\/(.+?)\s*$", RegexOptions.Compiled | RegexOptions.Multiline);

        private static readonly Regex PlusPlusPlusRegex =
            new(@"^\+\+\+\s+(b\/|\/dev\/null)(.+?)\s*$", RegexOptions.Compiled | RegexOptions.Multiline);

        public bool TryExtractAffectedAssetsFromPatch(
            string patchText,
            PathSafety pathSafety,
            out List<string> assetPaths,
            out string error)
        {
            assetPaths = new List<string>(4);
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(patchText))
            {
                error = "Patch text is empty.";
                return false;
            }

            // Prefer diff headers.
            MatchCollection diffs = DiffHeaderRegex.Matches(patchText);
            for (int i = 0; i < diffs.Count; i++)
            {
                string bPath = diffs[i].Groups[2].Value.Trim();
                if (string.IsNullOrEmpty(bPath))
                    continue;

                if (string.Equals(bPath, "/dev/null", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!TryAddValidatedAssetPath(bPath, pathSafety, assetPaths, ref error))
                    return false;
            }

            // Fallback to +++ headers.
            if (assetPaths.Count == 0)
            {
                MatchCollection plus = PlusPlusPlusRegex.Matches(patchText);
                for (int i = 0; i < plus.Count; i++)
                {
                    string kind = (plus[i].Groups[1].Value ?? string.Empty).Trim();
                    string rest = (plus[i].Groups[2].Value ?? string.Empty).Trim();

                    if (string.Equals(kind, "/dev/null", StringComparison.OrdinalIgnoreCase))
                        continue;

                    string path = rest.TrimStart(' ', '\t').Trim();
                    if (string.IsNullOrEmpty(path))
                        continue;

                    if (!TryAddValidatedAssetPath(path, pathSafety, assetPaths, ref error))
                        return false;
                }
            }

            if (assetPaths.Count == 0)
            {
                error = "Patch does not contain any recognizable file headers (diff --git / +++ b/...).";
                return false;
            }

            // De-dupe.
            HashSet<string> seen = new(StringComparer.Ordinal);
            int write = 0;
            for (int i = 0; i < assetPaths.Count; i++)
            {
                string p = assetPaths[i];
                if (seen.Add(p))
                    assetPaths[write++] = p;
            }

            if (write != assetPaths.Count)
                assetPaths.RemoveRange(write, assetPaths.Count - write);

            return true;
        }

        private bool TryAddValidatedAssetPath(string rawPath, PathSafety pathSafety, List<string> list,
            ref string error)
        {
            if (string.IsNullOrEmpty(rawPath))
                return true;

            string p = rawPath.Replace("\\", "/").Trim();

            if (p.StartsWith("/", StringComparison.Ordinal) || Regex.IsMatch(p, @"^[A-Za-z]:/"))
            {
                error = "Absolute paths are not allowed in patches: " + rawPath;
                return false;
            }

            if (!p.StartsWith("Assets/", StringComparison.Ordinal))
            {
                error = "Patch touches a non-Assets path (blocked): " + p;
                return false;
            }

            string rel = p.Substring("Assets/".Length);
            if (string.IsNullOrEmpty(rel))
            {
                error = "Patch targets Assets root (blocked): " + p;
                return false;
            }

            if (pathSafety.ContainsPathTraversal(rel))
            {
                error = "Path traversal is not allowed in patches: " + p;
                return false;
            }

            // Strict validation (ensures still under Assets on disk).
            try
            {
                _ = pathSafety.ToAbsolutePathStrict(p);
            }
            catch (Exception ex)
            {
                error = "Patch path validation failed: " + p + "\n\n" + ex.Message;
                return false;
            }

            list.Add(p);
            return true;
        }
    }
}
#endif