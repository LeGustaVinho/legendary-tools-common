using System;
using System.Collections.Generic;
using UnityEngine;

namespace LegendaryTools.Reactive.UGUI
{
    /// <summary>
    /// Fluent builder for UnityEngine.CanvasGroup.
    /// Example:
    /// vm.Bind(group)
    ///   .Alpha(alphaObs)             // or .Alpha(1f)
    ///   .Interactable(canInteractObs)// or .Interactable(true)
    ///   .BlocksRaycasts(blocksObs)   // or .BlocksRaycasts(false)
    ///   .IgnoreParentGroups(ignoreObs)// or .IgnoreParentGroups(true)
    ///   .Enabled(true)               // or .Enabled(obsBool)
    ///   .Owner(this)
    ///   .With(options);
    /// </summary>
    public sealed class CanvasGroupBindingBuilder<TValue>
        where TValue : IEquatable<TValue>, IComparable<TValue>, IComparable, IConvertible
    {
        private readonly Observable<TValue> _source;
        private readonly CanvasGroup _group;

        private Observable<float> _alphaObs;
        private float? _alphaFixed;
        private bool _alphaClamp01 = true;

        private Observable<bool> _interactableObs;
        private bool? _interactableFixed;

        private Observable<bool> _blocksRaycastsObs;
        private bool? _blocksRaycastsFixed;

        private Observable<bool> _ignoreParentGroupsObs;
        private bool? _ignoreParentGroupsFixed;

        private Observable<bool> _enabledObs;
        private bool? _enabledFixed;

        private MonoBehaviour _owner;
        private BindingOptions _options;

        public CanvasGroupBindingBuilder(Observable<TValue> source, CanvasGroup group)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _group = group ?? throw new ArgumentNullException(nameof(group));
        }

        public CanvasGroupBindingBuilder<TValue> Alpha(Observable<float> alphaObservable, bool clamp01 = true)
        {
            _alphaObs = alphaObservable;
            _alphaFixed = null;
            _alphaClamp01 = clamp01;
            return this;
        }

        public CanvasGroupBindingBuilder<TValue> Alpha(float alpha, bool clamp01 = true)
        {
            _alphaFixed = alpha;
            _alphaObs = null;
            _alphaClamp01 = clamp01;
            return this;
        }

        public CanvasGroupBindingBuilder<TValue> Interactable(Observable<bool> interactableObservable)
        {
            _interactableObs = interactableObservable;
            _interactableFixed = null;
            return this;
        }

        public CanvasGroupBindingBuilder<TValue> Interactable(bool interactable)
        {
            _interactableFixed = interactable;
            _interactableObs = null;
            return this;
        }

        public CanvasGroupBindingBuilder<TValue> BlocksRaycasts(Observable<bool> blocksRaycastsObservable)
        {
            _blocksRaycastsObs = blocksRaycastsObservable;
            _blocksRaycastsFixed = null;
            return this;
        }

        public CanvasGroupBindingBuilder<TValue> BlocksRaycasts(bool blocksRaycasts)
        {
            _blocksRaycastsFixed = blocksRaycasts;
            _blocksRaycastsObs = null;
            return this;
        }

        public CanvasGroupBindingBuilder<TValue> IgnoreParentGroups(Observable<bool> ignoreParentGroupsObservable)
        {
            _ignoreParentGroupsObs = ignoreParentGroupsObservable;
            _ignoreParentGroupsFixed = null;
            return this;
        }

        public CanvasGroupBindingBuilder<TValue> IgnoreParentGroups(bool ignoreParentGroups)
        {
            _ignoreParentGroupsFixed = ignoreParentGroups;
            _ignoreParentGroupsObs = null;
            return this;
        }

        public CanvasGroupBindingBuilder<TValue> Enabled(Observable<bool> enabledObservable)
        {
            _enabledObs = enabledObservable;
            _enabledFixed = null;
            return this;
        }

        public CanvasGroupBindingBuilder<TValue> Enabled(bool enabled)
        {
            _enabledFixed = enabled;
            _enabledObs = null;
            return this;
        }

        public CanvasGroupBindingBuilder<TValue> Owner(MonoBehaviour owner)
        {
            _owner = owner;
            return this;
        }

        public CompositeBindingHandle With(BindingOptions options = null)
        {
            _options ??= options ?? new BindingOptions();
            List<BindingHandle> handles = new();

            // Alpha
            if (_alphaObs != null)
                handles.Add(_group.BindAlpha(_alphaObs, _owner, _options, _alphaClamp01));
            else if (_alphaFixed.HasValue)
                _group.alpha = _alphaClamp01 ? Mathf.Clamp01(_alphaFixed.Value) : _alphaFixed.Value;

            // Interactable
            if (_interactableObs != null)
                handles.Add(_group.BindInteractable(_interactableObs, _owner, _options));
            else if (_interactableFixed.HasValue) _group.interactable = _interactableFixed.Value;

            // BlocksRaycasts
            if (_blocksRaycastsObs != null)
                handles.Add(_group.BindBlocksRaycasts(_blocksRaycastsObs, _owner, _options));
            else if (_blocksRaycastsFixed.HasValue) _group.blocksRaycasts = _blocksRaycastsFixed.Value;

            // IgnoreParentGroups
            if (_ignoreParentGroupsObs != null)
                handles.Add(_group.BindIgnoreParentGroups(_ignoreParentGroupsObs, _owner, _options));
            else if (_ignoreParentGroupsFixed.HasValue) _group.ignoreParentGroups = _ignoreParentGroupsFixed.Value;

            // Enabled
            if (_enabledObs != null)
                handles.Add(_group.BindEnabled(_enabledObs, _owner, _options));
            else if (_enabledFixed.HasValue) _group.enabled = _enabledFixed.Value;

            return new CompositeBindingHandle(handles);
        }

        public CompositeBindingHandle WithDefaultOptions()
        {
            return With(null);
        }
    }
}