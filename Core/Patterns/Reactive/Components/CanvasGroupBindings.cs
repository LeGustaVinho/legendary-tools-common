using System;
using UnityEngine;
using LegendaryTools; // Observable<T>
using LegendaryTools.Reactive;

namespace LegendaryTools.Reactive.UGUI
{
    /// <summary>
    /// Extension methods to bind Observables with UnityEngine.CanvasGroup.
    /// Provides one-way bindings for alpha, interactable, blocksRaycasts, ignoreParentGroups and enabled.
    /// </summary>
    public static class CanvasGroupBindings
    {
        /// <summary>
        /// One-way binding: Observable{float} -> CanvasGroup.alpha.
        /// </summary>
        public static BindingHandle BindAlpha(
            this CanvasGroup group,
            Observable<float> alphaObservable,
            MonoBehaviour owner = null,
            BindingOptions options = null,
            bool clamp01 = true)
        {
            if (group == null) throw new ArgumentNullException(nameof(group));
            if (alphaObservable == null) throw new ArgumentNullException(nameof(alphaObservable));

            BindingAnchor anchor = owner != null
                ? owner.GetComponent<BindingAnchor>() ?? owner.gameObject.AddComponent<BindingAnchor>()
                : null;
            options ??= new BindingOptions();

            void Apply(float a)
            {
                if (anchor != null && !anchor.ShouldProcessNow()) return;
                group.alpha = clamp01 ? Mathf.Clamp01(a) : a;
            }

            void OnChanged(IObservable<float> _, float oldV, float newV)
            {
                Apply(newV);
            }

            void Subscribe()
            {
                Apply(alphaObservable.Value);
                alphaObservable.OnChanged += OnChanged;
            }

            void Unsubscribe()
            {
                alphaObservable.OnChanged -= OnChanged;
            }

            void Resync()
            {
                Apply(alphaObservable.Value);
            }

            BindingInfo info = new()
            {
                Kind = "CanvasGroup.Alpha",
                Direction = "ToUI",
                Description = "alpha ← Observable<float>",
                Target = group,
                Owner = owner,
                Anchor = anchor,
                Options = options,
                GetState = () => $"alpha={group.alpha:0.###}"
            };

            BindingHandle handle = new(Subscribe, Unsubscribe, Resync, anchor, options, info);
            if (owner != null)
            {
                BindingDisposer disposer = owner.GetComponent<BindingDisposer>() ??
                                           owner.gameObject.AddComponent<BindingDisposer>();
                disposer.Add(handle);
            }

            return handle;
        }

        /// <summary>
        /// One-way binding: Observable{bool} -> CanvasGroup.interactable.
        /// </summary>
        public static BindingHandle BindInteractable(
            this CanvasGroup group,
            Observable<bool> interactableObservable,
            MonoBehaviour owner = null,
            BindingOptions options = null)
        {
            if (group == null) throw new ArgumentNullException(nameof(group));
            if (interactableObservable == null) throw new ArgumentNullException(nameof(interactableObservable));

            BindingAnchor anchor = owner != null
                ? owner.GetComponent<BindingAnchor>() ?? owner.gameObject.AddComponent<BindingAnchor>()
                : null;
            options ??= new BindingOptions();

            void Apply(bool v)
            {
                if (anchor != null && !anchor.ShouldProcessNow()) return;
                group.interactable = v;
            }

            void OnChanged(IObservable<bool> _, bool oldV, bool newV)
            {
                Apply(newV);
            }

            void Subscribe()
            {
                Apply(interactableObservable.Value);
                interactableObservable.OnChanged += OnChanged;
            }

            void Unsubscribe()
            {
                interactableObservable.OnChanged -= OnChanged;
            }

            void Resync()
            {
                Apply(interactableObservable.Value);
            }

            BindingInfo info = new()
            {
                Kind = "CanvasGroup.Interactable",
                Direction = "ToUI",
                Description = "interactable ← Observable<bool>",
                Target = group,
                Owner = owner,
                Anchor = anchor,
                Options = options,
                GetState = () => $"interactable={group.interactable}"
            };

            BindingHandle handle = new(Subscribe, Unsubscribe, Resync, anchor, options, info);
            if (owner != null)
            {
                BindingDisposer disposer = owner.GetComponent<BindingDisposer>() ??
                                           owner.gameObject.AddComponent<BindingDisposer>();
                disposer.Add(handle);
            }

            return handle;
        }

