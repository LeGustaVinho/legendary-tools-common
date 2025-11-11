using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace LegendaryTools.Reactive.UGUI
{
    /// <summary>
    /// Fluent builder for UnityEngine.UI.RawImage bindings.
    /// Example:
    /// vm.Bind(rawImage)
    ///   .Texture(obsTexture)         // or .Texture(fixedTex, setNativeSize: true)
    ///   .Color(obsColor)             // or .Color(Color.white)
    ///   .UVRect(obsRect)             // or .UVRect(new Rect(0,0,1,1))
    ///   .Enabled(true)               // or .Enabled(obsBool)
    ///   .RaycastTarget(false)        // or .RaycastTarget(obsBool)
    ///   .Owner(this)
    ///   .With(options);
    /// </summary>
    public sealed class RawImageBindingBuilder<TValue>
        where TValue : IEquatable<TValue>, IComparable<TValue>, IComparable, IConvertible
    {
        private readonly Observable<TValue> _source;
        private readonly RawImage _rawImage;

        // Reactive sources
        private Observable<Texture> _textureObs;
        private Observable<Color> _colorObs;
        private Observable<Rect> _uvRectObs;
        private Observable<bool> _enabledObs;
        private Observable<bool> _raycastObs;

        // Fixed values
        private Texture _textureFixed;
        private bool _textureFixedNativeSize;
        private Color? _colorFixed;
        private Rect? _uvRectFixed;
        private bool? _enabledFixed;
        private bool? _raycastFixed;

        // Lifetime & options
        private MonoBehaviour _owner;
        private BindingOptions _options;

        public RawImageBindingBuilder(Observable<TValue> source, RawImage rawImage)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _rawImage = rawImage ?? throw new ArgumentNullException(nameof(rawImage));
        }

        public RawImageBindingBuilder<TValue> Texture(Observable<Texture> textureObservable)
        {
            _textureObs = textureObservable;
            _textureFixed = null;
            _textureFixedNativeSize = false;
            return this;
        }

        public RawImageBindingBuilder<TValue> Texture(Texture fixedTexture, bool setNativeSize = false)
        {
            _textureFixed = fixedTexture;
            _textureFixedNativeSize = setNativeSize;
            _textureObs = null;
            return this;
        }

        public RawImageBindingBuilder<TValue> Color(Observable<Color> colorObservable)
        {
            _colorObs = colorObservable;
            _colorFixed = null;
            return this;
        }

        public RawImageBindingBuilder<TValue> Color(Color fixedColor)
        {
            _colorFixed = fixedColor;
            _colorObs = null;
            return this;
        }

        public RawImageBindingBuilder<TValue> UVRect(Observable<Rect> uvRectObservable)
        {
            _uvRectObs = uvRectObservable;
            _uvRectFixed = null;
            return this;
        }

        public RawImageBindingBuilder<TValue> UVRect(Rect fixedRect)
        {
            _uvRectFixed = fixedRect;
            _uvRectObs = null;
            return this;
        }

        public RawImageBindingBuilder<TValue> Enabled(Observable<bool> enabledObservable)
        {
            _enabledObs = enabledObservable;
            _enabledFixed = null;
            return this;
        }

        public RawImageBindingBuilder<TValue> Enabled(bool enabled)
        {
            _enabledFixed = enabled;
            _enabledObs = null;
            return this;
        }

        public RawImageBindingBuilder<TValue> RaycastTarget(Observable<bool> raycastObservable)
        {
            _raycastObs = raycastObservable;
            _raycastFixed = null;
            return this;
        }

        public RawImageBindingBuilder<TValue> RaycastTarget(bool raycastTarget)
        {
            _raycastFixed = raycastTarget;
            _raycastObs = null;
            return this;
        }

        public RawImageBindingBuilder<TValue> Owner(MonoBehaviour owner)
        {
            _owner = owner;
            return this;
        }

        public CompositeBindingHandle With(BindingOptions options = null)
        {
            _options ??= options ?? new BindingOptions();
            List<BindingHandle> handles = new();

            // Texture
            if (_textureObs != null)
            {
                handles.Add(_rawImage.BindTexture(_textureObs, _owner, _options, false));
            }
            else if (_textureFixed != null)
            {
                _rawImage.texture = _textureFixed;
                if (_textureFixedNativeSize) _rawImage.SetNativeSize();
            }

            // Color
            if (_colorObs != null)
                handles.Add(_rawImage.BindColor(_colorObs, _owner, _options));
            else if (_colorFixed.HasValue) _rawImage.color = _colorFixed.Value;

            // UVRect
            if (_uvRectObs != null)
                handles.Add(_rawImage.BindUVRect(_uvRectObs, _owner, _options));
            else if (_uvRectFixed.HasValue) _rawImage.uvRect = _uvRectFixed.Value;

            // Enabled
            if (_enabledObs != null)
                handles.Add(_rawImage.BindEnabled(_enabledObs, _owner, _options));
            else if (_enabledFixed.HasValue) _rawImage.enabled = _enabledFixed.Value;

            // Raycast
            if (_raycastObs != null)
                handles.Add(_rawImage.BindRaycastTarget(_raycastObs, _owner, _options));
            else if (_raycastFixed.HasValue) _rawImage.raycastTarget = _raycastFixed.Value;

            return new CompositeBindingHandle(handles);
        }

        public CompositeBindingHandle WithDefaultOptions()
        {
            return With(null);
        }
    }
}