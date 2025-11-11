using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace LegendaryTools.Reactive.UGUI
{
    /// <summary>
    /// Fluent builder for UnityEngine.UI.Image.
    /// You can attach multiple property bindings in one chain:
    /// observable.Bind(image)
    ///   .Color(colorObs, TwoWay, LateUpdate, eps: 0.001f)
    ///   .Sprite(spriteObs, TwoWay, LateUpdate)
    ///   .FillAmount(fillObs, TwoWay, LateUpdate, eps: 0.0001f)
    ///   .Enabled(enabledObs, TwoWay, Update)
    ///   .RaycastTarget(raycastObs, TwoWay, Update)
    ///   .Owner(this)
    ///   .With(options);
    /// </summary>
    public sealed class ImageBindingBuilder<TValue>
        where TValue : IEquatable<TValue>, IComparable<TValue>, IComparable, IConvertible
    {
        private readonly Observable<TValue> _source; // carrier only
        private readonly Image _image;

        private Observable<Color> _colorObs;
        private BindDirection _colorDir = BindDirection.TwoWay;
        private UpdatePhase _colorPhase = UpdatePhase.LateUpdate;
        private Func<Color, Color> _colorToUI;
        private Func<Color, Color> _colorFromUI;
        private float _colorEps = 0.001f;

        private Observable<Sprite> _spriteObs;
        private BindDirection _spriteDir = BindDirection.TwoWay;
        private UpdatePhase _spritePhase = UpdatePhase.LateUpdate;
        private Func<Sprite, Sprite> _spriteToUI;
        private Func<Sprite, Sprite> _spriteFromUI;

        private Observable<float> _fillObs;
        private BindDirection _fillDir = BindDirection.TwoWay;
        private UpdatePhase _fillPhase = UpdatePhase.LateUpdate;
        private Func<float, float> _fillToUI;
        private Func<float, float> _fillFromUI;
        private float _fillEps = 0.0001f;
        private bool _fillClamp01 = true;

        private Observable<bool> _enabledObs;
        private BindDirection _enabledDir = BindDirection.TwoWay;
        private UpdatePhase _enabledPhase = UpdatePhase.Update;
        private Func<bool, bool> _enabledToUI;
        private Func<bool, bool> _enabledFromUI;

        private Observable<bool> _raycastObs;
        private BindDirection _raycastDir = BindDirection.TwoWay;
        private UpdatePhase _raycastPhase = UpdatePhase.Update;
        private Func<bool, bool> _raycastToUI;
        private Func<bool, bool> _raycastFromUI;

        private MonoBehaviour _owner;
        private BindingOptions _options;

        public ImageBindingBuilder(Observable<TValue> source, Image image)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _image = image ?? throw new ArgumentNullException(nameof(image));
        }

        // -------------------
        // Color
        // -------------------
        public ImageBindingBuilder<TValue> Color(
            Observable<Color> observable,
            BindDirection direction = BindDirection.TwoWay,
            UpdatePhase phase = UpdatePhase.LateUpdate,
            float eps = 0.001f)
        {
            _colorObs = observable;
            _colorDir = direction;
            _colorPhase = phase;
            _colorEps = eps;
            return this;
        }

        public ImageBindingBuilder<TValue> ColorConverters(Func<Color, Color> toUI = null,
            Func<Color, Color> fromUI = null)
        {
            _colorToUI = toUI;
            _colorFromUI = fromUI;
            return this;
        }

        // -------------------
        // Sprite
        // -------------------
        public ImageBindingBuilder<TValue> Sprite(
            Observable<Sprite> observable,
            BindDirection direction = BindDirection.TwoWay,
            UpdatePhase phase = UpdatePhase.LateUpdate)
        {
            _spriteObs = observable;
            _spriteDir = direction;
            _spritePhase = phase;
            return this;
        }

        public ImageBindingBuilder<TValue> SpriteConverters(Func<Sprite, Sprite> toUI = null,
            Func<Sprite, Sprite> fromUI = null)
        {
            _spriteToUI = toUI;
            _spriteFromUI = fromUI;
            return this;
        }

        // -------------------
        // FillAmount
        // -------------------
        public ImageBindingBuilder<TValue> FillAmount(
            Observable<float> observable,
            BindDirection direction = BindDirection.TwoWay,
            UpdatePhase phase = UpdatePhase.LateUpdate,
            float eps = 0.0001f,
            bool clamp01 = true)
        {
            _fillObs = observable;
            _fillDir = direction;
            _fillPhase = phase;
            _fillEps = eps;
            _fillClamp01 = clamp01;
            return this;
        }

        public ImageBindingBuilder<TValue> FillConverters(Func<float, float> toUI = null,
            Func<float, float> fromUI = null)
        {
            _fillToUI = toUI;
            _fillFromUI = fromUI;
            return this;
        }

        // -------------------
        // Enabled
        // -------------------
        public ImageBindingBuilder<TValue> Enabled(
            Observable<bool> observable,
            BindDirection direction = BindDirection.TwoWay,
            UpdatePhase phase = UpdatePhase.Update)
        {
            _enabledObs = observable;
            _enabledDir = direction;
            _enabledPhase = phase;
            return this;
        }

        public ImageBindingBuilder<TValue> EnabledConverters(Func<bool, bool> toUI = null,
            Func<bool, bool> fromUI = null)
        {
            _enabledToUI = toUI;
            _enabledFromUI = fromUI;
            return this;
        }

        // -------------------
        // RaycastTarget
        // -------------------
        public ImageBindingBuilder<TValue> RaycastTarget(
            Observable<bool> observable,
            BindDirection direction = BindDirection.TwoWay,
            UpdatePhase phase = UpdatePhase.Update)
        {
            _raycastObs = observable;
            _raycastDir = direction;
            _raycastPhase = phase;
            return this;
        }

        public ImageBindingBuilder<TValue> RaycastConverters(Func<bool, bool> toUI = null,
            Func<bool, bool> fromUI = null)
        {
            _raycastToUI = toUI;
            _raycastFromUI = fromUI;
            return this;
        }

        // -------------------
        // Lifetime / build
        // -------------------
        public ImageBindingBuilder<TValue> Owner(MonoBehaviour owner)
        {
            _owner = owner;
            return this;
        }

        public CompositeBindingHandle With(BindingOptions options = null)
        {
            _options ??= options ?? new BindingOptions();
            List<BindingHandle> handles = new();

            // Color
            if (_colorObs != null)
                handles.Add(_image.BindColor(
                    _colorObs,
                    _colorDir,
                    _colorPhase,
                    _owner,
                    _options,
                    _colorToUI,
                    _colorFromUI,
                    _colorEps));

            // Sprite
            if (_spriteObs != null)
                handles.Add(_image.BindSprite(
                    _spriteObs,
                    _spriteDir,
                    _spritePhase,
                    _owner,
                    _options,
                    _spriteToUI,
                    _spriteFromUI));

            // FillAmount
            if (_fillObs != null)
                handles.Add(_image.BindFillAmount(
                    _fillObs,
                    _fillDir,
                    _fillPhase,
                    _owner,
                    _options,
                    _fillToUI,
                    _fillFromUI,
                    _fillEps,
                    _fillClamp01));

            // Enabled
            if (_enabledObs != null)
                handles.Add(_image.BindEnabled(
                    _enabledObs,
                    _enabledDir,
                    _enabledPhase,
                    _owner,
                    _options,
                    _enabledToUI,
                    _enabledFromUI));

            // RaycastTarget
            if (_raycastObs != null)
                handles.Add(_image.BindRaycastTarget(
                    _raycastObs,
                    _raycastDir,
                    _raycastPhase,
                    _owner,
                    _options,
                    _raycastToUI,
                    _raycastFromUI));

            return new CompositeBindingHandle(handles);
        }

        public CompositeBindingHandle WithDefaultOptions()
        {
            return With(null);
        }
    }
}