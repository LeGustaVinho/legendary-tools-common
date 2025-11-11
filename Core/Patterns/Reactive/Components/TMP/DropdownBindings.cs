// File: DropdownBindings.cs

using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI; // Sprite
using LegendaryTools;
using LegendaryTools.Reactive;

namespace LegendaryTools.Reactive.TMPro
{
    /// <summary>
    /// Extension methods to bind Observables with TMPro.TMP_Dropdown.
    /// - Options binding (incremental) from ObservableList{TItem}
    /// - SelectedIndex (int) with TwoWay/ToUI/FromUI
    /// - SelectedItem (TItem) with TwoWay/ToUI/FromUI using EqualityComparer{TItem}
    /// - Interactable (bool), Enabled (bool)
    /// All bindings integrate with BindingAnchor/BindingOptions and BindingsDebugger via BindingInfo.
    /// </summary>
    public static class DropdownBindings
    {
        // ---------------------------
        // OPTIONS (INCREMENTAL)
        // ---------------------------
        public static BindingHandle BindOptions<TItem>(
            this TMP_Dropdown dropdown,
            ObservableList<TItem> source,
            Func<TItem, string> labelSelector,
            Func<TItem, Sprite> spriteSelector = null,
            MonoBehaviour owner = null,
            BindingOptions options = null)
        {
            if (dropdown == null) throw new ArgumentNullException(nameof(dropdown));
            if (source == null) throw new ArgumentNullException(nameof(source));
            labelSelector ??= item => item?.ToString() ?? string.Empty;

            BindingAnchor anchor = owner != null
                ? owner.GetComponent<BindingAnchor>() ?? owner.gameObject.AddComponent<BindingAnchor>()
                : null;
            options ??= new BindingOptions();

            static TMP_Dropdown.OptionData MakeOption(string text, Sprite sprite)
            {
                // Use widely available ctor and set sprite via property for compatibility
                TMP_Dropdown.OptionData opt = new(text);
                opt.image = sprite;
                return opt;
            }

            void EnsureValidSelection()
            {
                int count = dropdown.options?.Count ?? 0;
                if (count <= 0)
                {
                    dropdown.SetValueWithoutNotify(0);
                    dropdown.RefreshShownValue();
                    return;
                }

                int clamped = Mathf.Clamp(dropdown.value, 0, count - 1);
                if (clamped != dropdown.value)
                    dropdown.SetValueWithoutNotify(clamped);

                dropdown.RefreshShownValue();
            }

            // Initial full build
            void BuildAll()
            {
                if (anchor != null && !anchor.ShouldProcessNow()) return;

                dropdown.options.Clear();
                for (int i = 0; i < source.Count; i++)
                {
                    TItem item = source[i];
                    string text = labelSelector(item) ?? string.Empty;
                    Sprite sprite = spriteSelector != null ? spriteSelector(item) : null;
                    dropdown.options.Add(MakeOption(text, sprite));
                }

                EnsureValidSelection();
            }

            // Incremental handlers
            void OnAdd(ObservableList<TItem> _, TItem item, int index)
            {
                if (anchor != null && !anchor.ShouldProcessNow()) return;

                string text = labelSelector(item) ?? string.Empty;
                Sprite sprite = spriteSelector != null ? spriteSelector(item) : null;

                index = Mathf.Clamp(index, 0, dropdown.options.Count);
                dropdown.options.Insert(index, MakeOption(text, sprite));

                if (index <= dropdown.value) dropdown.SetValueWithoutNotify(dropdown.value + 1);
                dropdown.RefreshShownValue();
            }

            void OnUpdate(ObservableList<TItem> _, TItem oldItem, TItem newItem, int index)
            {
                if (anchor != null && !anchor.ShouldProcessNow()) return;

                if (index < 0 || index >= dropdown.options.Count)
                {
                    BuildAll();
                    return;
                }

                string text = labelSelector(newItem) ?? string.Empty;
                Sprite sprite = spriteSelector != null ? spriteSelector(newItem) : null;

                TMP_Dropdown.OptionData opt = dropdown.options[index];
                opt.text = text;
                opt.image = sprite;

                if (dropdown.value == index)
                    dropdown.RefreshShownValue();
            }

            void OnRemove(ObservableList<TItem> _, TItem removed, int index)
            {
                if (anchor != null && !anchor.ShouldProcessNow()) return;

                if (index < 0 || index >= dropdown.options.Count)
                {
                    BuildAll();
                    return;
                }

                dropdown.options.RemoveAt(index);

                if (index < dropdown.value) dropdown.SetValueWithoutNotify(dropdown.value - 1);
                EnsureValidSelection();
            }

            void OnClear(ObservableList<TItem> _)
            {
                if (anchor != null && !anchor.ShouldProcessNow()) return;

                dropdown.options.Clear();
                EnsureValidSelection();
            }

            void Subscribe()
            {
                BuildAll();
                source.OnAdd += OnAdd;
                source.OnUpdate += OnUpdate;
                source.OnRemove += OnRemove;
                source.OnClear += OnClear;
            }

            void Unsubscribe()
            {
                source.OnAdd -= OnAdd;
                source.OnUpdate -= OnUpdate;
                source.OnRemove -= OnRemove;
                source.OnClear -= OnClear;
            }

            void Resync()
            {
                BuildAll();
            }

            BindingInfo info = new()
            {
                Kind = "TMP_Dropdown.Options",
                Direction = "ToUI",
                Description = $"options ← ObservableList<{typeof(TItem).Name}> (incremental)",
                Target = dropdown,
                Owner = owner,
                Anchor = anchor,
                Options = options,
                GetState = () => $"count={dropdown.options?.Count ?? 0}",
                Tags = new[] { "TMP_Dropdown", "Options" }
            };

            BindingHandle handle = new(Subscribe, Unsubscribe, Resync, anchor, options, info);
            if (owner != null)
            {
                BindingDisposer disposer = owner.GetComponent<BindingDisposer>() ??
                                           owner.gameObject.AddComponent<BindingDisposer>();
                disposer.Add(handle);
            }

            return handle;
        }