        /// <summary>
        /// One-way binding: Observable{bool} -> CanvasGroup.blocksRaycasts.
        /// </summary>
        public static BindingHandle BindBlocksRaycasts(
            this CanvasGroup group,
            Observable<bool> blocksRaycastsObservable,
            MonoBehaviour owner = null,
            BindingOptions options = null)
        {
            if (group == null) throw new ArgumentNullException(nameof(group));
            if (blocksRaycastsObservable == null) throw new ArgumentNullException(nameof(blocksRaycastsObservable));

            BindingAnchor anchor = owner != null
                ? owner.GetComponent<BindingAnchor>() ?? owner.gameObject.AddComponent<BindingAnchor>()
                : null;
            options ??= new BindingOptions();

            void Apply(bool v)
            {
                if (anchor != null && !anchor.ShouldProcessNow()) return;
                group.blocksRaycasts = v;
            }

            void OnChanged(IObservable<bool> _, bool oldV, bool newV)
            {
                Apply(newV);
            }

            void Subscribe()
            {
                Apply(blocksRaycastsObservable.Value);
                blocksRaycastsObservable.OnChanged += OnChanged;
            }

            void Unsubscribe()
            {
                blocksRaycastsObservable.OnChanged -= OnChanged;
            }

            void Resync()
            {
                Apply(blocksRaycastsObservable.Value);
            }

            BindingInfo info = new()
            {
                Kind = "CanvasGroup.BlocksRaycasts",
                Direction = "ToUI",
                Description = "blocksRaycasts ← Observable<bool>",
                Target = group,
                Owner = owner,
                Anchor = anchor,
                Options = options,
                GetState = () => $"blocksRaycasts={group.blocksRaycasts}"
            };

            BindingHandle handle = new(Subscribe, Unsubscribe, Resync, anchor, options, info);
            if (owner != null)
            {
                BindingDisposer disposer = owner.GetComponent<BindingDisposer>() ??
                                           owner.gameObject.AddComponent<BindingDisposer>();
                disposer.Add(handle);
            }

            return handle;
        }

        /// <summary>
        /// One-way binding: Observable{bool} -> CanvasGroup.ignoreParentGroups.
        /// </summary>
        public static BindingHandle BindIgnoreParentGroups(
            this CanvasGroup group,
            Observable<bool> ignoreParentGroupsObservable,
            MonoBehaviour owner = null,
            BindingOptions options = null)
        {
            if (group == null) throw new ArgumentNullException(nameof(group));
            if (ignoreParentGroupsObservable == null)
                throw new ArgumentNullException(nameof(ignoreParentGroupsObservable));

            BindingAnchor anchor = owner != null
                ? owner.GetComponent<BindingAnchor>() ?? owner.gameObject.AddComponent<BindingAnchor>()
                : null;
            options ??= new BindingOptions();

            void Apply(bool v)
            {
                if (anchor != null && !anchor.ShouldProcessNow()) return;
                group.ignoreParentGroups = v;
            }

            void OnChanged(IObservable<bool> _, bool oldV, bool newV)
            {
                Apply(newV);
            }

            void Subscribe()
            {
                Apply(ignoreParentGroupsObservable.Value);
                ignoreParentGroupsObservable.OnChanged += OnChanged;
            }

            void Unsubscribe()
            {
                ignoreParentGroupsObservable.OnChanged -= OnChanged;
            }

            void Resync()
            {
                Apply(ignoreParentGroupsObservable.Value);
            }

            BindingInfo info = new()
            {
                Kind = "CanvasGroup.IgnoreParentGroups",
                Direction = "ToUI",
                Description = "ignoreParentGroups ← Observable<bool>",
                Target = group,
                Owner = owner,
                Anchor = anchor,
                Options = options,
                GetState = () => $"ignoreParentGroups={group.ignoreParentGroups}"
            };

            BindingHandle handle = new(Subscribe, Unsubscribe, Resync, anchor, options, info);
            if (owner != null)
            {
                BindingDisposer disposer = owner.GetComponent<BindingDisposer>() ??
                                           owner.gameObject.AddComponent<BindingDisposer>();
                disposer.Add(handle);
            }

            return handle;
        }

        /// <summary>
        /// One-way binding: Observable{bool} -> Behaviour.enabled (CanvasGroup).
        /// </summary>
        public static BindingHandle BindEnabled(
            this CanvasGroup group,
            Observable<bool> enabledObservable,
            MonoBehaviour owner = null,
            BindingOptions options = null)
        {
            if (group == null) throw new ArgumentNullException(nameof(group));
            if (enabledObservable == null) throw new ArgumentNullException(nameof(enabledObservable));

            BindingAnchor anchor = owner != null
                ? owner.GetComponent<BindingAnchor>() ?? owner.gameObject.AddComponent<BindingAnchor>()
                : null;
            options ??= new BindingOptions();

            void Apply(bool v)
            {
                if (anchor != null && !anchor.ShouldProcessNow()) return;
                group.enabled = v;
            }

            void OnChanged(IObservable<bool> _, bool oldV, bool newV)
            {
                Apply(newV);
            }

            void Subscribe()
            {
                Apply(enabledObservable.Value);
                enabledObservable.OnChanged += OnChanged;
            }

            void Unsubscribe()
            {
                enabledObservable.OnChanged -= OnChanged;
            }

            void Resync()
            {
                Apply(enabledObservable.Value);
            }

            BindingInfo info = new()
            {
                Kind = "CanvasGroup.Enabled",
                Direction = "ToUI",
                Description = "enabled ← Observable<bool>",
                Target = group,
                Owner = owner,
                Anchor = anchor,
                Options = options,
                GetState = () => $"enabled={group.enabled}"
            };

            BindingHandle handle = new(Subscribe, Unsubscribe, Resync, anchor, options, info);
            if (owner != null)
            {
                BindingDisposer disposer = owner.GetComponent<BindingDisposer>() ??
                                           owner.gameObject.AddComponent<BindingDisposer>();
                disposer.Add(handle);
            }

            return handle;
        }
    }
}