using System;
using System.IO;
using LegendaryTools.Editor.Code.CSFilesAggregator.Pipeline;
using UnityEngine;

namespace LegendaryTools.Editor.Code.CSFilesAggregator.Services
{
    /// <summary>
    /// Unity implementation of <see cref="IPathService"/>.
    /// </summary>
    public sealed class UnityPathService : IPathService
    {
        private readonly string _assetsAbsolutePath;
        private readonly string _projectRootAbsolutePath;

        /// <inheritdoc />
        public string AssetsAbsolutePath => _assetsAbsolutePath;

        /// <summary>
        /// Creates a new instance.
        /// </summary>
        public UnityPathService()
        {
            _assetsAbsolutePath = NormalizeSeparators(Application.dataPath);
            _projectRootAbsolutePath =
                NormalizeSeparators(Directory.GetParent(_assetsAbsolutePath)?.FullName ?? _assetsAbsolutePath);
        }

        /// <inheritdoc />
        public string NormalizeToProjectDisplayPath(string anyPath)
        {
            if (string.IsNullOrWhiteSpace(anyPath)) return string.Empty;

            string normalized = NormalizeSeparators(anyPath);

            // If it already looks like a project-relative path.
            if (normalized.StartsWith("Assets/", StringComparison.Ordinal) ||
                normalized.Equals("Assets", StringComparison.Ordinal)) return normalized;

            // Convert absolute Assets path to "Assets/.."
            if (normalized.StartsWith(_assetsAbsolutePath, StringComparison.OrdinalIgnoreCase))
            {
                string suffix = normalized.Substring(_assetsAbsolutePath.Length);
                suffix = suffix.TrimStart('/');
                return string.IsNullOrEmpty(suffix) ? "Assets" : $"Assets/{suffix}";
            }

            // Convert absolute project root paths to project-relative (best effort).
            if (normalized.StartsWith(_projectRootAbsolutePath, StringComparison.OrdinalIgnoreCase))
            {
                string suffix = normalized.Substring(_projectRootAbsolutePath.Length);
                suffix = suffix.TrimStart('/');
                return string.IsNullOrEmpty(suffix) ? normalized : suffix;
            }

            return normalized;
        }

        /// <inheritdoc />
        public PathResolution Resolve(string inputPath)
        {
            if (string.IsNullOrWhiteSpace(inputPath))
                return new PathResolution(string.Empty, string.Empty, PathKind.Invalid);

            string display = NormalizeToProjectDisplayPath(inputPath);

            string absolute = display;

            if (display.StartsWith("Assets/", StringComparison.Ordinal) ||
                display.Equals("Assets", StringComparison.Ordinal))
            {
                string suffix = display.Equals("Assets", StringComparison.Ordinal)
                    ? string.Empty
                    : display.Substring("Assets".Length).TrimStart('/');
                absolute = string.IsNullOrEmpty(suffix)
                    ? _assetsAbsolutePath
                    : NormalizeSeparators(Path.Combine(_assetsAbsolutePath, suffix));
            }
            else if (!Path.IsPathRooted(display))
            {
                // Best effort: treat as project-root-relative.
                absolute = NormalizeSeparators(Path.Combine(_projectRootAbsolutePath, display));
            }
            else
            {
                absolute = NormalizeSeparators(display);
            }

            if (File.Exists(absolute)) return new PathResolution(absolute, display, PathKind.File);

            if (Directory.Exists(absolute)) return new PathResolution(absolute, display, PathKind.Folder);

            // Also consider absolute path exists but display differs.
            if (File.Exists(display))
                return new PathResolution(NormalizeSeparators(display), NormalizeToProjectDisplayPath(display),
                    PathKind.File);

            if (Directory.Exists(display))
                return new PathResolution(NormalizeSeparators(display), NormalizeToProjectDisplayPath(display),
                    PathKind.Folder);

            return new PathResolution(absolute, display, PathKind.Invalid);
        }

        private static string NormalizeSeparators(string path)
        {
            return (path ?? string.Empty).Replace('\\', '/');
        }
    }
}