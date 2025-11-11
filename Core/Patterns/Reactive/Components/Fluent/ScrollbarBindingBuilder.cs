using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace LegendaryTools.Reactive.UGUI
{
    /// <summary>
    /// Fluent builder for UnityEngine.UI.Scrollbar.
    /// Example:
    /// carrier.Bind(scrollbar)
    ///   .Size(sizeObs, TwoWay, LateUpdate, eps: 0.0001f, clamp01: true)
    ///   .NumberOfSteps(stepsObs, TwoWay, Update, minSteps: 0)
    ///   .Direction(dirObs, TwoWay, Update)
    ///   .Owner(this)
    ///   .With(options);
    /// 'carrier' is any Observable used only to start the chain.
    /// </summary>
    public sealed class ScrollbarBindingBuilder<TValue>
        where TValue : IEquatable<TValue>, IComparable<TValue>, IComparable, IConvertible
    {
        private readonly Observable<TValue> _source; // chain carrier only
        private readonly Scrollbar _scrollbar;

        private Observable<float> _sizeObs;
        private BindDirection _sizeDir = BindDirection.TwoWay;
        private UpdatePhase _sizePhase = UpdatePhase.LateUpdate;
        private Func<float, float> _sizeToUI;
        private Func<float, float> _sizeFromUI;
        private float _sizeEps = 0.0001f;
        private bool _sizeClamp01 = true;

        private Observable<int> _stepsObs;
        private BindDirection _stepsDir = BindDirection.TwoWay;
        private UpdatePhase _stepsPhase = UpdatePhase.Update;
        private Func<int, int> _stepsToUI;
        private Func<int, int> _stepsFromUI;
        private int _minSteps = 0;

        private Observable<Scrollbar.Direction> _dirObs;
        private BindDirection _dirDir = BindDirection.TwoWay;
        private UpdatePhase _dirPhase = UpdatePhase.Update;
        private Func<Scrollbar.Direction, Scrollbar.Direction> _dirToUI;
        private Func<Scrollbar.Direction, Scrollbar.Direction> _dirFromUI;

        private MonoBehaviour _owner;
        private BindingOptions _options;

        public ScrollbarBindingBuilder(Observable<TValue> source, Scrollbar scrollbar)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _scrollbar = scrollbar ?? throw new ArgumentNullException(nameof(scrollbar));
        }

        // Size
        public ScrollbarBindingBuilder<TValue> Size(
            Observable<float> observable,
            BindDirection direction = BindDirection.TwoWay,
            UpdatePhase phase = UpdatePhase.LateUpdate,
            float eps = 0.0001f,
            bool clamp01 = true)
        {
            _sizeObs = observable;
            _sizeDir = direction;
            _sizePhase = phase;
            _sizeEps = eps;
            _sizeClamp01 = clamp01;
            return this;
        }

        public ScrollbarBindingBuilder<TValue> SizeConverters(Func<float, float> toUI = null,
            Func<float, float> fromUI = null)
        {
            _sizeToUI = toUI;
            _sizeFromUI = fromUI;
            return this;
        }

        // NumberOfSteps
        public ScrollbarBindingBuilder<TValue> NumberOfSteps(
            Observable<int> observable,
            BindDirection direction = BindDirection.TwoWay,
            UpdatePhase phase = UpdatePhase.Update,
            int minSteps = 0)
        {
            _stepsObs = observable;
            _stepsDir = direction;
            _stepsPhase = phase;
            _minSteps = Mathf.Max(0, minSteps);
            return this;
        }

        public ScrollbarBindingBuilder<TValue> StepsConverters(Func<int, int> toUI = null, Func<int, int> fromUI = null)
        {
            _stepsToUI = toUI;
            _stepsFromUI = fromUI;
            return this;
        }

        // Direction
        public ScrollbarBindingBuilder<TValue> Direction(
            Observable<Scrollbar.Direction> observable,
            BindDirection direction = BindDirection.TwoWay,
            UpdatePhase phase = UpdatePhase.Update)
        {
            _dirObs = observable;
            _dirDir = direction;
            _dirPhase = phase;
            return this;
        }

        public ScrollbarBindingBuilder<TValue> DirectionConverters(
            Func<Scrollbar.Direction, Scrollbar.Direction> toUI = null,
            Func<Scrollbar.Direction, Scrollbar.Direction> fromUI = null)
        {
            _dirToUI = toUI;
            _dirFromUI = fromUI;
            return this;
        }

        // Lifetime / Build
        public ScrollbarBindingBuilder<TValue> Owner(MonoBehaviour owner)
        {
            _owner = owner;
            return this;
        }

        public CompositeBindingHandle With(BindingOptions options = null)
        {
            _options ??= options ?? new BindingOptions();
            List<BindingHandle> handles = new();

            if (_sizeObs != null)
                handles.Add(_scrollbar.BindSize(
                    _sizeObs,
                    _sizeDir,
                    _sizePhase,
                    _owner,
                    _options,
                    _sizeToUI,
                    _sizeFromUI,
                    _sizeEps,
                    _sizeClamp01));

            if (_stepsObs != null)
                handles.Add(_scrollbar.BindNumberOfSteps(
                    _stepsObs,
                    _stepsDir,
                    _stepsPhase,
                    _owner,
                    _options,
                    _stepsToUI,
                    _stepsFromUI,
                    _minSteps));

            if (_dirObs != null)
                handles.Add(_scrollbar.BindDirection(
                    _dirObs,
                    _dirDir,
                    _dirPhase,
                    _owner,
                    _options,
                    _dirToUI,
                    _dirFromUI));

            return new CompositeBindingHandle(handles);
        }

        public CompositeBindingHandle WithDefaultOptions()
        {
            return With(null);
        }
    }
}