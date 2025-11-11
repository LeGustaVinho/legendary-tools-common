using System;
using TMPro;
using UnityEngine;
using LegendaryTools;
using LegendaryTools.Reactive;

namespace LegendaryTools.Reactive.TMPro
{
    /// <summary>
    /// Extension methods to bind Observables with TMPro.TMP_Dropdown.
    /// Adds TwoWay polling (UpdatePhase) for interactable and enabled.
    /// </summary>
    public static class DropdownBindings
    {
        private static BindingUpdateDriver GetDriver(Component c)
        {
            return c.GetComponent<BindingUpdateDriver>() ?? c.gameObject.AddComponent<BindingUpdateDriver>();
        }

        // ---------------------------
        // INTERACTABLE (Selectable.interactable)
        // ---------------------------
        public static BindingHandle BindInteractable(
            this TMP_Dropdown dropdown,
            Observable<bool> observable,
            BindDirection direction = BindDirection.TwoWay,
            UpdatePhase phase = UpdatePhase.Update,
            MonoBehaviour owner = null,
            BindingOptions options = null,
            Func<bool, bool> toUI = null,
            Func<bool, bool> fromUI = null)
        {
            if (dropdown == null) throw new ArgumentNullException(nameof(dropdown));
            if (observable == null) throw new ArgumentNullException(nameof(observable));

            BindingAnchor anchor = owner != null
                ? owner.GetComponent<BindingAnchor>() ?? owner.gameObject.AddComponent<BindingAnchor>()
                : null;

            BindingUpdateDriver driver = GetDriver(dropdown);
            options ??= new BindingOptions();

            bool isUpdating = false;
            bool lastSent = dropdown.interactable;

            bool Read()
            {
                bool v = dropdown.interactable;
                return toUI != null ? toUI(v) : v;
            }

            void Write(bool v)
            {
                if (anchor != null && !anchor.ShouldProcessNow()) return;
                bool inV = fromUI != null ? fromUI(v) : v;

                try
                {
                    isUpdating = true;
                    dropdown.interactable = inV;
                }
                finally
                {
                    isUpdating = false;
                }
            }

            void ApplyToUI(bool v)
            {
                Write(v);
            }

            void OnObsChanged(IObservable<bool> _, bool oldV, bool newV)
            {
                if (direction == BindDirection.FromUI) return;
                ApplyToUI(newV);
            }

            void PollFromUI()
            {
                if (direction == BindDirection.ToUI) return;
                if (isUpdating) return;

                bool now = Read();
                if (now != lastSent)
                {
                    lastSent = now;
                    observable.Value = now;
                }
            }

            void Subscribe()
            {
                if (direction != BindDirection.FromUI)
                {
                    lastSent = Read();
                    ApplyToUI(observable.Value);
                }
                else
                {
                    lastSent = Read();
                }

                observable.OnChanged += OnObsChanged;

                switch (phase)
                {
                    case UpdatePhase.Update: driver.OnUpdateTick += PollFromUI; break;
                    case UpdatePhase.LateUpdate: driver.OnLateUpdateTick += PollFromUI; break;
                    case UpdatePhase.FixedUpdate: driver.OnFixedUpdateTick += PollFromUI; break;
                }
            }

            void Unsubscribe()
            {
                observable.OnChanged -= OnObsChanged;

                switch (phase)
                {
                    case UpdatePhase.Update: driver.OnUpdateTick -= PollFromUI; break;
                    case UpdatePhase.LateUpdate: driver.OnLateUpdateTick -= PollFromUI; break;
                    case UpdatePhase.FixedUpdate: driver.OnFixedUpdateTick -= PollFromUI; break;
                }
            }

            void Resync()
            {
                if (direction != BindDirection.FromUI)
                    ApplyToUI(observable.Value);
                lastSent = Read();
            }

            BindingInfo info = new()
            {
                Kind = "TMP_Dropdown.Interactable",
                Direction = direction.ToString(),
                Description = $"interactable ↔ Observable<bool> @ {phase}",
                Target = dropdown,
                Owner = owner,
                Anchor = anchor,
                Options = options,
                GetState = () => $"interactable={dropdown.interactable}",
                Tags = new[] { "TMP_Dropdown", "Interactable", phase.ToString() }
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
        // ENABLED (Behaviour.enabled)
        // ---------------------------
        public static BindingHandle BindEnabled(
            this TMP_Dropdown dropdown,
            Observable<bool> observable,
            BindDirection direction = BindDirection.TwoWay,
            UpdatePhase phase = UpdatePhase.Update,
            MonoBehaviour owner = null,
            BindingOptions options = null,
            Func<bool, bool> toUI = null,
            Func<bool, bool> fromUI = null)
        {
            if (dropdown == null) throw new ArgumentNullException(nameof(dropdown));
            if (observable == null) throw new ArgumentNullException(nameof(observable));

            BindingAnchor anchor = owner != null
                ? owner.GetComponent<BindingAnchor>() ?? owner.gameObject.AddComponent<BindingAnchor>()
                : null;

            BindingUpdateDriver driver = GetDriver(dropdown);
            options ??= new BindingOptions();

            bool isUpdating = false;
            bool lastSent = dropdown.enabled;

            bool Read()
            {
                bool v = dropdown.enabled;
                return toUI != null ? toUI(v) : v;
            }

            void Write(bool v)
            {
                if (anchor != null && !anchor.ShouldProcessNow()) return;
                bool inV = fromUI != null ? fromUI(v) : v;

                try
                {
                    isUpdating = true;
                    dropdown.enabled = inV;
                }
                finally
                {
                    isUpdating = false;
                }
            }

            void ApplyToUI(bool v)
            {
                Write(v);
            }

            void OnObsChanged(IObservable<bool> _, bool oldV, bool newV)
            {
                if (direction == BindDirection.FromUI) return;
                ApplyToUI(newV);
            }

            void PollFromUI()
            {
                if (direction == BindDirection.ToUI) return;
                if (isUpdating) return;

                bool now = Read();
                if (now != lastSent)
                {
                    lastSent = now;
                    observable.Value = now;
                }
            }

            void Subscribe()
            {
                if (direction != BindDirection.FromUI)
                {
                    lastSent = Read();
                    ApplyToUI(observable.Value);
                }
                else
                {
                    lastSent = Read();
                }

                observable.OnChanged += OnObsChanged;

                switch (phase)
                {
                    case UpdatePhase.Update: driver.OnUpdateTick += PollFromUI; break;
                    case UpdatePhase.LateUpdate: driver.OnLateUpdateTick += PollFromUI; break;
                    case UpdatePhase.FixedUpdate: driver.OnFixedUpdateTick += PollFromUI; break;
                }
            }

            void Unsubscribe()
            {
                observable.OnChanged -= OnObsChanged;

                switch (phase)
                {
                    case UpdatePhase.Update: driver.OnUpdateTick -= PollFromUI; break;
                    case UpdatePhase.LateUpdate: driver.OnLateUpdateTick -= PollFromUI; break;
                    case UpdatePhase.FixedUpdate: driver.OnFixedUpdateTick -= PollFromUI; break;
                }
            }

            void Resync()
            {
                if (direction != BindDirection.FromUI)
                    ApplyToUI(observable.Value);
                lastSent = Read();
            }

            BindingInfo info = new()
            {
                Kind = "TMP_Dropdown.Enabled",
                Direction = direction.ToString(),
                Description = $"enabled ↔ Observable<bool> @ {phase}",
                Target = dropdown,
                Owner = owner,
                Anchor = anchor,
                Options = options,
                GetState = () => $"enabled={dropdown.enabled}",
                Tags = new[] { "TMP_Dropdown", "Enabled", phase.ToString() }
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