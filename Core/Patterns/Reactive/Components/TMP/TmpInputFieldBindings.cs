using System;
using System.Globalization;
using TMPro;
using UnityEngine;
using LegendaryTools; // Observable<T>
using LegendaryTools.Reactive;
using UnityEngine.UI;

namespace LegendaryTools.Reactive.TMPro
{
    /// <summary>
    /// Extension methods to bind Observable with TMP_InputField (uGUI).
    /// Supports TwoWay/ToUI/FromUI, commit modes, formatting, culture, placeholders,
    /// edit-mode gating, auto-unbind/resync, suspend/resume and BindingInfo for the Debugger.
    /// </summary>
    public static class TmpInputFieldBindings
    {
        /// <summary>
        /// Binds the text of a TMP_InputField to an Observable{T}.
        /// Two-way by default. You can override data flow via <see cref="BindDirection"/>.
        /// </summary>
        public static BindingHandle BindText<T>(
            this TMP_InputField field,
            Observable<T> observable,
            BindDirection direction = BindDirection.TwoWay,
            MonoBehaviour owner = null,
            string format = null,
            IFormatProvider formatProvider = null,
            Func<T, string> toUI = null,
            Func<string, T> fromUI = null,
            BindingOptions options = null,
            CommitMode commitMode = CommitMode.OnChange)
            where T : IEquatable<T>, IComparable<T>, IComparable, IConvertible
        {
            if (field == null) throw new ArgumentNullException(nameof(field));
            if (observable == null) throw new ArgumentNullException(nameof(observable));

            BindingAnchor anchor = owner != null
                ? owner.GetComponent<BindingAnchor>() ?? owner.gameObject.AddComponent<BindingAnchor>()
                : null;

            options ??= new BindingOptions();
            formatProvider ??= options.FormatProvider ?? CultureInfo.InvariantCulture;
            string placeholder = string.IsNullOrEmpty(options.NullOrInvalidPlaceholder)
                ? "<empty>"
                : options.NullOrInvalidPlaceholder;

            bool isUpdating = false;

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

            T ParseValue(string s)
            {
                if (fromUI != null) return fromUI(s);

                Type t = typeof(T);
                if (t.IsEnum)
                {
                    if (Enum.TryParse(t, s, true, out object parsed)) return (T)parsed;
                    if (int.TryParse(s, NumberStyles.Integer, formatProvider, out int i))
                        return (T)Enum.ToObject(t, i);
                    throw new FormatException($"Cannot parse '{s}' to {t.Name}.");
                }

                if (string.IsNullOrWhiteSpace(s))
                    // Keep previous value if empty input is not parseable
                    return observable.Value;

                return (T)Convert.ChangeType(s, t, formatProvider);
            }

            void PushToUI(T value)
            {
                if (anchor != null && !anchor.ShouldProcessNow()) return;

                try
                {
                    isUpdating = true;
                    field.SetTextWithoutNotify(FormatValue(value));
                }
                finally
                {
                    isUpdating = false;
                }
            }

            void OnObsChanged(IObservable<T> _, T oldV, T newV)
            {
                if (direction == BindDirection.FromUI) return;
                PushToUI(newV);
            }

            // UI -> Observable handlers depending on commit mode
            void CommitFromUI(string text)
            {
                if (direction == BindDirection.ToUI) return;
                if (isUpdating) return;
                try
                {
                    T parsed = ParseValue(text);
                    observable.Value = parsed;
                }
                catch
                {
                    // Ignore parse errors; user can correct input before next commit.
                }
            }

            void OnUIChanged(string text)
            {
                if (commitMode == CommitMode.OnChange) CommitFromUI(text);
            }

            void OnUIEndEdit(string text)
            {
                if (commitMode == CommitMode.OnEndEdit) CommitFromUI(text);
            }

            void OnUISubmit(string text)
            {
                if (commitMode == CommitMode.OnSubmit) CommitFromUI(text);
            }

            void Subscribe()
            {
                if (direction != BindDirection.FromUI) PushToUI(observable.Value);

                observable.OnChanged += OnObsChanged;

                // Attach only the required UI listeners
                switch (commitMode)
                {
                    case CommitMode.OnChange:
                        field.onValueChanged.AddListener(OnUIChanged);
                        break;
                    case CommitMode.OnEndEdit:
                        field.onEndEdit.AddListener(OnUIEndEdit);
                        break;
                    case CommitMode.OnSubmit:
                        field.onSubmit.AddListener(OnUISubmit);
                        break;
                }
            }

            void Unsubscribe()
            {
                observable.OnChanged -= OnObsChanged;

                // Detach according to commit mode
                switch (commitMode)
                {
                    case CommitMode.OnChange:
                        field.onValueChanged.RemoveListener(OnUIChanged);
                        break;
                    case CommitMode.OnEndEdit:
                        field.onEndEdit.RemoveListener(OnUIEndEdit);
                        break;
                    case CommitMode.OnSubmit:
                        field.onSubmit.RemoveListener(OnUISubmit);
                        break;
                }
            }

            void Resync()
            {
                if (direction != BindDirection.FromUI)
                    PushToUI(observable.Value);
            }

            BindingInfo info = new()
            {
                Kind = "TMP_InputField.Text",
                Direction = $"{direction} [{commitMode}]",
                Description = $"Text ↔ Observable<{typeof(T).Name}>",
                Target = field,
                Owner = owner,
                Anchor = anchor,
                Options = options,
                GetState = () => $"text=\"{field.text}\"",
                Tags = new[] { "Input", "TMP", commitMode.ToString() }
            };

            BindingHandle handle = new(Subscribe, Unsubscribe, Resync, anchor, options, info);

            // Ensure disposal on destroy for legacy convenience
            if (owner != null)
            {
                BindingDisposer disposer = owner.GetComponent<BindingDisposer>() ??
                                           owner.gameObject.AddComponent<BindingDisposer>();
                disposer.Add(handle);
            }

            return handle;
        }

