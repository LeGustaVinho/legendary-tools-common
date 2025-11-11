using System;
using System.Collections.Generic;
using UnityEngine;
using LegendaryTools.Reactive;

namespace LegendaryTools.Reactive.Unity
{
    /// <summary>
    /// Fluent builder for Transform bindings (Position/Rotation Euler/Rotation Quaternion/Scale/UniformScale).
    /// This builder infers the binding based on the TSource type:
    /// - Observable<Vector3>: Position(), RotationEuler(), Scale()
    /// - Observable<Quaternion>: Rotation()
    /// - Observable<float>: UniformScale()
    /// </summary>
    public sealed class TransformBindingBuilder<TSource>
    {
        private readonly Observable<TSource> _source;
        private readonly Transform _target;

        private enum Kind
        {
            None,
            Position,
            RotationEuler,
            RotationQuat,
            Scale,
            UniformScale
        }

        private Kind _kind = Kind.None;

        // Common parameters
        private Space _space = Space.World;
        private BindDirection _direction = BindDirection.TwoWay;
        private UpdatePhase _phase = UpdatePhase.Update;

        private MonoBehaviour _owner;
        private BindingOptions _options;

        // Converters & epsilons
        private Func<Vector3, Vector3> _posToUI;
        private Func<Vector3, Vector3> _posFromUI;
        private float _posEps = 1e-5f;

        private Func<Vector3, Vector3> _eulerToUI;
        private Func<Vector3, Vector3> _eulerFromUI;
        private float _eulerEps = 0.01f;

        private Func<Quaternion, Quaternion> _quatToUI;
        private Func<Quaternion, Quaternion> _quatFromUI;
        private float _quatAngleEps = 0.01f;

        private Func<Vector3, Vector3> _scaleToUI;
        private Func<Vector3, Vector3> _scaleFromUI;
        private float _scaleEps = 1e-5f;

        private Func<float, float> _uScaleToUI;
        private Func<float, float> _uScaleFromUI;
        private float _uScaleEps = 1e-5f;

        public TransformBindingBuilder(Observable<TSource> source, Transform target)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _target = target ?? throw new ArgumentNullException(nameof(target));
        }

        // -------------------
        // Selectors
        // -------------------
        public TransformBindingBuilder<TSource> Position(Space space = Space.World,
            BindDirection direction = BindDirection.TwoWay, UpdatePhase phase = UpdatePhase.Update)
        {
            _kind = Kind.Position;
            _space = space;
            _direction = direction;
            _phase = phase;
            return this;
        }

        public TransformBindingBuilder<TSource> RotationEuler(Space space = Space.World,
            BindDirection direction = BindDirection.TwoWay, UpdatePhase phase = UpdatePhase.Update)
        {
            _kind = Kind.RotationEuler;
            _space = space;
            _direction = direction;
            _phase = phase;
            return this;
        }

        public TransformBindingBuilder<TSource> Rotation(Space space = Space.World,
            BindDirection direction = BindDirection.TwoWay, UpdatePhase phase = UpdatePhase.Update)
        {
            _kind = Kind.RotationQuat;
            _space = space;
            _direction = direction;
            _phase = phase;
            return this;
        }

        public TransformBindingBuilder<TSource> Scale(BindDirection direction = BindDirection.TwoWay,
            UpdatePhase phase = UpdatePhase.Update)
        {
            _kind = Kind.Scale;
            _direction = direction;
            _phase = phase;
            return this;
        }

        public TransformBindingBuilder<TSource> UniformScale(BindDirection direction = BindDirection.TwoWay,
            UpdatePhase phase = UpdatePhase.Update)
        {
            _kind = Kind.UniformScale;
            _direction = direction;
            _phase = phase;
            return this;
        }

        // -------------------
        // Converters & tuning
        // -------------------
        public TransformBindingBuilder<TSource> ToUI(Func<Vector3, Vector3> fn)
        {
            _posToUI = fn;
            _eulerToUI = fn;
            _scaleToUI = fn;
            return this;
        }

        public TransformBindingBuilder<TSource> FromUI(Func<Vector3, Vector3> fn)
        {
            _posFromUI = fn;
            _eulerFromUI = fn;
            _scaleFromUI = fn;
            return this;
        }

        public TransformBindingBuilder<TSource> ToUI(Func<Quaternion, Quaternion> fn)
        {
            _quatToUI = fn;
            return this;
        }

        public TransformBindingBuilder<TSource> FromUI(Func<Quaternion, Quaternion> fn)
        {
            _quatFromUI = fn;
            return this;
        }

