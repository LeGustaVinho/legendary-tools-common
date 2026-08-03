using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.UI;

namespace LegendaryTools.Editor
{
    internal enum UITextureConfidence
    {
        Safe,
        Estimated,
        Risky,
        Unsupported
    }

    internal enum UITextureUsageReason
    {
        Sliced,
        Tiled,
        RawImageUv,
        MissingCanvas,
        SyntheticCanvas,
        WorldSpaceCanvas,
        AnimationClip,
        Animator,
        DotweenAnimation,
        DotweenCode
    }

    internal enum UITextureRoundingMode
    {
        Up,
        Down
    }

    [Serializable]
    internal sealed class UITextureUsageRecord
    {
        public string TexturePath;
        public string AssetKind;
        public string ContainerPath;
        public string HierarchyPath;
        public string ComponentType;
        public string SpriteName;
        public Vector2 RenderedPixels;
        public int RequiredMaxSize;
        public UITextureConfidence Confidence;
        public List<UITextureUsageReason> Reasons = new();
        public string Warning;
    }

    internal sealed class UITextureOptimizationResult
    {
        public string AssetPath;
        public string AssetName;
        public string AssetKind;
        public int SourceWidth;
        public int SourceHeight;
        public int ImportedWidth;
        public int ImportedHeight;
        public int CurrentMaxSize;
        public int RecommendedMaxSize;
        public long CurrentMemoryBytes;
        public bool IsEditable;
        public string ImporterWarning;
        public readonly List<UITextureUsageRecord> Usages = new();

        public UITextureConfidence Confidence => Usages.Count == 0
            ? UITextureConfidence.Safe
            : Usages.Max(usage => usage.Confidence);

        public bool HasBlockingUsage => Confidence != UITextureConfidence.Safe;

        public bool CanApply =>
            IsEditable &&
            RecommendedMaxSize > 0 &&
            RecommendedMaxSize < CurrentMaxSize &&
            Mathf.Max(SourceWidth, SourceHeight) > RecommendedMaxSize;

        public bool IsCandidate => CanApply && Confidence == UITextureConfidence.Safe;

        public float EstimatedAreaReduction
        {
            get
            {
                int currentLargest = Mathf.Max(1, Mathf.Max(ImportedWidth, ImportedHeight));
                float scale = Mathf.Min(1f, RecommendedMaxSize / (float)currentLargest);
                return 1f - scale * scale;
            }
        }

        public long EstimatedMemorySaving =>
            RecommendedMaxSize <= 0 ? 0L : (long)(CurrentMemoryBytes * EstimatedAreaReduction);

        public Vector2 LargestRenderedUse
        {
            get
            {
                Vector2 largest = Vector2.zero;
                float largestArea = -1f;
                foreach (UITextureUsageRecord usage in Usages)
                {
                    float area = usage.RenderedPixels.x * usage.RenderedPixels.y;
                    if (area <= largestArea)
                    {
                        continue;
                    }

                    largestArea = area;
                    largest = usage.RenderedPixels;
                }

                return largest;
            }
        }

        public void AddUsage(UITextureUsageRecord usage)
        {
            Usages.Add(usage);
            RecommendedMaxSize = Mathf.Max(RecommendedMaxSize, usage.RequiredMaxSize);
        }

        public string GetReasonSummary()
        {
            IEnumerable<IGrouping<UITextureUsageReason, UITextureUsageReason>> groups = Usages
                .SelectMany(usage => (usage.Reasons ?? new List<UITextureUsageReason>()).Distinct())
                .GroupBy(reason => reason)
                .OrderByDescending(group => group.Count())
                .ThenBy(group => group.Key.ToString(), StringComparer.Ordinal);
            return string.Join(" + ", groups.Select(group => $"{group.Count()} {FormatReason(group.Key)}"));
        }

        public static string FormatReason(UITextureUsageReason reason)
        {
            return reason switch
            {
                UITextureUsageReason.RawImageUv => "RawImage UV",
                UITextureUsageReason.MissingCanvas => "Missing Canvas",
                UITextureUsageReason.SyntheticCanvas => "Synthetic Canvas",
                UITextureUsageReason.WorldSpaceCanvas => "World Space",
                UITextureUsageReason.AnimationClip => "Animation",
                UITextureUsageReason.DotweenAnimation => "DOTweenAnimation",
                UITextureUsageReason.DotweenCode => "DOTween code",
                _ => reason.ToString()
            };
        }

        public void RefreshImporterState()
        {
            TextureImporter importer = AssetImporter.GetAtPath(AssetPath) as TextureImporter;
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetPath);
            if (importer == null || texture == null)
            {
                IsEditable = false;
                ImporterWarning = "TextureImporter or imported Texture2D is unavailable.";
                return;
            }

