using System;
using System.Globalization;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace LegendaryTools.Reactive.TMPro
{
    /// <summary>
    /// Fluent builder for TMP_Text bindings: observable.Bind(label).Text().Format(...).Color(...).With(options)
    /// </summary>
    public sealed class TextBindingBuilder<T>
        where T : IEquatable<T>, IComparable<T>, IComparable, IConvertible
    {
        private readonly Observable<T> _observable;
        private readonly TMP_Text _label;

        // Text configuration
        private string _format;
        private IFormatProvider _formatProvider;
        private Func<T, string> _toUI;

        // Color configuration (optional)
        private Observable<Color> _colorObservable;
        private Color? _fixedColor;

        // Lifetime owner (optional)
        private MonoBehaviour _owner;

        // Options (optional; may be provided at With time)
        private BindingOptions _options;

        /// <summary>
        /// Initializes a new instance of the builder.
        /// </summary>
        public TextBindingBuilder(Observable<T> observable, TMP_Text label)
        {
            _observable = observable ?? throw new ArgumentNullException(nameof(observable));
            _label = label ?? throw new ArgumentNullException(nameof(label));
        }

        /// <summary>
        /// Optional semantic marker. No-op for now; kept for future expansions.
        /// </summary>
        public TextBindingBuilder<T> Text()
        {
            return this;
        }

        /// <summary>
        /// Sets the composite format string used to display the value.
        /// </summary>
        public TextBindingBuilder<T> Format(string format, IFormatProvider provider = null)
        {
            _format = format;
            _formatProvider = provider ?? _formatProvider;
            return this;
        }

        /// <summary>
        /// Uses a specific format provider for this binding (e.g., CultureInfo.InvariantCulture).
        /// </summary>
        public TextBindingBuilder<T> FormatProvider(IFormatProvider provider)
        {
            _formatProvider = provider;
            return this;
        }

        /// <summary>
        /// Uses a custom formatter that converts T to string.
        /// </summary>
        public TextBindingBuilder<T> ToUI(Func<T, string> toUI)
        {
            _toUI = toUI;
            return this;
        }

        /// <summary>
        /// Binds text color to an Observable{Color}.
        /// </summary>
        public TextBindingBuilder<T> Color(Observable<Color> colorObservable)
        {
            _colorObservable = colorObservable;
            _fixedColor = null;
            return this;
        }

        /// <summary>
        /// Sets a fixed color (non-reactive) for the label.
        /// </summary>
        public TextBindingBuilder<T> Color(Color fixedColor)
        {
            _fixedColor = fixedColor;
            _colorObservable = null;
            return this;
        }

        /// <summary>
        /// Optionally attach the binding to a MonoBehaviour lifetime (OnDisable/OnEnable/OnDestroy).
        /// </summary>
        public TextBindingBuilder<T> Owner(MonoBehaviour owner)
        {
            _owner = owner;
            return this;
        }

        /// <summary>
        /// Finalizes the binding with given options and returns a composite handle.
        /// If options is null, defaults are used (InvariantCulture, "<empty>", edit-mode off, etc).
        /// </summary>
        public CompositeBindingHandle With(BindingOptions options = null)
        {
            _options ??= options ?? new BindingOptions();

            List<BindingHandle> handles = new();

            // Ensure a format provider default if none set: BindingOptions.FormatProvider (defaults to InvariantCulture)
            IFormatProvider provider = _formatProvider ?? _options.FormatProvider ?? CultureInfo.InvariantCulture;

            // Text binding (ToUI) using existing extension method
            BindingHandle textHandle = _label.BindText(
                _observable,
                _owner,
                _format,
                provider,
                _toUI,
                _options);

            handles.Add(textHandle);

            // Reactive color
            if (_colorObservable != null)
            {
                BindingHandle colorHandle = _label.BindColor(
                    _colorObservable,
                    _owner,
                    _options);
                handles.Add(colorHandle);
            }

            // Fixed color (non-reactive)
            if (_fixedColor.HasValue) _label.color = _fixedColor.Value;
            // No handle required; value will remain until changed elsewhere.
            return new CompositeBindingHandle(handles);
        }

        /// <summary>
        /// Convenience terminal method with default options.
        /// </summary>
        public CompositeBindingHandle WithDefaultOptions()
        {
            return With(null);
        }
    }
}