using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

namespace LegendaryTools.Reactive.UGUI
{
    /// <summary>
    /// Fluent builder for UnityEngine.UI.Slider.
    /// Example:
    /// volume.Bind(slider)
    ///   .Value(BindDirection.TwoWay)
    ///   .ToUI(v => v)                 // optional converter T->float
    ///   .FromUI(f => f)               // optional converter float->T
    ///   .Min(minObs) .Max(1f)
    ///   .WholeNumbers(false)
    ///   .Interactable(isReadyObs)
    ///   .Enabled(true)
    ///   .Owner(this)
    ///   .With(options);
    /// </summary>
    public sealed class SliderBindingBuilder<T>
        where T : IEquatable<T>, IComparable<T>, IComparable, IConvertible
    {
        private readonly Observable<T> _observable;
        private readonly Slider _slider;

        private BindDirection _direction = BindDirection.TwoWay;
        private Func<T, float> _toUI;
        private Func<float, T> _fromUI;
        private IFormatProvider _formatProvider;

        private Observable<bool> _interactableObs;
        private bool? _interactableFixed;

        private Observable<bool> _enabledObs;
        private bool? _enabledFixed;

        private Observable<float> _minObs;
        private float? _minFixed;

        private Observable<float> _maxObs;
        private float? _maxFixed;

        private Observable<bool> _wholeNumbersObs;
        private bool? _wholeNumbersFixed;

        private MonoBehaviour _owner;
        private BindingOptions _options;

        public SliderBindingBuilder(Observable<T> observable, Slider slider)
        {
            _observable = observable ?? throw new ArgumentNullException(nameof(observable));
            _slider = slider ?? throw new ArgumentNullException(nameof(slider));
        }

        /// <summary>
        /// Configure the value binding direction (TwoWay by default).
        /// </summary>
        public SliderBindingBuilder<T> Value(BindDirection direction = BindDirection.TwoWay)
        {
            _direction = direction;
            return this;
        }

        /// <summary>
        /// Optional converter from T to float (UI).
        /// </summary>
        public SliderBindingBuilder<T> ToUI(Func<T, float> toUI)
        {
            _toUI = toUI;
            return this;
        }

        /// <summary>
        /// Optional converter from float (UI) to T (VM).
        /// </summary>
        public SliderBindingBuilder<T> FromUI(Func<float, T> fromUI)
        {
            _fromUI = fromUI;
            return this;
        }

        /// <summary>
        /// Optional IFormatProvider used by default conversion paths.
        /// </summary>
        public SliderBindingBuilder<T> FormatProvider(IFormatProvider provider)
        {
            _formatProvider = provider;
            return this;
        }

        public SliderBindingBuilder<T> Interactable(Observable<bool> interactableObservable)
        {
            _interactableObs = interactableObservable;
            _interactableFixed = null;
            return this;
        }

        public SliderBindingBuilder<T> Interactable(bool interactable)
        {
            _interactableFixed = interactable;
            _interactableObs = null;
            return this;
        }

        public SliderBindingBuilder<T> Enabled(Observable<bool> enabledObservable)
        {
            _enabledObs = enabledObservable;
            _enabledFixed = null;
            return this;
        }

        public SliderBindingBuilder<T> Enabled(bool enabled)
        {
            _enabledFixed = enabled;
            _enabledObs = null;
            return this;
        }

        public SliderBindingBuilder<T> Min(Observable<float> minObservable)
        {
            _minObs = minObservable;
            _minFixed = null;
            return this;
        }

        public SliderBindingBuilder<T> Min(float min)
        {
            _minFixed = min;
            _minObs = null;
            return this;
        }

        public SliderBindingBuilder<T> Max(Observable<float> maxObservable)
        {
            _maxObs = maxObservable;
            _maxFixed = null;
            return this;
        }

        public SliderBindingBuilder<T> Max(float max)
        {
            _maxFixed = max;
            _maxObs = null;
            return this;
        }

        public SliderBindingBuilder<T> WholeNumbers(Observable<bool> wholeNumbersObservable)
        {
            _wholeNumbersObs = wholeNumbersObservable;
            _wholeNumbersFixed = null;
            return this;
        }

        public SliderBindingBuilder<T> WholeNumbers(bool wholeNumbers)
        {
            _wholeNumbersFixed = wholeNumbers;
            _wholeNumbersObs = null;
            return this;
        }

        public SliderBindingBuilder<T> Owner(MonoBehaviour owner)
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

            // Value
            handles.Add(_slider.BindValue(
                _observable,
                _direction,
                _owner,
                _toUI,
                _fromUI,
                provider,
                _options));

            // Interactable
            if (_interactableObs != null)
                handles.Add(_slider.BindInteractable(_interactableObs, _owner, _options));
            else if (_interactableFixed.HasValue)
                _slider.interactable = _interactableFixed.Value;

            // Enabled
            if (_enabledObs != null)
                handles.Add(_slider.BindEnabled(_enabledObs, _owner, _options));
            else if (_enabledFixed.HasValue)
                _slider.enabled = _enabledFixed.Value;

            // Min/Max
            if (_minObs != null)
                handles.Add(_slider.BindMinValue(_minObs, _owner, _options));
            else if (_minFixed.HasValue)
                _slider.minValue = _minFixed.Value;

            if (_maxObs != null)
                handles.Add(_slider.BindMaxValue(_maxObs, _owner, _options));
            else if (_maxFixed.HasValue)
                _slider.maxValue = _maxFixed.Value;

            // WholeNumbers
            if (_wholeNumbersObs != null)
                handles.Add(_slider.BindWholeNumbers(_wholeNumbersObs, _owner, _options));
            else if (_wholeNumbersFixed.HasValue)
                _slider.wholeNumbers = _wholeNumbersFixed.Value;

            // Ensure value respects new min/max/wholeNumbers on first pass
            if (_wholeNumbersFixed == true) _slider.value = Mathf.Round(_slider.value);
            if (_minFixed.HasValue && _slider.value < _slider.minValue) _slider.value = _slider.minValue;
            if (_maxFixed.HasValue && _slider.value > _slider.maxValue) _slider.value = _slider.maxValue;

            return new CompositeBindingHandle(handles);
        }

        public CompositeBindingHandle WithDefaultOptions()
        {
            return With(null);
        }
    }
}