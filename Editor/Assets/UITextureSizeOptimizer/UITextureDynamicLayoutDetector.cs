using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace LegendaryTools.Editor
{
    internal sealed class UITextureDynamicLayoutDetector
    {
        private static readonly string[] DotweenSizeTokens =
        {
            "DOSizeDelta(",
            "DOScale(",
            "DOPunchScale(",
            "DOAnchorMin(",
            "DOAnchorMax(",
            "DOTween.To("
        };

        private static readonly Dictionary<string, bool> DotweenScriptCache =
            new(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<int, HashSet<UITextureUsageReason>> _reasonsByRectId = new();

        public UITextureDynamicLayoutDetector(GameObject root)
        {
            DetectAnimators(root);
            DetectLegacyAnimations(root);
            DetectDotweenAnimations(root);
            DetectDotweenCode(root);
        }

        public bool TryGetReasons(RectTransform rectTransform, out IReadOnlyCollection<UITextureUsageReason> reasons)
        {
            HashSet<UITextureUsageReason> combined = null;
            Transform current = rectTransform;
            while (current != null)
            {
                if (_reasonsByRectId.TryGetValue(current.GetInstanceID(), out HashSet<UITextureUsageReason> found))
                {
                    combined ??= new HashSet<UITextureUsageReason>();
                    combined.UnionWith(found);
                }

                current = current.parent;
            }

            if (combined != null)
            {
                reasons = combined;
                return true;
            }

            reasons = Array.Empty<UITextureUsageReason>();
            return false;
        }

        private void DetectAnimators(GameObject root)
        {
            foreach (Animator animator in root.GetComponentsInChildren<Animator>(true))
            {
                RuntimeAnimatorController controller = animator.runtimeAnimatorController;
                if (controller == null)
                {
                    continue;
                }

                foreach (AnimationClip clip in controller.animationClips.Where(clip => clip != null).Distinct())
                {
                    AddAnimationBindings(animator.transform, clip, UITextureUsageReason.Animator);
                }
            }
        }

        private void DetectLegacyAnimations(GameObject root)
        {
            foreach (Animation animation in root.GetComponentsInChildren<Animation>(true))
            {
                HashSet<AnimationClip> clips = new();
                if (animation.clip != null)
                {
                    clips.Add(animation.clip);
                }

                foreach (AnimationState state in animation)
                {
                    if (state?.clip != null)
                    {
                        clips.Add(state.clip);
                    }
                }

                foreach (AnimationClip clip in clips)
                {
                    AddAnimationBindings(animation.transform, clip, UITextureUsageReason.AnimationClip);
                }
            }
        }

        private void AddAnimationBindings(
            Transform animationRoot,
            AnimationClip clip,
            UITextureUsageReason reason)
        {
            foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
            {
                if (!IsRectTransformBinding(binding))
                {
                    continue;
                }

                Transform target = string.IsNullOrEmpty(binding.path)
                    ? animationRoot
                    : animationRoot.Find(binding.path);
                if (target is RectTransform rectTransform)
                {
                    AddReason(rectTransform, reason);
                }
            }
        }

        private static bool IsRectTransformBinding(EditorCurveBinding binding)
        {
            if (binding.type == typeof(RectTransform))
            {
                return true;
            }

            if (binding.type != typeof(Transform))
            {
                return false;
            }

            return binding.propertyName.StartsWith("m_LocalScale.", StringComparison.Ordinal) ||
                   binding.propertyName.StartsWith("localScale.", StringComparison.Ordinal);
        }

        private void DetectDotweenAnimations(GameObject root)
        {
            foreach (MonoBehaviour behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour == null ||
                    !string.Equals(behaviour.GetType().FullName, "DG.Tweening.DOTweenAnimation", StringComparison.Ordinal))
                {
                    continue;
                }

                RectTransform target = ResolveDotweenTarget(behaviour);
                if (target != null)
                {
                    AddReason(target, UITextureUsageReason.DotweenAnimation);
                }
            }
        }

        private static RectTransform ResolveDotweenTarget(MonoBehaviour behaviour)
        {
            SerializedObject serialized = new(behaviour);
            SerializedProperty targetProperty = serialized.FindProperty("target");
            UnityEngine.Object target = targetProperty?.objectReferenceValue;
            if (target is RectTransform rectTransform)
            {
                return rectTransform;
            }

            if (target is Component component)
            {
                return component.transform as RectTransform;
            }

            if (target is GameObject gameObject)
            {
                return gameObject.transform as RectTransform;
            }

            return behaviour.transform as RectTransform;
        }

        private void DetectDotweenCode(GameObject root)
        {
            foreach (MonoBehaviour behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour == null || !ScriptContainsDotweenSizing(behaviour))
                {
                    continue;
                }

                foreach (RectTransform rectTransform in behaviour.GetComponentsInChildren<RectTransform>(true))
                {
                    AddReason(rectTransform, UITextureUsageReason.DotweenCode);
                }
            }
        }

        private static bool ScriptContainsDotweenSizing(MonoBehaviour behaviour)
        {
            MonoScript script = MonoScript.FromMonoBehaviour(behaviour);
            string path = script != null ? AssetDatabase.GetAssetPath(script) : string.Empty;
            if (string.IsNullOrEmpty(path) || !path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (DotweenScriptCache.TryGetValue(path, out bool cached))
            {
                return cached;
            }

            bool contains = false;
            try
            {
                string source = File.ReadAllText(path);
                contains = DotweenSizeTokens.Any(token =>
                    source.IndexOf(token, StringComparison.Ordinal) >= 0);
            }
            catch
            {
                // An unreadable script should not fail the asset scan.
            }

            DotweenScriptCache[path] = contains;
            return contains;
        }

        private void AddReason(RectTransform rectTransform, UITextureUsageReason reason)
        {
            int instanceId = rectTransform.GetInstanceID();
            if (!_reasonsByRectId.TryGetValue(instanceId, out HashSet<UITextureUsageReason> reasons))
            {
                reasons = new HashSet<UITextureUsageReason>();
                _reasonsByRectId.Add(instanceId, reasons);
            }

            reasons.Add(reason);
        }
    }
}
