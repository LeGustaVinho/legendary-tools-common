using System;
using System.Globalization;
using TMPro;
using UnityEngine;
using LegendaryTools; // Observable<T>
using LegendaryTools.Reactive;

namespace LegendaryTools.Reactive.TMPro
{
    /// <summary>
    /// Extension methods to bind Observable with TextMeshProUGUI (TMP_Text).
    /// Supports formatting, culture, placeholders, edit-mode gating, auto-unbind/resync,
    /// and emits BindingInfo for the Bindings Debugger.
    /// </summary>
    public static class TmpTextBindings
    {
        /// <summary>
        /// One-way binding: Observable{T} -> TMP_Text.text.
        /// Supports format string, custom formatter, IFormatProvider, placeholder, EditMode policy and lifecycle.
        /// </summary>
        public static BindingHandle BindText<T>(
            this TMP_Text label,
            Observable<T> observable,
            MonoBehaviour owner = null,
            string format = null,
            IFormatProvider formatProvider = null,
            Func<T, string> toUI = null,
            BindingOptions options = null)
            where T : IEquatable<T>, IComparable<T>, IComparable, IConvertible
        {
            if (label == null) throw new ArgumentNullException(nameof(label));
            if (observable == null) throw new ArgumentNullException(nameof(observable));

            // Anchor & options
            BindingAnchor anchor = owner != null
                ? owner.GetComponent<BindingAnchor>() ?? owner.gameObject.AddComponent<BindingAnchor>()
                : null;

            options ??= new BindingOptions();
            formatProvider ??= options.FormatProvider ?? CultureInfo.InvariantCulture;
            string placeholder = string.IsNullOrEmpty(options.NullOrInvalidPlaceholder)
                ? "<empty>"
                : options.NullOrInvalidPlaceholder;

            // Formatter
            string FormatValue(T v)
            {
                try
                {
                    if (toUI != null) return toUI(v);
                    if (!string.IsNullOrEmpty(format)) return string.Format(formatProvider, format, v);
                    return v?.ToString() ?? placeholder;
                }
                catch
                {
                    return placeholder;
                }
            }

            // Apply helper (respects EditMode policy)
            void Apply(T v)
            {
                if (anchor != null && !anchor.ShouldProcessNow()) return;
                label.text = FormatValue(v);
            }

            // Subscriptions
            void OnObsChanged(IObservable<T> _, T oldV, T newV)
            {
                Apply(newV);
            }

            void Subscribe()
            {
                Apply(observable.Value);
                observable.OnChanged += OnObsChanged;
            }

            void Unsubscribe()
            {
                observable.OnChanged -= OnObsChanged;
            }

            void Resync()
            {
                Apply(observable.Value);
            }

            // Binding info for debugger
            BindingInfo info = new()
            {
                Kind = "TMP_Text.Text",
                Direction = "ToUI",
                Description = $"Text ← Observable<{typeof(T).Name}>",
                Target = label,
                Owner = owner,
                Anchor = anchor,
                Options = options,
                GetState = () => $"text=\"{label.text}\""
            };

            BindingHandle handle = new(Subscribe, Unsubscribe, Resync, anchor, options, info);

            // Ensure disposal on destroy (legacy path if no Anchor-based owner management is desired)
            if (owner != null)
            {
                BindingDisposer disposer = owner.GetComponent<BindingDisposer>() ??
                                           owner.gameObject.AddComponent<BindingDisposer>();
                disposer.Add(handle);
            }

            return handle;
        }