        // ---------------------------
        // SELECTED INDEX
        // ---------------------------
        public static BindingHandle BindSelectedIndex(
            this TMP_Dropdown dropdown,
            Observable<int> selectedIndex,
            BindDirection direction = BindDirection.TwoWay,
            MonoBehaviour owner = null,
            BindingOptions options = null)
        {
            if (dropdown == null) throw new ArgumentNullException(nameof(dropdown));
            if (selectedIndex == null) throw new ArgumentNullException(nameof(selectedIndex));

            BindingAnchor anchor = owner != null
                ? owner.GetComponent<BindingAnchor>() ?? owner.gameObject.AddComponent<BindingAnchor>()
                : null;
            options ??= new BindingOptions();

            bool isUpdating = false;

            void ApplyToUI(int idx)
            {
                if (anchor != null && !anchor.ShouldProcessNow()) return;
                try
                {
                    isUpdating = true;
                    int max = Math.Max(0, dropdown.options.Count - 1);
                    int clamped = Mathf.Clamp(idx, 0, max);
                    dropdown.SetValueWithoutNotify(clamped);
                    dropdown.RefreshShownValue();
                }
                finally
                {
                    isUpdating = false;
                }
            }

            void OnObsChanged(IObservable<int> _, int oldV, int newV)
            {
                if (direction == BindDirection.FromUI) return;
                ApplyToUI(newV);
            }

            void OnUIChanged(int idx)
            {
                if (direction == BindDirection.ToUI) return;
                if (isUpdating) return;
                selectedIndex.Value = idx;
            }

            void Subscribe()
            {
                if (direction != BindDirection.FromUI)
                    ApplyToUI(selectedIndex.Value);

                selectedIndex.OnChanged += OnObsChanged;
                dropdown.onValueChanged.AddListener(OnUIChanged);
            }

            void Unsubscribe()
            {
                selectedIndex.OnChanged -= OnObsChanged;
                dropdown.onValueChanged.RemoveListener(OnUIChanged);
            }

            void Resync()
            {
                if (direction != BindDirection.FromUI)
                    ApplyToUI(selectedIndex.Value);
            }

            BindingInfo info = new()
            {
                Kind = "TMP_Dropdown.SelectedIndex",
                Direction = direction.ToString(),
                Description = "value (index) ↔ Observable<int>",
                Target = dropdown,
                Owner = owner,
                Anchor = anchor,
                Options = options,
                GetState = () => $"value={dropdown.value}, options={dropdown.options?.Count ?? 0}",
                Tags = new[] { "TMP_Dropdown", "SelectedIndex" }
            };

            BindingHandle handle = new(Subscribe, Unsubscribe, Resync, anchor, options, info);
            if (owner != null)
            {
                BindingDisposer disposer = owner.GetComponent<BindingDisposer>() ??
                                           owner.gameObject.AddComponent<BindingDisposer>();
                disposer.Add(handle);
            }

            return handle;
        }

