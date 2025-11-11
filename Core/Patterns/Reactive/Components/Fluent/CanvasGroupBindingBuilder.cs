using System;
using System.Collections.Generic;
using UnityEngine;

namespace LegendaryTools.Reactive.UGUI
{
    /// <summary>
    /// Fluent builder for UnityEngine.CanvasGroup.
    /// Example:
    /// carrier.Bind(canvasGroup)
    ///   .Alpha(alphaObs, TwoWay, LateUpdate, eps: 0.0001f)
    ///   .Interactable(interactableObs, TwoWay, Update)
    ///   .BlocksRaycasts(blocksObs, TwoWay, Update)
    ///   .IgnoreParentGroups(ignoreObs, TwoWay, Update)
    ///   .Enabled(enabledObs, TwoWay, Update)
    ///   .Owner(this)
    ///   .With(options);
    ///
    /// 'carrier' is any Observable used only to start the chain.
    /// </summary>
    public sealed class CanvasGroupBindingBuilder<TValue>
        where TValue : IEquatable<TValue>, IComparable<TValue>, IComparable, IConvertible
    {
        private readonly Observable<TValue> _source; // chain carrier only
        private readonly CanvasGroup _group;

        private Observable<float> _alphaObs;
        private BindDirection _alphaDir = BindDirection.TwoWay;
        private UpdatePhase _alphaPhase = UpdatePhase.LateUpdate;
        private Func<float, float> _alphaToUI;
        private Func<float, float> _alphaFromUI;
        private float _alphaEps = 0.0001f;
        private bool _alphaClamp01 = true;

        private Observable<bool> _interactableObs;
        private BindDirection _interactableDir = BindDirection.TwoWay;
        private UpdatePhase _interactablePhase = UpdatePhase.Update;
        private Func<bool, bool> _interactableToUI;
        private Func<bool, bool> _interactableFromUI;

        private Observable<bool> _blocksObs;
        private BindDirection _blocksDir = BindDirection.TwoWay;
        private UpdatePhase _blocksPhase = UpdatePhase.Update;
        private Func<bool, bool> _blocksToUI;
        private Func<bool, bool> _blocksFromUI;

        private Observable<bool> _ignoreObs;
        private BindDirection _ignoreDir = BindDirection.TwoWay;
        private UpdatePhase _ignorePhase = UpdatePhase.Update;
        private Func<bool, bool> _ignoreToUI;
        private Func<bool, bool> _ignoreFromUI;

        private Observable<bool> _enabledObs;
        private BindDirection _enabledDir = BindDirection.TwoWay;
        private UpdatePhase _enabledPhase = UpdatePhase.Update;
        private Func<bool, bool> _enabledToUI;
        private Func<bool, bool> _enabledFromUI;

        private MonoBehaviour _owner;
        private BindingOptions _options;

        public CanvasGroupBindingBuilder(Observable<TValue> source, CanvasGroup group)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _group = group ?? throw new ArgumentNullException(nameof(group));
        }

        // Alpha
        public CanvasGroupBindingBuilder<TValue> Alpha(
            Observable<float> observable,
            BindDirection direction = BindDirection.TwoWay,
            UpdatePhase phase = UpdatePhase.LateUpdate,
            float eps = 0.0001f,
            bool clamp01 = true)
        {
            _alphaObs = observable;
            _alphaDir = direction;
            _alphaPhase = phase;
            _alphaEps = eps;
            _alphaClamp01 = clamp01;
            return this;
        }

        public CanvasGroupBindingBuilder<TValue> AlphaConverters(Func<float, float> toUI = null,
            Func<float, float> fromUI = null)
        {
            _alphaToUI = toUI;
            _alphaFromUI = fromUI;
            return this;
        }

        // Interactable
        public CanvasGroupBindingBuilder<TValue> Interactable(
            Observable<bool> observable,
            BindDirection direction = BindDirection.TwoWay,
            UpdatePhase phase = UpdatePhase.Update)
        {
            _interactableObs = observable;
            _interactableDir = direction;
            _interactablePhase = phase;
            return this;
        }

