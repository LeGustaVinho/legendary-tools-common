using System;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;

namespace LegendaryTools.Reactive.TMPro
{
    /// <summary>
    /// Fluent builder for TMP_InputField bindings:
    /// observable.Bind(input)
    ///   .Text(direction: TwoWay).Commit(CommitMode.OnChange).Format("{0}", provider)...
    ///   .Placeholder("Type...") or .Placeholder(obs)
    ///   .Color(Color.white) or .Color(obsColor)
    ///   .FontSize(28f) or .FontSize(obsFloat)
    ///   .Enabled(true) or .Enabled(obsBool)
    ///   .Owner(this)
    ///   .With(options);
    /// </summary>
    public sealed class InputFieldBindingBuilder<T>
        where T : IEquatable<T>, IComparable<T>, IComparable, IConvertible
    {
        private readonly Observable<T> _observable;
        private readonly TMP_InputField _field;

        private BindDirection _direction = BindDirection.TwoWay;
        private CommitMode _commitMode = CommitMode.OnChange;

        private string _format;
        private IFormatProvider _formatProvider;
        private Func<T, string> _toUI;
        private Func<string, T> _fromUI;

        private Observable<string> _placeholderObs;
        private string _placeholderFixed;

        private Observable<Color> _colorObs;
        private Color? _colorFixed;

        private Observable<float> _fontSizeObs;
        private float? _fontSizeFixed;

        private Observable<bool> _enabledObs;
        private bool? _enabledFixed;

        private MonoBehaviour _owner;
        private BindingOptions _options;

        public InputFieldBindingBuilder(Observable<T> observable, TMP_InputField field)
        {
            _observable = observable ?? throw new ArgumentNullException(nameof(observable));
            _field = field ?? throw new ArgumentNullException(nameof(field));
        }

        public InputFieldBindingBuilder<T> Text(BindDirection direction = BindDirection.TwoWay)
        {
            _direction = direction;
            return this;
        }

        /// <summary>
        /// Sets when UI changes are committed back to the Observable.
        /// </summary>
        public InputFieldBindingBuilder<T> Commit(CommitMode mode)
        {
            _commitMode = mode;
            return this;
        }

        public InputFieldBindingBuilder<T> Format(string format, IFormatProvider provider = null)
        {
            _format = format;
            _formatProvider = provider ?? _formatProvider;
            return this;
        }

        public InputFieldBindingBuilder<T> FormatProvider(IFormatProvider provider)
        {
            _formatProvider = provider;
            return this;
        }

        public InputFieldBindingBuilder<T> ToUI(Func<T, string> toUI)
        {
            _toUI = toUI;
            return this;
        }

        public InputFieldBindingBuilder<T> FromUI(Func<string, T> fromUI)
        {
            _fromUI = fromUI;
            return this;
        }

        public InputFieldBindingBuilder<T> Placeholder(Observable<string> placeholderObservable)
        {
            _placeholderObs = placeholderObservable;
            _placeholderFixed = null;
            return this;
        }

        public InputFieldBindingBuilder<T> Placeholder(string fixedPlaceholder)
        {
            _placeholderFixed = fixedPlaceholder;
            _placeholderObs = null;
            return this;
        }

        public InputFieldBindingBuilder<T> Color(Observable<Color> colorObservable)
        {
            _colorObs = colorObservable;
            _colorFixed = null;
            return this;
        }

        public InputFieldBindingBuilder<T> Color(Color fixedColor)
        {
            _colorFixed = fixedColor;
            _colorObs = null;
            return this;
        }

        public InputFieldBindingBuilder<T> FontSize(Observable<float> sizeObservable)
        {
            _fontSizeObs = sizeObservable;
            _fontSizeFixed = null;
            return this;
        }

        public InputFieldBindingBuilder<T> FontSize(float fixedSize)
        {
            _fontSizeFixed = fixedSize;
            _fontSizeObs = null;
            return this;
        }

        public InputFieldBindingBuilder<T> Enabled(Observable<bool> enabledObservable)
        {
            _enabledObs = enabledObservable;
            _enabledFixed = null;
            return this;
        }

        public InputFieldBindingBuilder<T> Enabled(bool enabled)
        {
            _enabledFixed = enabled;
            _enabledObs = null;
            return this;
        }

        public InputFieldBindingBuilder<T> Owner(MonoBehaviour owner)
        {
            _owner = owner;
            return this;
        }

        public CompositeBindingHandle With(BindingOptions options = null)
        {
            _options ??= options ?? new BindingOptions();
            List<BindingHandle> handles = new();

            IFormatProvider provider = _formatProvider ?? _options.FormatProvider ?? CultureInfo.InvariantCulture;

            // Text + Commit mode
            handles.Add(_field.BindText(
                _observable,
                _direction,
                _owner,
                _format,
                provider,
                _toUI,
                _fromUI,
                _options,
                _commitMode));

            // Placeholder
            if (_placeholderObs != null)
            {
                handles.Add(_field.BindPlaceholderText(
                    _placeholderObs,
                    _owner,
                    null,
                    provider,
                    null,
                    _options));
            }
            else if (!string.IsNullOrEmpty(_placeholderFixed))
            {
                TMP_Text t = _field.placeholder as TMP_Text;
                if (t != null) t.text = _placeholderFixed;
            }

            // Color
            if (_colorObs != null)
                handles.Add(_field.BindColor(_colorObs, _owner, _options));
            else if (_colorFixed.HasValue && _field.textComponent != null)
                _field.textComponent.color = _colorFixed.Value;

            // Font size
            if (_fontSizeObs != null)
                handles.Add(_field.BindFontSize(_fontSizeObs, _owner, _options));
            else if (_fontSizeFixed.HasValue && _field.textComponent != null)
                _field.textComponent.fontSize = _fontSizeFixed.Value;

            // Enabled
            if (_enabledObs != null)
                handles.Add(_field.BindEnabled(_enabledObs, _owner, _options));
            else if (_enabledFixed.HasValue) _field.enabled = _enabledFixed.Value;

            return new CompositeBindingHandle(handles);
        }

        public CompositeBindingHandle WithDefaultOptions()
        {
            return With(null);
        }
    }
}