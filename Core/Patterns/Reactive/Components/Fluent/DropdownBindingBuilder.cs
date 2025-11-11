using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LegendaryTools.Reactive.TMPro
{
    /// <summary>
    /// Fluent builder for TMP_Dropdown.
    /// Example:
    /// items.Bind(dropdown)
    ///   .Options(label: it => it.Name, sprite: it => it.Icon)
    ///   .SelectedItem(selectedItemObs, BindDirection.TwoWay)
    ///   .SelectedIndex(selectedIndexObs, BindDirection.ToUI)
    ///   .Interactable(canUseObs)
    ///   .Enabled(true)
    ///   .Owner(this)
    ///   .With(options);
    ///
    /// If both SelectedItem and SelectedIndex are configured, SelectedItem takes precedence
    /// to avoid conflicting bindings on the same target property.
    /// </summary>
    public sealed class DropdownBindingBuilder<TItem>
    {
        private readonly ObservableList<TItem> _optionsSource;
        private readonly TMP_Dropdown _dropdown;

        private Func<TItem, string> _labelSelector = item => item?.ToString() ?? string.Empty;
        private Func<TItem, Sprite> _spriteSelector;

        private Observable<TItem> _selectedItemObs;
        private BindDirection _selectedItemDir = BindDirection.TwoWay;
        private EqualityComparer<TItem> _itemComparer = EqualityComparer<TItem>.Default;

        private Observable<int> _selectedIndexObs;
        private BindDirection _selectedIndexDir = BindDirection.TwoWay;

        private Observable<bool> _interactableObs;
        private bool? _interactableFixed;

        private Observable<bool> _enabledObs;
        private bool? _enabledFixed;

        private MonoBehaviour _owner;
        private BindingOptions _options;

        public DropdownBindingBuilder(ObservableList<TItem> optionsSource, TMP_Dropdown dropdown)
        {
            _optionsSource = optionsSource ?? throw new ArgumentNullException(nameof(optionsSource));
            _dropdown = dropdown ?? throw new ArgumentNullException(nameof(dropdown));
        }

        /// <summary>
        /// Configure how to build OptionData from each TItem.
        /// </summary>
        public DropdownBindingBuilder<TItem> Options(Func<TItem, string> label, Func<TItem, Sprite> sprite = null)
        {
            _labelSelector = label ?? _labelSelector;
            _spriteSelector = sprite;
            return this;
        }

        /// <summary>
        /// Bind selection as item.
        /// </summary>
        public DropdownBindingBuilder<TItem> SelectedItem(Observable<TItem> observable,
            BindDirection dir = BindDirection.TwoWay, EqualityComparer<TItem> comparer = null)
        {
            _selectedItemObs = observable;
            _selectedItemDir = dir;
            _itemComparer = comparer ?? _itemComparer;
            return this;
        }

        /// <summary>
        /// Bind selection as index.
        /// </summary>
        public DropdownBindingBuilder<TItem> SelectedIndex(Observable<int> observable,
            BindDirection dir = BindDirection.TwoWay)
        {
            _selectedIndexObs = observable;
            _selectedIndexDir = dir;
            return this;
        }

        public DropdownBindingBuilder<TItem> Interactable(Observable<bool> interactableObservable)
        {
            _interactableObs = interactableObservable;
            _interactableFixed = null;
            return this;
        }

        public DropdownBindingBuilder<TItem> Interactable(bool interactable)
        {
            _interactableFixed = interactable;
            _interactableObs = null;
            return this;
        }

        public DropdownBindingBuilder<TItem> Enabled(Observable<bool> enabledObservable)
        {
            _enabledObs = enabledObservable;
            _enabledFixed = null;
            return this;
        }

        public DropdownBindingBuilder<TItem> Enabled(bool enabled)
        {
            _enabledFixed = enabled;
            _enabledObs = null;
            return this;
        }

        public DropdownBindingBuilder<TItem> Owner(MonoBehaviour owner)
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

            // Options
            handles.Add(_dropdown.BindOptions(_optionsSource, _labelSelector, _spriteSelector, _owner, _options));

            // Selection (prefer SelectedItem if both provided)
            if (_selectedItemObs != null)
                handles.Add(_dropdown.BindSelectedItem(_selectedItemObs, _optionsSource, _itemComparer,
                    _selectedItemDir, _owner, _options));
            else if (_selectedIndexObs != null)
                handles.Add(_dropdown.BindSelectedIndex(_selectedIndexObs, _selectedIndexDir, _owner, _options));

            // Interactable
            if (_interactableObs != null)
                handles.Add(_dropdown.BindInteractable(_interactableObs, _owner, _options));
            else if (_interactableFixed.HasValue)
                _dropdown.interactable = _interactableFixed.Value;

            // Enabled
            if (_enabledObs != null)
                handles.Add(_dropdown.BindEnabled(_enabledObs, _owner, _options));
            else if (_enabledFixed.HasValue)
                _dropdown.enabled = _enabledFixed.Value;

            return new CompositeBindingHandle(handles);
        }

        public CompositeBindingHandle WithDefaultOptions()
        {
            return With(null);
        }
    }
}