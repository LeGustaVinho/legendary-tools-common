using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace LegendaryTools.Reactive.TMPro
{
    /// <summary>
    /// Fluent builder for TMPro.TMP_Dropdown focusing on state bindings without native events:
    /// - Interactable
    /// - Enabled
    ///
    /// Example:
    /// carrier.Bind(dropdown)
    ///   .Interactable(interactableObs, TwoWay, Update)
    ///   .Enabled(enabledObs, TwoWay, Update)
    ///   .Owner(this)
    ///   .With(options);
    ///
    /// 'carrier' is any Observable only to start the chain.
    /// </summary>
    public sealed class TmpDropdownBindingBuilder<TValue>
        where TValue : IEquatable<TValue>, IComparable<TValue>, IComparable, IConvertible
    {
        private readonly Observable<TValue> _source; // chain carrier only
        private readonly TMP_Dropdown _dropdown;

        private Observable<bool> _interactableObs;
        private BindDirection _interactableDir = BindDirection.TwoWay;
        private UpdatePhase _interactablePhase = UpdatePhase.Update;
        private Func<bool, bool> _interactableToUI;
        private Func<bool, bool> _interactableFromUI;

        private Observable<bool> _enabledObs;
        private BindDirection _enabledDir = BindDirection.TwoWay;
        private UpdatePhase _enabledPhase = UpdatePhase.Update;
        private Func<bool, bool> _enabledToUI;
        private Func<bool, bool> _enabledFromUI;

        private MonoBehaviour _owner;
        private BindingOptions _options;

        public TmpDropdownBindingBuilder(Observable<TValue> source, TMP_Dropdown dropdown)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _dropdown = dropdown ?? throw new ArgumentNullException(nameof(dropdown));
        }

        // Interactable
        public TmpDropdownBindingBuilder<TValue> Interactable(
            Observable<bool> observable,
            BindDirection direction = BindDirection.TwoWay,
            UpdatePhase phase = UpdatePhase.Update)
        {
            _interactableObs = observable;
            _interactableDir = direction;
            _interactablePhase = phase;
            return this;
        }

        public TmpDropdownBindingBuilder<TValue> InteractableConverters(
            Func<bool, bool> toUI = null,
            Func<bool, bool> fromUI = null)
        {
            _interactableToUI = toUI;
            _interactableFromUI = fromUI;
            return this;
        }

        // Enabled
        public TmpDropdownBindingBuilder<TValue> Enabled(
            Observable<bool> observable,
            BindDirection direction = BindDirection.TwoWay,
            UpdatePhase phase = UpdatePhase.Update)
        {
            _enabledObs = observable;
            _enabledDir = direction;
            _enabledPhase = phase;
            return this;
        }

        public TmpDropdownBindingBuilder<TValue> EnabledConverters(
            Func<bool, bool> toUI = null,
            Func<bool, bool> fromUI = null)
        {
            _enabledToUI = toUI;
            _enabledFromUI = fromUI;
            return this;
        }

        // Lifetime / Build
        public TmpDropdownBindingBuilder<TValue> Owner(MonoBehaviour owner)
        {
            _owner = owner;
            return this;
        }

        public CompositeBindingHandle With(BindingOptions options = null)
        {
            _options ??= options ?? new BindingOptions();
            List<BindingHandle> handles = new();

            if (_interactableObs != null)
                handles.Add(_dropdown.BindInteractable(
                    _interactableObs,
                    _interactableDir,
                    _interactablePhase,
                    _owner,
                    _options,
                    _interactableToUI,
                    _interactableFromUI));

            if (_enabledObs != null)
                handles.Add(_dropdown.BindEnabled(
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