        // ---------------------------
        // SELECTED ITEM (NO CONSTRAINTS)
        // ---------------------------
        public static BindingHandle BindSelectedItem<TItem>(
            this TMP_Dropdown dropdown,
            Observable<TItem> selectedItem,
            ObservableList<TItem> optionsSource,
            EqualityComparer<TItem> comparer = null,
            BindDirection direction = BindDirection.TwoWay,
            MonoBehaviour owner = null,
            BindingOptions options = null)
        {
            if (dropdown == null) throw new ArgumentNullException(nameof(dropdown));
            if (selectedItem == null) throw new ArgumentNullException(nameof(selectedItem));
            if (optionsSource == null) throw new ArgumentNullException(nameof(optionsSource));

            comparer ??= EqualityComparer<TItem>.Default;

            BindingAnchor anchor = owner != null
                ? owner.GetComponent<BindingAnchor>() ?? owner.gameObject.AddComponent<BindingAnchor>()
                : null;
            options ??= new BindingOptions();

            bool isUpdating = false;

            int IndexOf(TItem item)
            {
                for (int i = 0; i < optionsSource.Count; i++)
                {
                    if (comparer.Equals(optionsSource[i], item)) return i;
                }

                return -1;
            }

            void ApplyToUI(TItem current)
            {
                if (anchor != null && !anchor.ShouldProcessNow()) return;
                try
                {
                    isUpdating = true;
                    int idx = IndexOf(current);
                    int max = Math.Max(0, dropdown.options.Count - 1);
                    if (idx < 0) idx = Mathf.Clamp(dropdown.value, 0, max); // keep current if not found
                    idx = Mathf.Clamp(idx, 0, max);
                    dropdown.SetValueWithoutNotify(idx);
                    dropdown.RefreshShownValue();
                }
                finally
                {
                    isUpdating = false;
                }
            }

            void OnItemChanged(IObservable<TItem> _, TItem oldV, TItem newV)
            {
                if (direction == BindDirection.FromUI) return;
                ApplyToUI(newV);
            }

            // Keep selection aligned when options shift (insert/remove/update/clear)
            void OnOptionsAdd(ObservableList<TItem> __, TItem ___, int __idx)
            {
                if (direction == BindDirection.FromUI) return;
                ApplyToUI(selectedItem.Value);
            }

            void OnOptionsUpdate(ObservableList<TItem> __, TItem ___, TItem ____, int __idx)
            {
                if (direction == BindDirection.FromUI) return;
                // If the updated slot is the current selection index or equals the selected item by comparer, refresh.
                ApplyToUI(selectedItem.Value);
            }

            void OnOptionsRemove(ObservableList<TItem> __, TItem ___, int __idx)
            {
                if (direction == BindDirection.FromUI) return;
                ApplyToUI(selectedItem.Value);
            }

            void OnOptionsClear(ObservableList<TItem> __)
            {
                if (direction == BindDirection.FromUI) return;
                ApplyToUI(selectedItem.Value);
            }

            void OnUIChanged(int idx)
            {
                if (direction == BindDirection.ToUI) return;
                if (isUpdating) return;
                if (idx >= 0 && idx < optionsSource.Count)
                    selectedItem.Value = optionsSource[idx];
            }

            void Subscribe()
            {
                if (direction != BindDirection.FromUI)
                    ApplyToUI(selectedItem.Value);

                selectedItem.OnChanged += OnItemChanged;
                dropdown.onValueChanged.AddListener(OnUIChanged);

                optionsSource.OnAdd += OnOptionsAdd;
                optionsSource.OnUpdate += OnOptionsUpdate;
                optionsSource.OnRemove += OnOptionsRemove;
                optionsSource.OnClear += OnOptionsClear;
            }

            void Unsubscribe()
            {
                selectedItem.OnChanged -= OnItemChanged;
                dropdown.onValueChanged.RemoveListener(OnUIChanged);

                optionsSource.OnAdd -= OnOptionsAdd;
                optionsSource.OnUpdate -= OnOptionsUpdate;
                optionsSource.OnRemove -= OnOptionsRemove;
                optionsSource.OnClear -= OnOptionsClear;
            }

            void Resync()
            {
                if (direction != BindDirection.FromUI)
                    ApplyToUI(selectedItem.Value);
            }

            BindingInfo info = new()
            {
                Kind = "TMP_Dropdown.SelectedItem",
                Direction = direction.ToString(),
                Description = $"value (item) ↔ Observable<{typeof(TItem).Name}>",
                Target = dropdown,
                Owner = owner,
                Anchor = anchor,
                Options = options,
                GetState = () =>
                {
                    int count = dropdown.options?.Count ?? 0;
                    int idx = dropdown.value;
                    string itemStr = idx >= 0 && idx < optionsSource.Count
                        ? optionsSource[idx]?.ToString()
                        : "<out-of-range>";
                    return $"value={idx}, options={count}, item={itemStr}";
                },
                Tags = new[] { "TMP_Dropdown", "SelectedItem" }
            };

            BindingHandle handle = new(Subscribe, Unsubscribe, Resync, anchor, options, info);
            if (owner != null)
            {
                BindingDisposer disposer = owner.GetComponent<BindingDisposer>() ??
                                           owner.gameObject.AddComponent<BindingDisposer>();
                disposer.Add(handle);
            }

            return handle;
        }