        public static BindingHandle BindEnabled(
            this TMP_InputField field,
            Observable<bool> enabledObservable,
            MonoBehaviour owner = null,
            BindingOptions options = null)
        {
            if (field == null) throw new ArgumentNullException(nameof(field));
            if (enabledObservable == null) throw new ArgumentNullException(nameof(enabledObservable));

            BindingAnchor anchor = owner != null
                ? owner.GetComponent<BindingAnchor>() ?? owner.gameObject.AddComponent<BindingAnchor>()
                : null;

            options ??= new BindingOptions();

            void Apply(bool v)
            {
                if (anchor != null && !anchor.ShouldProcessNow()) return;
                field.enabled = v;
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
                Kind = "TMP_InputField.Enabled",
                Direction = "ToUI",
                Description = "enabled ← Observable<bool>",
                Target = field,
                Owner = owner,
                Anchor = anchor,
                Options = options,
                GetState = () => $"enabled={field.enabled}"
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

        public static BindingHandle BindColor(
            this TMP_InputField field,
            Observable<Color> colorObservable,
            MonoBehaviour owner = null,
            BindingOptions options = null)
        {
            if (field == null) throw new ArgumentNullException(nameof(field));
            if (colorObservable == null) throw new ArgumentNullException(nameof(colorObservable));
            if (field.textComponent == null)
                throw new InvalidOperationException("TMP_InputField.textComponent is null.");

            BindingAnchor anchor = owner != null
                ? owner.GetComponent<BindingAnchor>() ?? owner.gameObject.AddComponent<BindingAnchor>()
                : null;

            options ??= new BindingOptions();
            TMP_Text text = field.textComponent;

            void Apply(Color c)
            {
                if (anchor != null && !anchor.ShouldProcessNow()) return;
                text.color = c;
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
                Kind = "TMP_InputField.TextColor",
                Direction = "ToUI",
                Description = "text.color ← Observable<Color>",
                Target = field,
                Owner = owner,
                Anchor = anchor,
                Options = options,
                GetState = () => field.textComponent != null ? $"color={field.textComponent.color}" : "no textComponent"
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

        public static BindingHandle BindFontSize(
            this TMP_InputField field,
            Observable<float> fontSizeObservable,
            MonoBehaviour owner = null,
            BindingOptions options = null)
        {
            if (field == null) throw new ArgumentNullException(nameof(field));
            if (fontSizeObservable == null) throw new ArgumentNullException(nameof(fontSizeObservable));
            if (field.textComponent == null)
                throw new InvalidOperationException("TMP_InputField.textComponent is null.");

            BindingAnchor anchor = owner != null
                ? owner.GetComponent<BindingAnchor>() ?? owner.gameObject.AddComponent<BindingAnchor>()
                : null;

            options ??= new BindingOptions();
            TMP_Text text = field.textComponent;

            void Apply(float s)
            {
                if (anchor != null && !anchor.ShouldProcessNow()) return;
                text.fontSize = s;
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
                Kind = "TMP_InputField.FontSize",
                Direction = "ToUI",
                Description = "text.fontSize ← Observable<float>",
                Target = field,
                Owner = owner,
                Anchor = anchor,
                Options = options,
                GetState = () =>
                    field.textComponent != null ? $"fontSize={field.textComponent.fontSize}" : "no textComponent"
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

        public static BindingHandle BindPlaceholderText(
            this TMP_InputField field,
            Observable<string> placeholderObservable,
            MonoBehaviour owner = null,
            string format = null,
            IFormatProvider formatProvider = null,
            Func<string, string> toUI = null,
            BindingOptions options = null)
        {
            if (field == null) throw new ArgumentNullException(nameof(field));
            if (placeholderObservable == null) throw new ArgumentNullException(nameof(placeholderObservable));

            TMP_Text placeholderText = field.placeholder as TMP_Text
                                       ?? throw new InvalidOperationException(
                                           "TMP_InputField.placeholder is not TMP_Text.");

            BindingAnchor anchor = owner != null
                ? owner.GetComponent<BindingAnchor>() ?? owner.gameObject.AddComponent<BindingAnchor>()
                : null;

            options ??= new BindingOptions();
            formatProvider ??= options.FormatProvider ?? CultureInfo.InvariantCulture;
            string placeholder = string.IsNullOrEmpty(options.NullOrInvalidPlaceholder)
                ? "<empty>"
                : options.NullOrInvalidPlaceholder;

            string FormatValue(string v)
            {
                try
                {
                    if (toUI != null) return toUI(v);
                    if (!string.IsNullOrEmpty(format)) return string.Format(formatProvider, format, v);
                    return v ?? placeholder;
                }
                catch
                {
                    return placeholder;
                }
            }

            void Apply(string s)
            {
                if (anchor != null && !anchor.ShouldProcessNow()) return;
                placeholderText.text = FormatValue(s);
            }

            void OnObsChanged(IObservable<string> _, string oldV, string newV)
            {
                Apply(newV);
            }

            void Subscribe()
            {
                Apply(placeholderObservable.Value);
                placeholderObservable.OnChanged += OnObsChanged;
            }

            void Unsubscribe()
            {
                placeholderObservable.OnChanged -= OnObsChanged;
            }

            void Resync()
            {
                Apply(placeholderObservable.Value);
            }

            BindingInfo info = new()
            {
                Kind = "TMP_InputField.PlaceholderText",
                Direction = "ToUI",
                Description = "placeholder.text ← Observable<string>",
                Target = field,
                Owner = owner,
                Anchor = anchor,
                Options = options,
                GetState = () =>
                    field.placeholder is TMP_Text t ? $"placeholder=\"{t.text}\"" : "no TMP_Text placeholder"
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
        /// Binds Selectable.interactable to Observable{bool}. One-way ToUI.
        /// </summary>
        public static BindingHandle BindInteractable(
            this Selectable selectable,
            Observable<bool> interactableObservable,
            MonoBehaviour owner = null,
            BindingOptions options = null)
        {
            if (selectable == null) throw new ArgumentNullException(nameof(selectable));
            if (interactableObservable == null) throw new ArgumentNullException(nameof(interactableObservable));

            BindingAnchor anchor = owner != null
                ? owner.GetComponent<BindingAnchor>() ?? owner.gameObject.AddComponent<BindingAnchor>()
                : null;

            options ??= new BindingOptions();

            void Apply(bool v)
            {
                if (anchor != null && !anchor.ShouldProcessNow()) return;
                selectable.interactable = v;
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
                Kind = "Selectable.Interactable",
                Direction = "ToUI",
                Description = "interactable ← Observable<bool>",
                Target = selectable,
                Owner = owner,
                Anchor = anchor,
                Options = options,
                GetState = () => $"interactable={selectable.interactable}"
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