        /// <summary>
        /// One-way binding: Observable{Color} -> TMP_Text.color.
        /// Respects EditMode policy and lifecycle.
        /// </summary>
        public static BindingHandle BindColor(
            this TMP_Text label,
            Observable<Color> colorObservable,
            MonoBehaviour owner = null,
            BindingOptions options = null)
        {
            if (label == null) throw new ArgumentNullException(nameof(label));
            if (colorObservable == null) throw new ArgumentNullException(nameof(colorObservable));

            BindingAnchor anchor = owner != null
                ? owner.GetComponent<BindingAnchor>() ?? owner.gameObject.AddComponent<BindingAnchor>()
                : null;

            options ??= new BindingOptions();

            void Apply(Color c)
            {
                if (anchor != null && !anchor.ShouldProcessNow()) return;
                label.color = c;
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
                Kind = "TMP_Text.Color",
                Direction = "ToUI",
                Description = "color ← Observable<Color>",
                Target = label,
                Owner = owner,
                Anchor = anchor,
                Options = options,
                GetState = () => $"color={label.color}"
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
        /// One-way binding: Observable{float} -> TMP_Text.fontSize.
        /// Respects EditMode policy and lifecycle.
        /// </summary>
        public static BindingHandle BindFontSize(
            this TMP_Text label,
            Observable<float> fontSizeObservable,
            MonoBehaviour owner = null,
            BindingOptions options = null)
        {
            if (label == null) throw new ArgumentNullException(nameof(label));
            if (fontSizeObservable == null) throw new ArgumentNullException(nameof(fontSizeObservable));

            BindingAnchor anchor = owner != null
                ? owner.GetComponent<BindingAnchor>() ?? owner.gameObject.AddComponent<BindingAnchor>()
                : null;

            options ??= new BindingOptions();

            void Apply(float s)
            {
                if (anchor != null && !anchor.ShouldProcessNow()) return;
                label.fontSize = s;
            }

            void OnObsChanged(IObservable<float> _, float oldV, float newV)
            {
                Apply(newV);
            }

            void Subscribe()
            {
                Apply(fontSizeObservable.Value);
                fontSizeObservable.OnChanged += OnObsChanged;
            }

            void Unsubscribe()
            {
                fontSizeObservable.OnChanged -= OnObsChanged;
            }

            void Resync()
            {
                Apply(fontSizeObservable.Value);
            }

            BindingInfo info = new()
            {
                Kind = "TMP_Text.FontSize",
                Direction = "ToUI",
                Description = "fontSize ← Observable<float>",
                Target = label,
                Owner = owner,
                Anchor = anchor,
                Options = options,
                GetState = () => $"fontSize={label.fontSize}"
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
        /// One-way binding: Observable{bool} -> component enabled state.
        /// Respects EditMode policy and lifecycle.
        /// </summary>
        public static BindingHandle BindEnabled(
            this TMP_Text label,
            Observable<bool> enabledObservable,
            MonoBehaviour owner = null,
            BindingOptions options = null)
        {
            if (label == null) throw new ArgumentNullException(nameof(label));
            if (enabledObservable == null) throw new ArgumentNullException(nameof(enabledObservable));

            BindingAnchor anchor = owner != null
                ? owner.GetComponent<BindingAnchor>() ?? owner.gameObject.AddComponent<BindingAnchor>()
                : null;

            options ??= new BindingOptions();

            void Apply(bool v)
            {
                if (anchor != null && !anchor.ShouldProcessNow()) return;
                label.enabled = v;
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
                Kind = "TMP_Text.Enabled",
                Direction = "ToUI",
                Description = "enabled ← Observable<bool>",
                Target = label,
                Owner = owner,
                Anchor = anchor,
                Options = options,
                GetState = () => $"enabled={label.enabled}"
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
        /// Inverted fluent syntax: Observable -> TMP_Text (ToUI).
        /// Shortcut that calls label.BindText(...).
        /// </summary>
        public static BindingHandle BindTmpText<T>(
            this Observable<T> observable,
            TMP_Text label,
            MonoBehaviour owner = null,
            string format = null,
            IFormatProvider formatProvider = null,
            Func<T, string> toUI = null,
            BindingOptions options = null)
            where T : IEquatable<T>, IComparable<T>, IComparable, IConvertible
        {
            return label.BindText(observable, owner, format, formatProvider, toUI, options);
        }
    }
}