        // ---------------------------
        // INTERACTABLE
        // ---------------------------
        public static BindingHandle BindInteractable(
            this TMP_Dropdown dropdown,
            Observable<bool> interactable,
            MonoBehaviour owner = null,
            BindingOptions options = null)
        {
            if (dropdown == null) throw new ArgumentNullException(nameof(dropdown));
            if (interactable == null) throw new ArgumentNullException(nameof(interactable));

            BindingAnchor anchor = owner != null
                ? owner.GetComponent<BindingAnchor>() ?? owner.gameObject.AddComponent<BindingAnchor>()
                : null;
            options ??= new BindingOptions();

            void Apply(bool v)
            {
                if (anchor != null && !anchor.ShouldProcessNow()) return;
                dropdown.interactable = v;
            }

            void OnChanged(IObservable<bool> _, bool oldV, bool newV)
            {
                Apply(newV);
            }

            void Subscribe()
            {
                Apply(interactable.Value);
                interactable.OnChanged += OnChanged;
            }

            void Unsubscribe()
            {
                interactable.OnChanged -= OnChanged;
            }

            void Resync()
            {
                Apply(interactable.Value);
            }

            BindingInfo info = new()
            {
                Kind = "TMP_Dropdown.Interactable",
                Direction = "ToUI",
                Description = "interactable ← Observable<bool>",
                Target = dropdown,
                Owner = owner,
                Anchor = anchor,
                Options = options,
                GetState = () => $"interactable={dropdown.interactable}",
                Tags = new[] { "TMP_Dropdown", "Interactable" }
            };

            BindingHandle handle = new(Subscribe, Unsubscribe, Resync, anchor, options, info);
            if (owner != null)
            {
                BindingDisposer disposer = owner.GetComponent<BindingDisposer>() ??
                                           owner.gameObject.AddComponent<BindingDisposer>();
                disposer.Add(handle);
            }

            return handle;
        }

        // ---------------------------
        // ENABLED
        // ---------------------------
        public static BindingHandle BindEnabled(
            this TMP_Dropdown dropdown,
            Observable<bool> enabledObservable,
            MonoBehaviour owner = null,
            BindingOptions options = null)
        {
            if (dropdown == null) throw new ArgumentNullException(nameof(dropdown));
            if (enabledObservable == null) throw new ArgumentNullException(nameof(enabledObservable));

            BindingAnchor anchor = owner != null
                ? owner.GetComponent<BindingAnchor>() ?? owner.gameObject.AddComponent<BindingAnchor>()
                : null;
            options ??= new BindingOptions();

            void Apply(bool v)
            {
                if (anchor != null && !anchor.ShouldProcessNow()) return;
                dropdown.enabled = v;
            }

            void OnChanged(IObservable<bool> _, bool oldV, bool newV)
            {
                Apply(newV);
            }

            void Subscribe()
            {
                Apply(enabledObservable.Value);
                enabledObservable.OnChanged += OnChanged;
            }

            void Unsubscribe()
            {
                enabledObservable.OnChanged -= OnChanged;
            }

            void Resync()
            {
                Apply(enabledObservable.Value);
            }

            BindingInfo info = new()
            {
                Kind = "TMP_Dropdown.Enabled",
                Direction = "ToUI",
                Description = "enabled ← Observable<bool>",
                Target = dropdown,
                Owner = owner,
                Anchor = anchor,
                Options = options,
                GetState = () => $"enabled={dropdown.enabled}",
                Tags = new[] { "TMP_Dropdown", "Enabled" }
            };

            BindingHandle handle = new(Subscribe, Unsubscribe, Resync, anchor, options, info);
            if (owner != null)
            {
                BindingDisposer disposer = owner.GetComponent<BindingDisposer>() ??
                                           owner.gameObject.AddComponent<BindingDisposer>();
                disposer.Add(handle);
            }

            return handle;
        }
    }
}