        public CanvasGroupBindingBuilder<TValue> InteractableConverters(Func<bool, bool> toUI = null,
            Func<bool, bool> fromUI = null)
        {
            _interactableToUI = toUI;
            _interactableFromUI = fromUI;
            return this;
        }

        // BlocksRaycasts
        public CanvasGroupBindingBuilder<TValue> BlocksRaycasts(
            Observable<bool> observable,
            BindDirection direction = BindDirection.TwoWay,
            UpdatePhase phase = UpdatePhase.Update)
        {
            _blocksObs = observable;
            _blocksDir = direction;
            _blocksPhase = phase;
            return this;
        }

        public CanvasGroupBindingBuilder<TValue> BlocksConverters(Func<bool, bool> toUI = null,
            Func<bool, bool> fromUI = null)
        {
            _blocksToUI = toUI;
            _blocksFromUI = fromUI;
            return this;
        }

        // IgnoreParentGroups
        public CanvasGroupBindingBuilder<TValue> IgnoreParentGroups(
            Observable<bool> observable,
            BindDirection direction = BindDirection.TwoWay,
            UpdatePhase phase = UpdatePhase.Update)
        {
            _ignoreObs = observable;
            _ignoreDir = direction;
            _ignorePhase = phase;
            return this;
        }

        public CanvasGroupBindingBuilder<TValue> IgnoreConverters(Func<bool, bool> toUI = null,
            Func<bool, bool> fromUI = null)
        {
            _ignoreToUI = toUI;
            _ignoreFromUI = fromUI;
            return this;
        }

        // Enabled
        public CanvasGroupBindingBuilder<TValue> Enabled(
            Observable<bool> observable,
            BindDirection direction = BindDirection.TwoWay,
            UpdatePhase phase = UpdatePhase.Update)
        {
            _enabledObs = observable;
            _enabledDir = direction;
            _enabledPhase = phase;
            return this;
        }

        public CanvasGroupBindingBuilder<TValue> EnabledConverters(Func<bool, bool> toUI = null,
            Func<bool, bool> fromUI = null)
        {
            _enabledToUI = toUI;
            _enabledFromUI = fromUI;
            return this;
        }

        // Lifetime / Build
        public CanvasGroupBindingBuilder<TValue> Owner(MonoBehaviour owner)
        {
            _owner = owner;
            return this;
        }

        public CompositeBindingHandle With(BindingOptions options = null)
        {
            _options ??= options ?? new BindingOptions();
            List<BindingHandle> handles = new();

            if (_alphaObs != null)
                handles.Add(_group.BindAlpha(
                    _alphaObs,
                    _alphaDir,
                    _alphaPhase,
                    _owner,
                    _options,
                    _alphaToUI,
                    _alphaFromUI,
                    _alphaEps,
                    _alphaClamp01));

            if (_interactableObs != null)
                handles.Add(_group.BindInteractable(
                    _interactableObs,
                    _interactableDir,
                    _interactablePhase,
                    _owner,
                    _options,
                    _interactableToUI,
                    _interactableFromUI));

            if (_blocksObs != null)
                handles.Add(_group.BindBlocksRaycasts(
                    _blocksObs,
                    _blocksDir,
                    _blocksPhase,
                    _owner,
                    _options,
                    _blocksToUI,
                    _blocksFromUI));

            if (_ignoreObs != null)
                handles.Add(_group.BindIgnoreParentGroups(
                    _ignoreObs,
                    _ignoreDir,
                    _ignorePhase,
                    _owner,
                    _options,
                    _ignoreToUI,
                    _ignoreFromUI));

            if (_enabledObs != null)
                handles.Add(_group.BindEnabled(
                    _enabledObs,
                    _enabledDir,
                    _enabledPhase,
                    _owner,
                    _options,
                    _enabledToUI,
                    _enabledFromUI));

            return new CompositeBindingHandle(handles);
        }

        public CompositeBindingHandle WithDefaultOptions()
        {
            return With(null);
        }
    }
}