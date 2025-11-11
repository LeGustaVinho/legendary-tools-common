using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace LegendaryTools.Reactive.TMPro
{
    /// <summary>
    /// Fluent builder for TMPro.TMP_Text (e.g., TextMeshProUGUI).
    /// Example:
    /// labelCarrier.Bind(label)
    ///   .Text(nameObs, TwoWay, LateUpdate)
    ///   .Color(colorObs, TwoWay, LateUpdate, eps: 0.001f)
    ///   .FontSize(sizeObs, TwoWay, LateUpdate, eps: 0.001f)
    ///   .Owner(this)
    ///   .With(options);
    ///
    /// 'labelCarrier' is just any Observable used to start the chain (can be the same as one of the props).
    /// </summary>
    public sealed class TmpTextBindingBuilder<TValue>
        where TValue : IEquatable<TValue>, IComparable<TValue>, IComparable, IConvertible
    {
        private readonly Observable<TValue> _source; // chain carrier only
        private readonly TMP_Text _label;

        private Observable<string> _textObs;
        private BindDirection _textDir = BindDirection.TwoWay;
        private UpdatePhase _textPhase = UpdatePhase.LateUpdate;
        private Func<string, string> _textToUI;
        private Func<string, string> _textFromUI;

        private Observable<Color> _colorObs;
        private BindDirection _colorDir = BindDirection.TwoWay;
        private UpdatePhase _colorPhase = UpdatePhase.LateUpdate;
        private Func<Color, Color> _colorToUI;
        private Func<Color, Color> _colorFromUI;
        private float _colorEps = 0.001f;

        private Observable<float> _sizeObs;
        private BindDirection _sizeDir = BindDirection.TwoWay;
        private UpdatePhase _sizePhase = UpdatePhase.LateUpdate;
        private Func<float, float> _sizeToUI;
        private Func<float, float> _sizeFromUI;
        private float _sizeEps = 0.001f;

        private MonoBehaviour _owner;
        private BindingOptions _options;

        public TmpTextBindingBuilder(Observable<TValue> source, TMP_Text label)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _label = label ?? throw new ArgumentNullException(nameof(label));
        }

        // -------------------
        // Text
        // -------------------
        public TmpTextBindingBuilder<TValue> Text(
            Observable<string> observable,
            BindDirection direction = BindDirection.TwoWay,
            UpdatePhase phase = UpdatePhase.LateUpdate)
        {
            _textObs = observable;
            _textDir = direction;
            _textPhase = phase;
            return this;
        }

        public TmpTextBindingBuilder<TValue> TextConverters(Func<string, string> toUI = null,
            Func<string, string> fromUI = null)
        {
            _textToUI = toUI;
            _textFromUI = fromUI;
            return this;
        }

        // -------------------
        // Color
        // -------------------
        public TmpTextBindingBuilder<TValue> Color(
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

        public TmpTextBindingBuilder<TValue> ColorConverters(Func<Color, Color> toUI = null,
            Func<Color, Color> fromUI = null)
        {
            _colorToUI = toUI;
            _colorFromUI = fromUI;
            return this;
        }

        // -------------------
        // Font Size
        // -------------------
        public TmpTextBindingBuilder<TValue> FontSize(
            Observable<float> observable,
            BindDirection direction = BindDirection.TwoWay,
            UpdatePhase phase = UpdatePhase.LateUpdate,
            float eps = 0.001f)
        {
            _sizeObs = observable;
            _sizeDir = direction;
            _sizePhase = phase;
            _sizeEps = eps;
            return this;
        }

        public TmpTextBindingBuilder<TValue> FontSizeConverters(Func<float, float> toUI = null,
            Func<float, float> fromUI = null)
        {
            _sizeToUI = toUI;
            _sizeFromUI = fromUI;
            return this;
        }

        // -------------------
        // Lifetime / Build
        // -------------------
        public TmpTextBindingBuilder<TValue> Owner(MonoBehaviour owner)
        {
            _owner = owner;
            return this;
        }

        public CompositeBindingHandle With(BindingOptions options = null)
        {
            _options ??= options ?? new BindingOptions();
            List<BindingHandle> handles = new();

            if (_textObs != null)
                handles.Add(_label.BindText(
                    _textObs,
                    _textDir,
                    _textPhase,
                    _owner,
                    _options,
                    _textToUI,
                    _textFromUI));

            if (_colorObs != null)
                handles.Add(_label.BindColor(
                    _colorObs,
                    _colorDir,
                    _colorPhase,
                    _owner,
                    _options,
                    _colorToUI,
                    _colorFromUI,
                    _colorEps));

            if (_sizeObs != null)
                handles.Add(_label.BindFontSize(
                    _sizeObs,
                    _sizeDir,
                    _sizePhase,
                    _owner,
                    _options,
                    _sizeToUI,
                    _sizeFromUI,
                    _sizeEps));

            return new CompositeBindingHandle(handles);
        }

        public CompositeBindingHandle WithDefaultOptions()
        {
            return With(null);
        }
    }
}