            CurrentMaxSize = importer.maxTextureSize;
            ImportedWidth = texture.width;
            ImportedHeight = texture.height;
            CurrentMemoryBytes = Profiler.GetRuntimeMemorySizeLong(texture);
            IsEditable = AssetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) &&
                         AssetDatabase.IsOpenForEdit(AssetPath, StatusQueryOptions.UseCachedIfPossible);
        }
    }

    internal static class UITextureSizeCalculator
    {
        private const int MinimumImporterMaxSize = 32;

        public static float CalculateCanvasScaleFactor(CanvasScaler scaler, Vector2 simulatedScreenSize)
        {
            if (scaler == null)
            {
                return 1f;
            }

            switch (scaler.uiScaleMode)
            {
                case CanvasScaler.ScaleMode.ConstantPixelSize:
                    return Mathf.Max(0.01f, scaler.scaleFactor);

                case CanvasScaler.ScaleMode.ScaleWithScreenSize:
                {
                    Vector2 reference = scaler.referenceResolution;
                    float widthRatio = simulatedScreenSize.x / Mathf.Max(1f, reference.x);
                    float heightRatio = simulatedScreenSize.y / Mathf.Max(1f, reference.y);

                    switch (scaler.screenMatchMode)
                    {
                        case CanvasScaler.ScreenMatchMode.Expand:
                            return Mathf.Min(widthRatio, heightRatio);
                        case CanvasScaler.ScreenMatchMode.Shrink:
                            return Mathf.Max(widthRatio, heightRatio);
                        default:
                            float logWidth = Mathf.Log(widthRatio, 2f);
                            float logHeight = Mathf.Log(heightRatio, 2f);
                            return Mathf.Pow(2f, Mathf.Lerp(logWidth, logHeight, scaler.matchWidthOrHeight));
                    }
                }

                case CanvasScaler.ScaleMode.ConstantPhysicalSize:
                {
                    float dpi = Mathf.Max(1f, scaler.fallbackScreenDPI);
                    float unitsPerInch = scaler.physicalUnit switch
                    {
                        CanvasScaler.Unit.Centimeters => 2.54f,
                        CanvasScaler.Unit.Millimeters => 25.4f,
                        CanvasScaler.Unit.Points => 72f,
                        CanvasScaler.Unit.Picas => 6f,
                        _ => 1f
                    };
                    return dpi / unitsPerInch;
                }

                default:
                    return 1f;
            }
        }

        public static Vector2 ApplyPreserveAspect(Vector2 rectPixels, float contentAspect)
        {
            if (rectPixels.x <= 0f || rectPixels.y <= 0f || contentAspect <= 0f)
            {
                return rectPixels;
            }

            float rectAspect = rectPixels.x / rectPixels.y;
            if (contentAspect > rectAspect)
            {
                rectPixels.y = rectPixels.x / contentAspect;
            }
            else
            {
                rectPixels.x = rectPixels.y * contentAspect;
            }

            return rectPixels;
        }

        public static int CalculateRequiredMaxSize(
            Vector2 renderedPixels,
            Vector2 importedSamplePixels,
            Vector2Int sourceTextureSize,
            int currentMaxSize,
            UITextureRoundingMode roundingMode = UITextureRoundingMode.Up)
        {
            if (renderedPixels.x <= 0f || renderedPixels.y <= 0f ||
                importedSamplePixels.x <= 0f || importedSamplePixels.y <= 0f ||
                sourceTextureSize.x <= 0 || sourceTextureSize.y <= 0)
            {
                return currentMaxSize;
            }

            float requiredScale = Mathf.Max(
                renderedPixels.x / importedSamplePixels.x,
                renderedPixels.y / importedSamplePixels.y);
            requiredScale = Mathf.Clamp01(requiredScale);

            float requiredLargestAxis = Mathf.Max(sourceTextureSize.x, sourceTextureSize.y) * requiredScale;
            int requiredPixels = Mathf.Max(1, Mathf.CeilToInt(requiredLargestAxis));
            int nextPower = Mathf.NextPowerOfTwo(requiredPixels);
            int rounded = roundingMode == UITextureRoundingMode.Down && nextPower > requiredPixels
                ? Mathf.Max(MinimumImporterMaxSize, nextPower / 2)
                : nextPower;
            return Mathf.Clamp(rounded, MinimumImporterMaxSize, Mathf.Max(MinimumImporterMaxSize, currentMaxSize));
        }

        public static Vector2 MeasureRenderedPixels(RectTransform rectTransform, Canvas rootCanvas, float scaleFactor)
        {
            Vector3[] corners = new Vector3[4];
            rectTransform.GetWorldCorners(corners);

            if (rootCanvas != null)
            {
                Transform canvasTransform = rootCanvas.transform;
                for (int i = 0; i < corners.Length; i++)
                {
                    corners[i] = canvasTransform.InverseTransformPoint(corners[i]);
                }
            }

            float width = Vector3.Distance(corners[0], corners[3]) * scaleFactor;
            float height = Vector3.Distance(corners[0], corners[1]) * scaleFactor;
            return new Vector2(Mathf.Abs(width), Mathf.Abs(height));
        }
    }
}
