using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace LegendaryTools.Reactive.UGUI
{
    /// <summary>
    /// Fluent builder for UnityEngine.UI.Image bindings.
    /// Example:
    /// hpPercent.Bind(hpBarImage)
    ///   .FillAmount(obsFloat)   // or .FillAmount(0.75f)
    ///   .Color(obsColor)        // or .Color(Color.green)
    ///   .Sprite(obsSprite)      // or .Sprite(fixedSprite, setNativeSize: true)
    ///   .Enabled(true)          // or .Enabled(obsBool)
    ///   .RaycastTarget(false)   // or .RaycastTarget(obsBool)
    ///   .Owner(this)
    ///   .With(options);
    /// </summary>
    public sealed class ImageBindingBuilder<TValue>
        where TValue : IEquatable<TValue>, IComparable<TValue>, IComparable, IConvertible
    {
        private readonly Observable<TValue> _source;
        private readonly Image _image;

        // Bind targets (observables)
        private Observable<Color> _colorObs;
        private Observable<Sprite> _spriteObs;
        private Observable<bool> _enabledObs;
        private Observable<float> _fillAmountObs;
        private Observable<bool> _raycastTargetObs;

        // Fixed values
        private Color? _colorFixed;
        private Sprite _spriteFixed;
        private bool _spriteFixedNativeSize;
        private bool? _enabledFixed;
        private float? _fillAmountFixed;
        private bool? _raycastFixed;

        // Lifetime & options
        private MonoBehaviour _owner;
        private BindingOptions _options;

        public ImageBindingBuilder(Observable<TValue> source, Image image)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _image = image ?? throw new ArgumentNullException(nameof(image));
        }

        /// <summary>
        /// Binds image color from an Observable.
        /// </summary>
        public ImageBindingBuilder<TValue> Color(Observable<Color> colorObservable)
        {
            _colorObs = colorObservable;
            _colorFixed = null;
            return this;
        }

        /// <summary>
        /// Sets a fixed color (non-reactive).
        /// </summary>
        public ImageBindingBuilder<TValue> Color(Color fixedColor)
        {
            _colorFixed = fixedColor;
            _colorObs = null;
            return this;
        }

        /// <summary>
        /// Binds image sprite from an Observable.
        /// </summary>
        public ImageBindingBuilder<TValue> Sprite(Observable<Sprite> spriteObservable)
        {
            _spriteObs = spriteObservable;
            _spriteFixed = null;
            _spriteFixedNativeSize = false;
            return this;
        }

        /// <summary>
        /// Sets a fixed sprite (non-reactive). Optionally calls SetNativeSize on application.
        /// </summary>
        public ImageBindingBuilder<TValue> Sprite(Sprite fixedSprite, bool setNativeSize = false)
        {
            _spriteFixed = fixedSprite;
            _spriteFixedNativeSize = setNativeSize;
            _spriteObs = null;
            return this;
        }

        /// <summary>
        /// Binds image.enabled from an Observable.
        /// </summary>
        public ImageBindingBuilder<TValue> Enabled(Observable<bool> enabledObservable)
        {
            _enabledObs = enabledObservable;
            _enabledFixed = null;
            return this;
        }

        /// <summary>
        /// Sets a fixed enabled state (non-reactive).
        /// </summary>
        public ImageBindingBuilder<TValue> Enabled(bool enabled)
        {
            _enabledFixed = enabled;
            _enabledObs = null;
            return this;
        }

        /// <summary>
        /// Binds image.fillAmount from an Observable<float>.
        /// </summary>
        public ImageBindingBuilder<TValue> FillAmount(Observable<float> fillObservable)
        {
            _fillAmountObs = fillObservable;
            _fillAmountFixed = null;
            return this;
        }

        /// <summary>
        /// Sets a fixed fill amount.
        /// </summary>
        public ImageBindingBuilder<TValue> FillAmount(float amount)
        {
            _fillAmountFixed = amount;
            _fillAmountObs = null;
            return this;
        }

        /// <summary>
        /// Binds image.raycastTarget from an Observable<bool>.
        /// </summary>
        public ImageBindingBuilder<TValue> RaycastTarget(Observable<bool> raycastTargetObservable)
        {
            _raycastTargetObs = raycastTargetObservable;
            _raycastFixed = null;
            return this;
        }

        /// <summary>
        /// Sets a fixed raycastTarget flag.
        /// </summary>
        public ImageBindingBuilder<TValue> RaycastTarget(bool raycastTarget)
        {
            _raycastFixed = raycastTarget;
            _raycastTargetObs = null;
            return this;
        }

        /// <summary>
        /// Assigns a lifetime owner for auto-unbind/resync and edit-mode gating.
        /// </summary>
        public ImageBindingBuilder<TValue> Owner(MonoBehaviour owner)
        {
            _owner = owner;
            return this;
        }

        /// <summary>
        /// Finalizes the builder, creates all configured bindings, and returns a composite handle.
        /// </summary>
        public CompositeBindingHandle With(BindingOptions options = null)
        {
            _options ??= options ?? new BindingOptions();
            List<BindingHandle> handles = new();

            // Color
            if (_colorObs != null)
                handles.Add(_image.BindColor(_colorObs, _owner, _options));
            else if (_colorFixed.HasValue) _image.color = _colorFixed.Value;

            // Sprite
            if (_spriteObs != null)
            {
                handles.Add(_image.BindSprite(_spriteObs, _owner, _options, false));
            }
            else if (_spriteFixed != null)
            {
                _image.sprite = _spriteFixed;
                if (_spriteFixedNativeSize && _spriteFixed != null) _image.SetNativeSize();
            }

            // Enabled
            if (_enabledObs != null)
                handles.Add(_image.BindEnabled(_enabledObs, _owner, _options));
            else if (_enabledFixed.HasValue) _image.enabled = _enabledFixed.Value;

            // Fill amount
            if (_fillAmountObs != null)
                handles.Add(_image.BindFillAmount(_fillAmountObs, _owner, _options, true));
            else if (_fillAmountFixed.HasValue) _image.fillAmount = Mathf.Clamp01(_fillAmountFixed.Value);

            // RaycastTarget
            if (_raycastTargetObs != null)
                handles.Add(_image.BindRaycastTarget(_raycastTargetObs, _owner, _options));
            else if (_raycastFixed.HasValue) _image.raycastTarget = _raycastFixed.Value;

            return new CompositeBindingHandle(handles);
        }

        public CompositeBindingHandle WithDefaultOptions()
        {
            return With(null);
        }
    }
}