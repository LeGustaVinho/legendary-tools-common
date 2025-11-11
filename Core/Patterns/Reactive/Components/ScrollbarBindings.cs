using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

namespace LegendaryTools.Reactive.UGUI
{
    /// <summary>
    /// Extension methods to bind Observables with UnityEngine.UI.Scrollbar.
    /// Provides bindings for value (two-way), size, numberOfSteps, direction, interactable and enabled.
    /// </summary>
    public static class ScrollbarBindings
    {
        /// <summary>
        /// Binds Scrollbar.value to an Observable{T}. Supports TwoWay/ToUI/FromUI.
        /// </summary>
        public static BindingHandle BindValue<T>(
            this Scrollbar scrollbar,
            Observable<T> observable,
            BindDirection direction = Reactive.BindDirection.ToUI,
            MonoBehaviour owner = null,
            Func<T, float> toUI = null,
            Func<float, T> fromUI = null,
            IFormatProvider formatProvider = null,
            BindingOptions options = null)
            where T : IEquatable<T>, IComparable<T>, IComparable, IConvertible
        {
            if (scrollbar == null) throw new ArgumentNullException(nameof(scrollbar));
            if (observable == null) throw new ArgumentNullException(nameof(observable));

            BindingAnchor anchor = owner != null
                ? owner.GetComponent<BindingAnchor>() ?? owner.gameObject.AddComponent<BindingAnchor>()
                : null;

            options ??= new BindingOptions();
            formatProvider ??= options.FormatProvider ?? CultureInfo.InvariantCulture;

            bool isUpdating = false;

            float ToFloat(T v)
            {
                if (toUI != null) return Mathf.Clamp01(toUI(v));
                try
                {
                    return Mathf.Clamp01(Convert.ToSingle(v, formatProvider));
                }
                catch
                {
                    return scrollbar.value;
                }
            }

            T FromFloat(float f)
            {
                if (fromUI != null) return fromUI(Mathf.Clamp01(f));
                try
                {
                    return (T)Convert.ChangeType(Mathf.Clamp01(f), typeof(T), formatProvider);
                }
                catch
                {
                    return observable.Value;
                }
            }

            void ApplyToUI(T v)
            {
                if (anchor != null && !anchor.ShouldProcessNow()) return;
                try
                {
                    isUpdating = true;
                    scrollbar.SetValueWithoutNotify(ToFloat(v));
                }
                finally
                {
                    isUpdating = false;
                }
            }

            void OnObsChanged(IObservable<T> _, T oldV, T newV)
            {
                if (direction == Reactive.BindDirection.FromUI) return;
                ApplyToUI(newV);
            }

            void OnUIChanged(float val)
            {
                if (direction == Reactive.BindDirection.ToUI) return;
                if (isUpdating) return;
                T parsed = FromFloat(val);
                observable.Value = parsed;
            }

            void Subscribe()
            {
                if (direction != Reactive.BindDirection.FromUI)
                    ApplyToUI(observable.Value);

                observable.OnChanged += OnObsChanged;
                scrollbar.onValueChanged.AddListener(OnUIChanged);
            }

            void Unsubscribe()
            {
                observable.OnChanged -= OnObsChanged;
                scrollbar.onValueChanged.RemoveListener(OnUIChanged);
            }

            void Resync()
            {
                if (direction != Reactive.BindDirection.FromUI)
                    ApplyToUI(observable.Value);
            }

            BindingInfo info = new()
            {
                Kind = "Scrollbar.Value",
                Direction = direction.ToString(),
                Description = $"value ↔ Observable<{typeof(T).Name}> (0..1)",
                Target = scrollbar,
                Owner = owner,
                Anchor = anchor,
                Options = options,
                GetState = () =>
                    $"value={scrollbar.value:0.###}, size={scrollbar.size:0.###}, steps={scrollbar.numberOfSteps}, dir={scrollbar.direction}",
                Tags = new[] { "Scrollbar", "Value" }
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
        /// One-way binding: Observable{float} -> Scrollbar.size (0..1).
        /// </summary>
        public static BindingHandle BindSize(
            this Scrollbar scrollbar,
            Observable<float> sizeObservable,
            MonoBehaviour owner = null,
            BindingOptions options = null,
            bool clamp01 = true)
        {
            if (scrollbar == null) throw new ArgumentNullException(nameof(scrollbar));
            if (sizeObservable == null) throw new ArgumentNullException(nameof(sizeObservable));

            BindingAnchor anchor = owner != null
                ? owner.GetComponent<BindingAnchor>() ?? owner.gameObject.AddComponent<BindingAnchor>()
                : null;
            options ??= new BindingOptions();

            void Apply(float v)
            {
                if (anchor != null && !anchor.ShouldProcessNow()) return;
                scrollbar.size = clamp01 ? Mathf.Clamp01(v) : v;
            }

            void OnObsChanged(IObservable<float> _, float oldV, float newV)
            {
                Apply(newV);
            }

            void Subscribe()
            {
                Apply(sizeObservable.Value);
                sizeObservable.OnChanged += OnObsChanged;
            }

            void Unsubscribe()
            {
                sizeObservable.OnChanged -= OnObsChanged;
            }

            void Resync()
            {
                Apply(sizeObservable.Value);
            }

            BindingInfo info = new()
            {
                Kind = "Scrollbar.Size",
                Direction = "ToUI",
                Description = "size ← Observable<float> (0..1)",
                Target = scrollbar,
                Owner = owner,
                Anchor = anchor,
                Options = options,
                GetState = () => $"size={scrollbar.size:0.###}",
                Tags = new[] { "Scrollbar", "Size" }
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
        /// One-way binding: Observable{int} -> Scrollbar.numberOfSteps (>=0).
        /// </summary>
        public static BindingHandle BindNumberOfSteps(
            this Scrollbar scrollbar,
            Observable<int> stepsObservable,
            MonoBehaviour owner = null,
            BindingOptions options = null)
        {
            if (scrollbar == null) throw new ArgumentNullException(nameof(scrollbar));
            if (stepsObservable == null) throw new ArgumentNullException(nameof(stepsObservable));

            BindingAnchor anchor = owner != null
                ? owner.GetComponent<BindingAnchor>() ?? owner.gameObject.AddComponent<BindingAnchor>()
                : null;
            options ??= new BindingOptions();

            void Apply(int steps)
            {
                if (anchor != null && !anchor.ShouldProcessNow()) return;
                scrollbar.numberOfSteps = Mathf.Max(0, steps);
            }

            void OnObsChanged(IObservable<int> _, int oldV, int newV)
            {
                Apply(newV);
            }

            void Subscribe()
            {
                Apply(stepsObservable.Value);
                stepsObservable.OnChanged += OnObsChanged;
            }

            void Unsubscribe()
            {
                stepsObservable.OnChanged -= OnObsChanged;
            }

            void Resync()
            {
                Apply(stepsObservable.Value);
            }

            BindingInfo info = new()
            {
                Kind = "Scrollbar.NumberOfSteps",
                Direction = "ToUI",
                Description = "numberOfSteps ← Observable<int> (0 = continuous)",
                Target = scrollbar,
                Owner = owner,
                Anchor = anchor,
                Options = options,
                GetState = () => $"steps={scrollbar.numberOfSteps}",
                Tags = new[] { "Scrollbar", "Steps" }
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
        /// One-way binding: Observable{Scrollbar.Direction} -> Scrollbar.direction.
        /// </summary>
        public static BindingHandle BindDirection(
            this Scrollbar scrollbar,
            Observable<Scrollbar.Direction> directionObservable,
            MonoBehaviour owner = null,
            BindingOptions options = null)
        {
            if (scrollbar == null) throw new ArgumentNullException(nameof(scrollbar));
            if (directionObservable == null) throw new ArgumentNullException(nameof(directionObservable));

            BindingAnchor anchor = owner != null
                ? owner.GetComponent<BindingAnchor>() ?? owner.gameObject.AddComponent<BindingAnchor>()
                : null;
            options ??= new BindingOptions();

            void Apply(Scrollbar.Direction d)
            {
                if (anchor != null && !anchor.ShouldProcessNow()) return;
                scrollbar.direction = d;
            }

            void OnObsChanged(IObservable<Scrollbar.Direction> _, Scrollbar.Direction oldV, Scrollbar.Direction newV)
            {
                Apply(newV);
            }

            void Subscribe()
            {
                Apply(directionObservable.Value);
                directionObservable.OnChanged += OnObsChanged;
            }

            void Unsubscribe()
            {
                directionObservable.OnChanged -= OnObsChanged;
            }

            void Resync()
            {
                Apply(directionObservable.Value);
            }

            BindingInfo info = new()
            {
                Kind = "Scrollbar.Direction",
                Direction = "ToUI",
                Description = "direction ← Observable<Scrollbar.Direction>",
                Target = scrollbar,
                Owner = owner,
                Anchor = anchor,
                Options = options,
                GetState = () => $"direction={scrollbar.direction}",
                Tags = new[] { "Scrollbar", "Direction" }
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
        /// One-way binding: Observable{bool} -> Scrollbar.interactable.
        /// </summary>
        public static BindingHandle BindInteractable(
            this Scrollbar scrollbar,
            Observable<bool> interactableObservable,
            MonoBehaviour owner = null,
            BindingOptions options = null)
        {
            if (scrollbar == null) throw new ArgumentNullException(nameof(scrollbar));
            if (interactableObservable == null) throw new ArgumentNullException(nameof(interactableObservable));

            BindingAnchor anchor = owner != null
                ? owner.GetComponent<BindingAnchor>() ?? owner.gameObject.AddComponent<BindingAnchor>()
                : null;
            options ??= new BindingOptions();

            void Apply(bool v)
            {
                if (anchor != null && !anchor.ShouldProcessNow()) return;
                scrollbar.interactable = v;
            }

            void OnObsChanged(IObservable<bool> _, bool oldV, bool newV)
            {
                Apply(newV);
            }

            void Subscribe()
            {
                Apply(interactableObservable.Value);
                interactableObservable.OnChanged += OnObsChanged;
            }

            void Unsubscribe()
            {
                interactableObservable.OnChanged -= OnObsChanged;
            }

            void Resync()
            {
                Apply(interactableObservable.Value);
            }

            BindingInfo info = new()
            {
                Kind = "Scrollbar.Interactable",
                Direction = "ToUI",
                Description = "interactable ← Observable<bool>",
                Target = scrollbar,
                Owner = owner,
                Anchor = anchor,
                Options = options,
                GetState = () => $"interactable={scrollbar.interactable}",
                Tags = new[] { "Scrollbar", "Interactable" }
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
        /// One-way binding: Observable{bool} -> Behaviour.enabled (Scrollbar).
        /// </summary>
        public static BindingHandle BindEnabled(
            this Scrollbar scrollbar,
            Observable<bool> enabledObservable,
            MonoBehaviour owner = null,
            BindingOptions options = null)
        {
            if (scrollbar == null) throw new ArgumentNullException(nameof(scrollbar));
            if (enabledObservable == null) throw new ArgumentNullException(nameof(enabledObservable));

            BindingAnchor anchor = owner != null
                ? owner.GetComponent<BindingAnchor>() ?? owner.gameObject.AddComponent<BindingAnchor>()
                : null;
            options ??= new BindingOptions();

            void Apply(bool v)
            {
                if (anchor != null && !anchor.ShouldProcessNow()) return;
                scrollbar.enabled = v;
            }

            void OnObsChanged(IObservable<bool> _, bool oldV, bool newV)
            {
                Apply(newV);
            }

            void Subscribe()
            {
                Apply(enabledObservable.Value);
                enabledObservable.OnChanged += OnObsChanged;
            }

            void Unsubscribe()
            {
                enabledObservable.OnChanged -= OnObsChanged;
            }

            void Resync()
            {
                Apply(enabledObservable.Value);
            }

            BindingInfo info = new()
            {
                Kind = "Scrollbar.Enabled",
                Direction = "ToUI",
                Description = "enabled ← Observable<bool>",
                Target = scrollbar,
                Owner = owner,
                Anchor = anchor,
                Options = options,
                GetState = () => $"enabled={scrollbar.enabled}",
                Tags = new[] { "Scrollbar", "Enabled" }
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