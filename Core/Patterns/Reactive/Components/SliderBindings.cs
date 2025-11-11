using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;
using LegendaryTools; // Observable<T>
using LegendaryTools.Reactive;

namespace LegendaryTools.Reactive.UGUI
{
    /// <summary>
    /// Extension methods to bind Observables with UnityEngine.UI.Slider.
    /// Provides bindings for value (two-way), interactable, enabled, min/max and wholeNumbers.
    /// </summary>
    public static class SliderBindings
    {
        /// <summary>
        /// Binds Slider.value to an Observable{T}. Supports TwoWay/ToUI/FromUI.
        /// </summary>
        public static BindingHandle BindValue<T>(
            this Slider slider,
            Observable<T> observable,
            BindDirection direction = BindDirection.TwoWay,
            MonoBehaviour owner = null,
            Func<T, float> toUI = null,
            Func<float, T> fromUI = null,
            IFormatProvider formatProvider = null,
            BindingOptions options = null)
            where T : IEquatable<T>, IComparable<T>, IComparable, IConvertible
        {
            if (slider == null) throw new ArgumentNullException(nameof(slider));
            if (observable == null) throw new ArgumentNullException(nameof(observable));

            BindingAnchor anchor = owner != null
                ? owner.GetComponent<BindingAnchor>() ?? owner.gameObject.AddComponent<BindingAnchor>()
                : null;

            options ??= new BindingOptions();
            formatProvider ??= options.FormatProvider ?? CultureInfo.InvariantCulture;

            bool isUpdating = false;

            float ToFloat(T v)
            {
                if (toUI != null) return toUI(v);
                try
                {
                    return Convert.ToSingle(v, formatProvider);
                }
                catch
                {
                    return slider.value;
                }
            }

            T FromFloat(float f)
            {
                if (fromUI != null) return fromUI(f);
                try
                {
                    return (T)Convert.ChangeType(f, typeof(T), formatProvider);
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
                    slider.SetValueWithoutNotify(ToFloat(v));
                }
                finally
                {
                    isUpdating = false;
                }
            }

            void OnObsChanged(IObservable<T> _, T oldV, T newV)
            {
                if (direction == BindDirection.FromUI) return;
                ApplyToUI(newV);
            }

            void OnUIChanged(float val)
            {
                if (direction == BindDirection.ToUI) return;
                if (isUpdating) return;
                T parsed = FromFloat(val);
                observable.Value = parsed;
            }

            void Subscribe()
            {
                if (direction != BindDirection.FromUI)
                    ApplyToUI(observable.Value);

                observable.OnChanged += OnObsChanged;
                slider.onValueChanged.AddListener(OnUIChanged);
            }

            void Unsubscribe()
            {
                observable.OnChanged -= OnObsChanged;
                slider.onValueChanged.RemoveListener(OnUIChanged);
            }

            void Resync()
            {
                if (direction != BindDirection.FromUI)
                    ApplyToUI(observable.Value);
            }

            BindingInfo info = new()
            {
                Kind = "Slider.Value",
                Direction = direction.ToString(),
                Description = $"value ↔ Observable<{typeof(T).Name}>",
                Target = slider,
                Owner = owner,
                Anchor = anchor,
                Options = options,
                GetState = () =>
                    $"value={slider.value:0.###}, min={slider.minValue:0.###}, max={slider.maxValue:0.###}, wholeNumbers={slider.wholeNumbers}",
                Tags = new[] { "Slider", "Value" }
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
        /// One-way binding: Observable{bool} -> Slider.interactable.
        /// </summary>
        public static BindingHandle BindInteractable(
            this Slider slider,
            Observable<bool> interactableObservable,
            MonoBehaviour owner = null,
            BindingOptions options = null)
        {
            if (slider == null) throw new ArgumentNullException(nameof(slider));
            if (interactableObservable == null) throw new ArgumentNullException(nameof(interactableObservable));

            BindingAnchor anchor = owner != null
                ? owner.GetComponent<BindingAnchor>() ?? owner.gameObject.AddComponent<BindingAnchor>()
                : null;

            options ??= new BindingOptions();

            void Apply(bool v)
            {
                if (anchor != null && !anchor.ShouldProcessNow()) return;
                slider.interactable = v;
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
                Kind = "Slider.Interactable",
                Direction = "ToUI",
                Description = "interactable ← Observable<bool>",
                Target = slider,
                Owner = owner,
                Anchor = anchor,
                Options = options,
                GetState = () => $"interactable={slider.interactable}",
                Tags = new[] { "Slider", "Interactable" }
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
        /// One-way binding: Observable{bool} -> Behaviour.enabled (Slider).
        /// </summary>
        public static BindingHandle BindEnabled(
            this Slider slider,
            Observable<bool> enabledObservable,
            MonoBehaviour owner = null,
            BindingOptions options = null)
        {
            if (slider == null) throw new ArgumentNullException(nameof(slider));
            if (enabledObservable == null) throw new ArgumentNullException(nameof(enabledObservable));

            BindingAnchor anchor = owner != null
                ? owner.GetComponent<BindingAnchor>() ?? owner.gameObject.AddComponent<BindingAnchor>()
                : null;

            options ??= new BindingOptions();

            void Apply(bool v)
            {
                if (anchor != null && !anchor.ShouldProcessNow()) return;
                slider.enabled = v;
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
                Kind = "Slider.Enabled",
                Direction = "ToUI",
                Description = "enabled ← Observable<bool>",
                Target = slider,
                Owner = owner,
                Anchor = anchor,
                Options = options,
                GetState = () => $"enabled={slider.enabled}",
                Tags = new[] { "Slider", "Enabled" }
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
        /// One-way binding: Observable{float} -> Slider.minValue.
        /// </summary>
        public static BindingHandle BindMinValue(
            this Slider slider,
            Observable<float> minValueObservable,
            MonoBehaviour owner = null,
            BindingOptions options = null)
        {
            if (slider == null) throw new ArgumentNullException(nameof(slider));
            if (minValueObservable == null) throw new ArgumentNullException(nameof(minValueObservable));

            BindingAnchor anchor = owner != null
                ? owner.GetComponent<BindingAnchor>() ?? owner.gameObject.AddComponent<BindingAnchor>()
                : null;

            options ??= new BindingOptions();

            void Apply(float v)
            {
                if (anchor != null && !anchor.ShouldProcessNow()) return;
                slider.minValue = v;
                if (slider.value < slider.minValue) slider.SetValueWithoutNotify(slider.minValue);
            }

            void OnObsChanged(IObservable<float> _, float oldV, float newV)
            {
                Apply(newV);
            }

            void Subscribe()
            {
                Apply(minValueObservable.Value);
                minValueObservable.OnChanged += OnObsChanged;
            }

            void Unsubscribe()
            {
                minValueObservable.OnChanged -= OnObsChanged;
            }

            void Resync()
            {
                Apply(minValueObservable.Value);
            }

            BindingInfo info = new()
            {
                Kind = "Slider.MinValue",
                Direction = "ToUI",
                Description = "minValue ← Observable<float>",
                Target = slider,
                Owner = owner,
                Anchor = anchor,
                Options = options,
                GetState = () => $"minValue={slider.minValue:0.###}",
                Tags = new[] { "Slider", "MinValue" }
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
        /// One-way binding: Observable{float} -> Slider.maxValue.
        /// </summary>
        public static BindingHandle BindMaxValue(
            this Slider slider,
            Observable<float> maxValueObservable,
            MonoBehaviour owner = null,
            BindingOptions options = null)
        {
            if (slider == null) throw new ArgumentNullException(nameof(slider));
            if (maxValueObservable == null) throw new ArgumentNullException(nameof(maxValueObservable));

            BindingAnchor anchor = owner != null
                ? owner.GetComponent<BindingAnchor>() ?? owner.gameObject.AddComponent<BindingAnchor>()
                : null;

            options ??= new BindingOptions();

            void Apply(float v)
            {
                if (anchor != null && !anchor.ShouldProcessNow()) return;
                slider.maxValue = v;
                if (slider.value > slider.maxValue) slider.SetValueWithoutNotify(slider.maxValue);
            }

            void OnObsChanged(IObservable<float> _, float oldV, float newV)
            {
                Apply(newV);
            }

            void Subscribe()
            {
                Apply(maxValueObservable.Value);
                maxValueObservable.OnChanged += OnObsChanged;
            }

            void Unsubscribe()
            {
                maxValueObservable.OnChanged -= OnObsChanged;
            }

            void Resync()
            {
                Apply(maxValueObservable.Value);
            }

            BindingInfo info = new()
            {
                Kind = "Slider.MaxValue",
                Direction = "ToUI",
                Description = "maxValue ← Observable<float>",
                Target = slider,
                Owner = owner,
                Anchor = anchor,
                Options = options,
                GetState = () => $"maxValue={slider.maxValue:0.###}",
                Tags = new[] { "Slider", "MaxValue" }
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
        /// One-way binding: Observable{bool} -> Slider.wholeNumbers.
        /// </summary>
        public static BindingHandle BindWholeNumbers(
            this Slider slider,
            Observable<bool> wholeNumbersObservable,
            MonoBehaviour owner = null,
            BindingOptions options = null)
        {
            if (slider == null) throw new ArgumentNullException(nameof(slider));
            if (wholeNumbersObservable == null) throw new ArgumentNullException(nameof(wholeNumbersObservable));

            BindingAnchor anchor = owner != null
                ? owner.GetComponent<BindingAnchor>() ?? owner.gameObject.AddComponent<BindingAnchor>()
                : null;

            options ??= new BindingOptions();

            void Apply(bool v)
            {
                if (anchor != null && !anchor.ShouldProcessNow()) return;
                slider.wholeNumbers = v;
                if (v) slider.SetValueWithoutNotify(Mathf.Round(slider.value));
            }

            void OnObsChanged(IObservable<bool> _, bool oldV, bool newV)
            {
                Apply(newV);
            }

            void Subscribe()
            {
                Apply(wholeNumbersObservable.Value);
                wholeNumbersObservable.OnChanged += OnObsChanged;
            }

            void Unsubscribe()
            {
                wholeNumbersObservable.OnChanged -= OnObsChanged;
            }

            void Resync()
            {
                Apply(wholeNumbersObservable.Value);
            }

            BindingInfo info = new()
            {
                Kind = "Slider.WholeNumbers",
                Direction = "ToUI",
                Description = "wholeNumbers ← Observable<bool>",
                Target = slider,
                Owner = owner,
                Anchor = anchor,
                Options = options,
                GetState = () => $"wholeNumbers={slider.wholeNumbers}",
                Tags = new[] { "Slider", "WholeNumbers" }
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