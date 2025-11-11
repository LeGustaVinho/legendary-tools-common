using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

namespace LegendaryTools.Reactive.UGUI
{
    /// <summary>
    /// Fluent builder for UnityEngine.UI.Toggle.
    /// Example:
    /// vmFlag.Bind(toggle)
    ///   .IsOn(BindDirection.TwoWay)
    ///   .ToUI(v => v > 0)                 // optional converter T->bool
    ///   .FromUI(b => b ? 1 : 0)           // optional converter bool->T
    ///   .Interactable(isReadyObs)
    ///   .Enabled(true)
    ///   .GraphicColor(Color.white)        // or .GraphicColor(obsColor)
    ///   .Owner(this)
    ///   .With(options);
    /// </summary>
    public sealed class ToggleBindingBuilder<T>
        where T : IEquatable<T>, IComparable<T>, IComparable, IConvertible
    {
        private readonly Observable<T> _observable;
        private readonly Toggle _toggle;

        private BindDirection _direction = BindDirection.TwoWay;
        private Func<T, bool> _toUI;
        private Func<bool, T> _fromUI;
        private IFormatProvider _formatProvider;

        private Observable<bool> _interactableObs;
        private bool? _interactableFixed;

        private Observable<bool> _enabledObs;
        private bool? _enabledFixed;

        private Observable<Color> _graphicColorObs;
        private Color? _graphicColorFixed;

        private MonoBehaviour _owner;
        private BindingOptions _options;

        public ToggleBindingBuilder(Observable<T> observable, Toggle toggle)
        {
            _observable = observable ?? throw new ArgumentNullException(nameof(observable));
            _toggle = toggle ?? throw new ArgumentNullException(nameof(toggle));
        }

        /// <summary>
        /// Configure the isOn binding direction (TwoWay by default).
        /// </summary>
        public ToggleBindingBuilder<T> IsOn(BindDirection direction = BindDirection.TwoWay)
        {
            _direction = direction;
            return this;
        }

        /// <summary>
        /// Optional converter from T to bool (UI).
        /// </summary>
        public ToggleBindingBuilder<T> ToUI(Func<T, bool> toUI)
        {
            _toUI = toUI;
            return this;
        }

        /// <summary>
        /// Optional converter from bool (UI) to T (VM).
        /// </summary>
        public ToggleBindingBuilder<T> FromUI(Func<bool, T> fromUI)
        {
            _fromUI = fromUI;
            return this;
        }

        /// <summary>
        /// Optional IFormatProvider used by default conversion paths.
        /// </summary>
        public ToggleBindingBuilder<T> FormatProvider(IFormatProvider provider)
        {
            _formatProvider = provider;
            return this;
        }

        public ToggleBindingBuilder<T> Interactable(Observable<bool> interactableObservable)
        {
            _interactableObs = interactableObservable;
            _interactableFixed = null;
            return this;
        }

        public ToggleBindingBuilder<T> Interactable(bool interactable)
        {
            _interactableFixed = interactable;
            _interactableObs = null;
            return this;
        }

        public ToggleBindingBuilder<T> Enabled(Observable<bool> enabledObservable)
        {
            _enabledObs = enabledObservable;
            _enabledFixed = null;
            return this;
        }

        public ToggleBindingBuilder<T> Enabled(bool enabled)
        {
            _enabledFixed = enabled;
            _enabledObs = null;
            return this;
        }

        public ToggleBindingBuilder<T> GraphicColor(Observable<Color> colorObservable)
        {
            _graphicColorObs = colorObservable;
            _graphicColorFixed = null;
            return this;
        }

        public ToggleBindingBuilder<T> GraphicColor(Color fixedColor)
        {
            _graphicColorFixed = fixedColor;
            _graphicColorObs = null;
            return this;
        }

        public ToggleBindingBuilder<T> Owner(MonoBehaviour owner)
        {
            _owner = owner;
            return this;
        }

        /// <summary>
        /// Creates the configured bindings and returns a composite handle.
        /// </summary>
        public CompositeBindingHandle With(BindingOptions options = null)
        {
            _options ??= options ?? new BindingOptions();
            List<BindingHandle> handles = new();

            IFormatProvider provider = _formatProvider ?? _options.FormatProvider ?? CultureInfo.InvariantCulture;

            // isOn
            handles.Add(_toggle.BindIsOn(
                _observable,
                _direction,
                _owner,
                _toUI,
                _fromUI,
                provider,
                _options));

            // Interactable
            if (_interactableObs != null)
                handles.Add(_toggle.BindInteractable(_interactableObs, _owner, _options));
            else if (_interactableFixed.HasValue)
                _toggle.interactable = _interactableFixed.Value;

            // Enabled
            if (_enabledObs != null)
                handles.Add(_toggle.BindEnabled(_enabledObs, _owner, _options));
            else if (_enabledFixed.HasValue)
                _toggle.enabled = _enabledFixed.Value;

            // Graphic color
            if (_graphicColorObs != null)
                handles.Add(_toggle.BindGraphicColor(_graphicColorObs, _owner, _options));
            else if (_graphicColorFixed.HasValue && _toggle.graphic != null)
                _toggle.graphic.color = _graphicColorFixed.Value;

            return new CompositeBindingHandle(handles);
        }

        public CompositeBindingHandle WithDefaultOptions()
        {
            return With(null);
        }
    }
}