        public TransformBindingBuilder<TSource> ToUI(Func<float, float> fn)
        {
            _uScaleToUI = fn;
            return this;
        }

        public TransformBindingBuilder<TSource> FromUI(Func<float, float> fn)
        {
            _uScaleFromUI = fn;
            return this;
        }

        public TransformBindingBuilder<TSource> Epsilon(float positionEps = 1e-5f, float eulerEpsDeg = 0.01f,
            float quatAngleEpsDeg = 0.01f, float scaleEps = 1e-5f, float uniformScaleEps = 1e-5f)
        {
            _posEps = positionEps;
            _eulerEps = eulerEpsDeg;
            _quatAngleEps = quatAngleEpsDeg;
            _scaleEps = scaleEps;
            _uScaleEps = uniformScaleEps;
            return this;
        }

        public TransformBindingBuilder<TSource> Owner(MonoBehaviour owner)
        {
            _owner = owner;
            return this;
        }

        // -------------------
        // Build
        // -------------------
        public CompositeBindingHandle With(BindingOptions options = null)
        {
            _options ??= options ?? new BindingOptions();
            List<BindingHandle> handles = new();

            // Dispatch based on TSource + selected Kind
            if (typeof(TSource) == typeof(Vector3))
            {
                Observable<Vector3> obs = (Observable<Vector3>)(object)_source;

                switch (_kind)
                {
                    case Kind.Position:
                        handles.Add(_target.BindPosition(obs, _space, _direction, _phase, _owner, _options, _posToUI,
                            _posFromUI, _posEps));
                        break;
                    case Kind.RotationEuler:
                        handles.Add(_target.BindRotationEuler(obs, _space, _direction, _phase, _owner, _options,
                            _eulerToUI, _eulerFromUI, _eulerEps));
                        break;
                    case Kind.Scale:
                        handles.Add(_target.BindScale(obs, _direction, _phase, _owner, _options, _scaleToUI,
                            _scaleFromUI, _scaleEps));
                        break;
                    default:
                        throw new InvalidOperationException(
                            "For Observable<Vector3>, call Position(), RotationEuler() or Scale().");
                }
            }
            else if (typeof(TSource) == typeof(Quaternion))
            {
                Observable<Quaternion> obs = (Observable<Quaternion>)(object)_source;
                if (_kind != Kind.RotationQuat)
                    throw new InvalidOperationException("For Observable<Quaternion>, call Rotation().");

                handles.Add(_target.BindRotation(obs, _space, _direction, _phase, _owner, _options, _quatToUI,
                    _quatFromUI, _quatAngleEps));
            }
            else if (typeof(TSource) == typeof(float))
            {
                Observable<float> obs = (Observable<float>)(object)_source;
                if (_kind != Kind.UniformScale)
                    throw new InvalidOperationException("For Observable<float>, call UniformScale().");

                handles.Add(_target.BindUniformScale(obs, _direction, _phase, _owner, _options, _uScaleToUI,
                    _uScaleFromUI, _uScaleEps));
            }
            else
            {
                throw new NotSupportedException($"Unsupported observable type: {typeof(TSource).Name}");
            }

            return new CompositeBindingHandle(handles);
        }

        public CompositeBindingHandle WithDefaultOptions()
        {
            return With(null);
        }
    }

    /// <summary>
    /// Fluent builder for GameObject Active binding (Observable<bool>).
    /// </summary>
    public sealed class TransformActiveBindingBuilder
    {
        private readonly Observable<bool> _source;
        private readonly Transform _target;

        private BindDirection _direction = BindDirection.ToUI;
        private MonoBehaviour _owner;
        private BindingOptions _options;

        public TransformActiveBindingBuilder(Observable<bool> source, Transform target)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _target = target ?? throw new ArgumentNullException(nameof(target));
        }

        public TransformActiveBindingBuilder Active(BindDirection direction = BindDirection.ToUI)
        {
            _direction = direction;
            return this;
        }

        public TransformActiveBindingBuilder Owner(MonoBehaviour owner)
        {
            _owner = owner;
            return this;
        }

        public CompositeBindingHandle With(BindingOptions options = null)
        {
            _options ??= options ?? new BindingOptions();
            List<BindingHandle> handles = new()
            {
                _target.BindActive(_source, _direction, _owner, _options)
            };
            return new CompositeBindingHandle(handles);
        }

        public CompositeBindingHandle WithDefaultOptions()
        {
            return With(null);
        }
    }
}