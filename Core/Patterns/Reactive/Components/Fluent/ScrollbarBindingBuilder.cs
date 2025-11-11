using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

namespace LegendaryTools.Reactive.UGUI
{
    /// <summary>
    /// Fluent builder for UnityEngine.UI.Scrollbar.
    /// Example:
    /// percent.Bind(scrollbar)
    ///   .Value(BindDirection.TwoWay).ToUI(v => v).FromUI(f => f)
    ///   .Size(sizeObs)           // or .Size(0.2f)
    ///   .Steps(10)               // or .Steps(obsInt)
    ///   .Direction(dirObs)       // Observable<Scrollbar.Direction>
    ///   .Interactable(isReady)
    ///   .Enabled(true)
    ///   .Owner(this)
    ///   .With(options);
    /// </summary>
    public sealed class ScrollbarBindingBuilder<T>
        where T : IEquatable<T>, IComparable<T>, IComparable, IConvertible
    {
        private readonly Observable<T> _observable;
        private readonly Scrollbar _scrollbar;

        // Value
        private BindDirection _direction = BindDirection.TwoWay;
        private Func<T, float> _toUI;
        private Func<float, T> _fromUI;
        private IFormatProvider _formatProvider;

        // Size
        private Observable<float> _sizeObs;
        private float? _sizeFixed;
        private bool _sizeClamp01 = true;

        // Steps
        private Observable<int> _stepsObs;
        private int? _stepsFixed;

        // Direction
        private Observable<Scrollbar.Direction> _directionObs;

        // Interactable/Enabled
        private Observable<bool> _interactableObs;
        private bool? _interactableFixed;

        private Observable<bool> _enabledObs;
        private bool? _enabledFixed;

        // Lifetime / options
        private MonoBehaviour _owner;
        private BindingOptions _options;

        public ScrollbarBindingBuilder(Observable<T> observable, Scrollbar scrollbar)
        {
            _observable = observable ?? throw new ArgumentNullException(nameof(observable));
            _scrollbar = scrollbar ?? throw new ArgumentNullException(nameof(scrollbar));
        }

        /// <summary>
        /// Configure the value binding direction (TwoWay by default).
        /// </summary>
        public ScrollbarBindingBuilder<T> Value(BindDirection direction = BindDirection.TwoWay)
        {
            _direction = direction;
            return this;
        }

        /// <summary>
        /// Optional converter from T to float (UI).
        /// </summary>
        public ScrollbarBindingBuilder<T> ToUI(Func<T, float> toUI)
        {
            _toUI = toUI;
            return this;
        }

        /// <summary>
        /// Optional converter from float (UI) to T (VM).
        /// </summary>
        public ScrollbarBindingBuilder<T> FromUI(Func<float, T> fromUI)
        {
            _fromUI = fromUI;
            return this;
        }

        /// <summary>
        /// Optional IFormatProvider used by default conversion paths.
        /// </summary>
        public ScrollbarBindingBuilder<T> FormatProvider(IFormatProvider provider)
        {
            _formatProvider = provider;
            return this;
        }

        public ScrollbarBindingBuilder<T> Size(Observable<float> sizeObservable, bool clamp01 = true)
        {
            _sizeObs = sizeObservable;
            _sizeFixed = null;
            _sizeClamp01 = clamp01;
            return this;
        }

        public ScrollbarBindingBuilder<T> Size(float size, bool clamp01 = true)
        {
            _sizeFixed = size;
            _sizeObs = null;
            _sizeClamp01 = clamp01;
            return this;
        }

        public ScrollbarBindingBuilder<T> Steps(Observable<int> stepsObservable)
        {
            _stepsObs = stepsObservable;
            _stepsFixed = null;
            return this;
        }

        public ScrollbarBindingBuilder<T> Steps(int steps)
        {
            _stepsFixed = steps;
            _stepsObs = null;
            return this;
        }

        public ScrollbarBindingBuilder<T> Direction(Observable<Scrollbar.Direction> directionObservable)
        {
            _directionObs = directionObservable;
            return this;
        }

        public ScrollbarBindingBuilder<T> Interactable(Observable<bool> interactableObservable)
        {
            _interactableObs = interactableObservable;
            _interactableFixed = null;
            return this;
        }

        public ScrollbarBindingBuilder<T> Interactable(bool interactable)
        {
            _interactableFixed = interactable;
            _interactableObs = null;
            return this;
        }

        public ScrollbarBindingBuilder<T> Enabled(Observable<bool> enabledObservable)
        {
            _enabledObs = enabledObservable;
            _enabledFixed = null;
            return this;
        }

        public ScrollbarBindingBuilder<T> Enabled(bool enabled)
        {
            _enabledFixed = enabled;
            _enabledObs = null;
            return this;
        }

        public ScrollbarBindingBuilder<T> Owner(MonoBehaviour owner)
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

            // Value (TwoWay/ToUI/FromUI)
            handles.Add(_scrollbar.BindValue(
                _observable,
                _direction,
                _owner,
                _toUI,
                _fromUI,
                provider,
                _options));

            // Size
            if (_sizeObs != null)
                handles.Add(_scrollbar.BindSize(_sizeObs, _owner, _options, _sizeClamp01));
            else if (_sizeFixed.HasValue)
                _scrollbar.size = _sizeClamp01 ? Mathf.Clamp01(_sizeFixed.Value) : _sizeFixed.Value;

            // Steps
            if (_stepsObs != null)
                handles.Add(_scrollbar.BindNumberOfSteps(_stepsObs, _owner, _options));
            else if (_stepsFixed.HasValue)
                _scrollbar.numberOfSteps = Mathf.Max(0, _stepsFixed.Value);

            // Direction
            if (_directionObs != null)
                handles.Add(_scrollbar.BindDirection(_directionObs, _owner, _options));

            // Interactable
            if (_interactableObs != null)
                handles.Add(_scrollbar.BindInteractable(_interactableObs, _owner, _options));
            else if (_interactableFixed.HasValue)
                _scrollbar.interactable = _interactableFixed.Value;

            // Enabled
            if (_enabledObs != null)
                handles.Add(_scrollbar.BindEnabled(_enabledObs, _owner, _options));
            else if (_enabledFixed.HasValue)
                _scrollbar.enabled = _enabledFixed.Value;

            return new CompositeBindingHandle(handles);
        }

        public CompositeBindingHandle WithDefaultOptions()
        {
            return With(null);
        }
    }
}