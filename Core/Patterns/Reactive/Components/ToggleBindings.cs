using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;
using LegendaryTools; // Observable<T>
using LegendaryTools.Reactive;

namespace LegendaryTools.Reactive.UGUI
{
    /// <summary>
    /// Extension methods to bind Observables with UnityEngine.UI.Toggle.
    /// Provides bindings for IsOn (two-way), Interactable, Enabled and GraphicColor.
    /// </summary>
    public static class ToggleBindings
    {
        /// <summary>
        /// Binds Toggle.isOn to an Observable{T}. Supports TwoWay/ToUI/FromUI with optional converters.
        /// </summary>
        public static BindingHandle BindIsOn<T>(
            this Toggle toggle,
            Observable<T> observable,
            BindDirection direction = BindDirection.TwoWay,
            MonoBehaviour owner = null,
            Func<T, bool> toUI = null,
            Func<bool, T> fromUI = null,
            IFormatProvider formatProvider = null,
            BindingOptions options = null)
            where T : IEquatable<T>, IComparable<T>, IComparable, IConvertible
        {
            if (toggle == null) throw new ArgumentNullException(nameof(toggle));
            if (observable == null) throw new ArgumentNullException(nameof(observable));

            BindingAnchor anchor = owner != null
                ? owner.GetComponent<BindingAnchor>() ?? owner.gameObject.AddComponent<BindingAnchor>()
                : null;

            options ??= new BindingOptions();
            formatProvider ??= options.FormatProvider ?? CultureInfo.InvariantCulture;

            bool isUpdating = false;

            bool ToBool(T v)
            {
                if (toUI != null) return toUI(v);
                try
                {
                    return Convert.ToBoolean(v, formatProvider);
                }
                catch
                {
                    return toggle.isOn;
                }
            }

            T FromBool(bool b)
            {
                if (fromUI != null) return fromUI(b);
                try
                {
                    return (T)Convert.ChangeType(b, typeof(T), formatProvider);
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
                    toggle.SetIsOnWithoutNotify(ToBool(v));
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

            void OnUIChanged(bool b)
            {
                if (direction == BindDirection.ToUI) return;
                if (isUpdating) return;
                T parsed = FromBool(b);
                observable.Value = parsed;
            }

            void Subscribe()
            {
                if (direction != BindDirection.FromUI)
                    ApplyToUI(observable.Value);

                observable.OnChanged += OnObsChanged;
                toggle.onValueChanged.AddListener(OnUIChanged);
            }

            void Unsubscribe()
            {
                observable.OnChanged -= OnObsChanged;
                toggle.onValueChanged.RemoveListener(OnUIChanged);
            }

            void Resync()
            {
                if (direction != BindDirection.FromUI)
                    ApplyToUI(observable.Value);
            }

            BindingInfo info = new()
            {
                Kind = "Toggle.IsOn",
                Direction = direction.ToString(),
                Description = $"isOn ↔ Observable<{typeof(T).Name}>",
                Target = toggle,
                Owner = owner,
                Anchor = anchor,
                Options = options,
                GetState = () => $"isOn={toggle.isOn}",
                Tags = new[] { "Toggle", "IsOn" }
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
        /// One-way binding: Observable{bool} -> Toggle.interactable.
        /// </summary>
        public static BindingHandle BindInteractable(
            this Toggle toggle,
            Observable<bool> interactableObservable,
            MonoBehaviour owner = null,
            BindingOptions options = null)
        {
            if (toggle == null) throw new ArgumentNullException(nameof(toggle));
            if (interactableObservable == null) throw new ArgumentNullException(nameof(interactableObservable));

            BindingAnchor anchor = owner != null
                ? owner.GetComponent<BindingAnchor>() ?? owner.gameObject.AddComponent<BindingAnchor>()
                : null;

            options ??= new BindingOptions();

            void Apply(bool v)
            {
                if (anchor != null && !anchor.ShouldProcessNow()) return;
                toggle.interactable = v;
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
                Kind = "Toggle.Interactable",
                Direction = "ToUI",
                Description = "interactable ← Observable<bool>",
                Target = toggle,
                Owner = owner,
                Anchor = anchor,
                Options = options,
                GetState = () => $"interactable={toggle.interactable}",
                Tags = new[] { "Toggle", "Interactable" }
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
        /// One-way binding: Observable{bool} -> Behaviour.enabled (Toggle).
        /// </summary>
        public static BindingHandle BindEnabled(
            this Toggle toggle,
            Observable<bool> enabledObservable,
            MonoBehaviour owner = null,
            BindingOptions options = null)
        {
            if (toggle == null) throw new ArgumentNullException(nameof(toggle));
            if (enabledObservable == null) throw new ArgumentNullException(nameof(enabledObservable));

            BindingAnchor anchor = owner != null
                ? owner.GetComponent<BindingAnchor>() ?? owner.gameObject.AddComponent<BindingAnchor>()
                : null;

            options ??= new BindingOptions();

            void Apply(bool v)
            {
                if (anchor != null && !anchor.ShouldProcessNow()) return;
                toggle.enabled = v;
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
                Kind = "Toggle.Enabled",
                Direction = "ToUI",
                Description = "enabled ← Observable<bool>",
                Target = toggle,
                Owner = owner,
                Anchor = anchor,
                Options = options,
                GetState = () => $"enabled={toggle.enabled}",
                Tags = new[] { "Toggle", "Enabled" }
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
        /// One-way binding: Observable{Color} -> Toggle.graphic.color (if graphic exists).
        /// </summary>
        public static BindingHandle BindGraphicColor(
            this Toggle toggle,
            Observable<Color> colorObservable,
            MonoBehaviour owner = null,
            BindingOptions options = null)
        {
            if (toggle == null) throw new ArgumentNullException(nameof(toggle));
            if (colorObservable == null) throw new ArgumentNullException(nameof(colorObservable));

            Graphic graphic = toggle.graphic;
            if (graphic == null) throw new InvalidOperationException("Toggle.graphic is null.");

            BindingAnchor anchor = owner != null
                ? owner.GetComponent<BindingAnchor>() ?? owner.gameObject.AddComponent<BindingAnchor>()
                : null;

            options ??= new BindingOptions();

            void Apply(Color c)
            {
                if (anchor != null && !anchor.ShouldProcessNow()) return;
                graphic.color = c;
            }

            void OnObsChanged(IObservable<Color> _, Color oldV, Color newV)
            {
                Apply(newV);
            }

            void Subscribe()
            {
                Apply(colorObservable.Value);
                colorObservable.OnChanged += OnObsChanged;
            }

            void Unsubscribe()
            {
                colorObservable.OnChanged -= OnObsChanged;
            }

            void Resync()
            {
                Apply(colorObservable.Value);
            }

            BindingInfo info = new()
            {
                Kind = "Toggle.GraphicColor",
                Direction = "ToUI",
                Description = "graphic.color ← Observable<Color>",
                Target = toggle,
                Owner = owner,
                Anchor = anchor,
                Options = options,
                GetState = () => $"graphic.color={(graphic != null ? graphic.color.ToString() : "<no graphic>")}",
                Tags = new[] { "Toggle", "Graphic" }
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