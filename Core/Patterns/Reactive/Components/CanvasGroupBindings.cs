using System;
using UnityEngine;
using UnityEngine.UI; // keep for consistency if UI used elsewhere
using LegendaryTools;
using LegendaryTools.Reactive;

namespace LegendaryTools.Reactive.UGUI
{
    /// <summary>
    /// Extension methods to bind Observables with UnityEngine.CanvasGroup.
    /// Supports TwoWay polling via UpdatePhase for alpha, interactable, blocksRaycasts, ignoreParentGroups and enabled.
    /// </summary>
    public static class CanvasGroupBindings
    {
        private static BindingUpdateDriver GetDriver(Component c)
        {
            return c.GetComponent<BindingUpdateDriver>() ?? c.gameObject.AddComponent<BindingUpdateDriver>();
        }

        // ---------------------------
        // ALPHA (0..1)
        // ---------------------------
        public static BindingHandle BindAlpha(
            this CanvasGroup group,
            Observable<float> observable,
            BindDirection direction = BindDirection.TwoWay,
            UpdatePhase phase = UpdatePhase.LateUpdate,
            MonoBehaviour owner = null,
            BindingOptions options = null,
            Func<float, float> toUI = null,
            Func<float, float> fromUI = null,
            float epsilon = 0.0001f,
            bool clamp01 = true)
        {
            if (group == null) throw new ArgumentNullException(nameof(group));
            if (observable == null) throw new ArgumentNullException(nameof(observable));

            BindingAnchor anchor = owner != null
                ? owner.GetComponent<BindingAnchor>() ?? owner.gameObject.AddComponent<BindingAnchor>()
                : null;

            BindingUpdateDriver driver = GetDriver(group);
            options ??= new BindingOptions();

            bool isUpdating = false;
            float lastSent = group.alpha;

            float ReadAlpha()
            {
                float v = group.alpha;
                v = clamp01 ? Mathf.Clamp01(v) : v;
                return toUI != null ? toUI(v) : v;
            }

            void WriteAlpha(float v)
            {
                if (anchor != null && !anchor.ShouldProcessNow()) return;
                float inV = fromUI != null ? fromUI(v) : v;
                if (clamp01) inV = Mathf.Clamp01(inV);

                try
                {
                    isUpdating = true;
                    group.alpha = inV;
                }
                finally
                {
                    isUpdating = false;
                }
            }

            void ApplyToUI(float v)
            {
                WriteAlpha(v);
            }

            void OnObsChanged(IObservable<float> _, float oldV, float newV)
            {
                if (direction == BindDirection.FromUI) return;
                ApplyToUI(newV);
            }

            void PollFromUI()
            {
                if (direction == BindDirection.ToUI) return;
                if (isUpdating) return;

                float now = ReadAlpha();
                if (Mathf.Abs(now - lastSent) > epsilon)
                {
                    lastSent = now;
                    observable.Value = now;
                }
            }

            void Subscribe()
            {
                if (direction != BindDirection.FromUI)
                {
                    lastSent = ReadAlpha();
                    ApplyToUI(observable.Value);
                }
                else
                {
                    lastSent = ReadAlpha();
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
                lastSent = ReadAlpha();
            }

            BindingInfo info = new()
            {
                Kind = "CanvasGroup.Alpha",
                Direction = direction.ToString(),
                Description = $"alpha ↔ Observable<float> @ {phase}",
                Target = group,
                Owner = owner,
                Anchor = anchor,
                Options = options,
                GetState = () => $"alpha={group.alpha:0.###}",
                Tags = new[] { "CanvasGroup", "Alpha", phase.ToString() }
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
            this CanvasGroup group,
            Observable<bool> observable,
            BindDirection direction = BindDirection.TwoWay,
            UpdatePhase phase = UpdatePhase.Update,
            MonoBehaviour owner = null,
            BindingOptions options = null,
            Func<bool, bool> toUI = null,
            Func<bool, bool> fromUI = null)
        {
            if (group == null) throw new ArgumentNullException(nameof(group));
            if (observable == null) throw new ArgumentNullException(nameof(observable));

            BindingAnchor anchor = owner != null
                ? owner.GetComponent<BindingAnchor>() ?? owner.gameObject.AddComponent<BindingAnchor>()
                : null;

            BindingUpdateDriver driver = GetDriver(group);
            options ??= new BindingOptions();

            bool isUpdating = false;
            bool lastSent = group.interactable;

            bool ReadInteractable()
            {
                bool v = group.interactable;
                return toUI != null ? toUI(v) : v;
            }

            void WriteInteractable(bool v)
            {
                if (anchor != null && !anchor.ShouldProcessNow()) return;
                bool inV = fromUI != null ? fromUI(v) : v;

                try
                {
                    isUpdating = true;
                    group.interactable = inV;
                }
                finally
                {
                    isUpdating = false;
                }
            }

            void ApplyToUI(bool v)
            {
                WriteInteractable(v);
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

                bool now = ReadInteractable();
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
                    lastSent = ReadInteractable();
                    ApplyToUI(observable.Value);
                }
                else
                {
                    lastSent = ReadInteractable();
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
                lastSent = ReadInteractable();
            }

            BindingInfo info = new()
            {
                Kind = "CanvasGroup.Interactable",
                Direction = direction.ToString(),
                Description = $"interactable ↔ Observable<bool> @ {phase}",
                Target = group,
                Owner = owner,
                Anchor = anchor,
                Options = options,
                GetState = () => $"interactable={group.interactable}",
                Tags = new[] { "CanvasGroup", "Interactable", phase.ToString() }
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
        // BLOCKS RAYCASTS
        // ---------------------------
        public static BindingHandle BindBlocksRaycasts(
            this CanvasGroup group,
            Observable<bool> observable,
            BindDirection direction = BindDirection.TwoWay,
            UpdatePhase phase = UpdatePhase.Update,
            MonoBehaviour owner = null,
            BindingOptions options = null,
            Func<bool, bool> toUI = null,
            Func<bool, bool> fromUI = null)
        {
            if (group == null) throw new ArgumentNullException(nameof(group));
            if (observable == null) throw new ArgumentNullException(nameof(observable));

            BindingAnchor anchor = owner != null
                ? owner.GetComponent<BindingAnchor>() ?? owner.gameObject.AddComponent<BindingAnchor>()
                : null;

            BindingUpdateDriver driver = GetDriver(group);
            options ??= new BindingOptions();

            bool isUpdating = false;
            bool lastSent = group.blocksRaycasts;

            bool ReadBlocks()
            {
                bool v = group.blocksRaycasts;
                return toUI != null ? toUI(v) : v;
            }

            void WriteBlocks(bool v)
            {
                if (anchor != null && !anchor.ShouldProcessNow()) return;
                bool inV = fromUI != null ? fromUI(v) : v;

                try
                {
                    isUpdating = true;
                    group.blocksRaycasts = inV;
                }
                finally
                {
                    isUpdating = false;
                }
            }

            void ApplyToUI(bool v)
            {
                WriteBlocks(v);
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

                bool now = ReadBlocks();
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
                    lastSent = ReadBlocks();
                    ApplyToUI(observable.Value);
                }
                else
                {
                    lastSent = ReadBlocks();
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
                lastSent = ReadBlocks();
            }

            BindingInfo info = new()
            {
                Kind = "CanvasGroup.BlocksRaycasts",
                Direction = direction.ToString(),
                Description = $"blocksRaycasts ↔ Observable<bool> @ {phase}",
                Target = group,
                Owner = owner,
                Anchor = anchor,
                Options = options,
                GetState = () => $"blocksRaycasts={group.blocksRaycasts}",
                Tags = new[] { "CanvasGroup", "BlocksRaycasts", phase.ToString() }
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
        // IGNORE PARENT GROUPS
        // ---------------------------
        public static BindingHandle BindIgnoreParentGroups(
            this CanvasGroup group,
            Observable<bool> observable,
            BindDirection direction = BindDirection.TwoWay,
            UpdatePhase phase = UpdatePhase.Update,
            MonoBehaviour owner = null,
            BindingOptions options = null,
            Func<bool, bool> toUI = null,
            Func<bool, bool> fromUI = null)
        {
            if (group == null) throw new ArgumentNullException(nameof(group));
            if (observable == null) throw new ArgumentNullException(nameof(observable));

            BindingAnchor anchor = owner != null
                ? owner.GetComponent<BindingAnchor>() ?? owner.gameObject.AddComponent<BindingAnchor>()
                : null;

            BindingUpdateDriver driver = GetDriver(group);
            options ??= new BindingOptions();

            bool isUpdating = false;
            bool lastSent = group.ignoreParentGroups;

            bool ReadIgnore()
            {
                bool v = group.ignoreParentGroups;
                return toUI != null ? toUI(v) : v;
            }

            void WriteIgnore(bool v)
            {
                if (anchor != null && !anchor.ShouldProcessNow()) return;
                bool inV = fromUI != null ? fromUI(v) : v;

                try
                {
                    isUpdating = true;
                    group.ignoreParentGroups = inV;
                }
                finally
                {
                    isUpdating = false;
                }
            }

            void ApplyToUI(bool v)
            {
                WriteIgnore(v);
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

                bool now = ReadIgnore();
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
                    lastSent = ReadIgnore();
                    ApplyToUI(observable.Value);
                }
                else
                {
                    lastSent = ReadIgnore();
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
                lastSent = ReadIgnore();
            }

            BindingInfo info = new()
            {
                Kind = "CanvasGroup.IgnoreParentGroups",
                Direction = direction.ToString(),
                Description = $"ignoreParentGroups ↔ Observable<bool> @ {phase}",
                Target = group,
                Owner = owner,
                Anchor = anchor,
                Options = options,
                GetState = () => $"ignoreParentGroups={group.ignoreParentGroups}",
                Tags = new[] { "CanvasGroup", "IgnoreParentGroups", phase.ToString() }
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
            this CanvasGroup group,
            Observable<bool> observable,
            BindDirection direction = BindDirection.TwoWay,
            UpdatePhase phase = UpdatePhase.Update,
            MonoBehaviour owner = null,
            BindingOptions options = null,
            Func<bool, bool> toUI = null,
            Func<bool, bool> fromUI = null)
        {
            if (group == null) throw new ArgumentNullException(nameof(group));
            if (observable == null) throw new ArgumentNullException(nameof(observable));

            BindingAnchor anchor = owner != null
                ? owner.GetComponent<BindingAnchor>() ?? owner.gameObject.AddComponent<BindingAnchor>()
                : null;

            BindingUpdateDriver driver = GetDriver(group);
            options ??= new BindingOptions();

            bool isUpdating = false;
            bool lastSent = group.enabled;

            bool ReadEnabled()
            {
                bool v = group.enabled;
                return toUI != null ? toUI(v) : v;
            }

            void WriteEnabled(bool v)
            {
                if (anchor != null && !anchor.ShouldProcessNow()) return;
                bool inV = fromUI != null ? fromUI(v) : v;

                try
                {
                    isUpdating = true;
                    group.enabled = inV;
                }
                finally
                {
                    isUpdating = false;
                }
            }

            void ApplyToUI(bool v)
            {
                WriteEnabled(v);
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

                bool now = ReadEnabled();
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
                    lastSent = ReadEnabled();
                    ApplyToUI(observable.Value);
                }
                else
                {
                    lastSent = ReadEnabled();
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
                lastSent = ReadEnabled();
            }

            BindingInfo info = new()
            {
                Kind = "CanvasGroup.Enabled",
                Direction = direction.ToString(),
                Description = $"enabled ↔ Observable<bool> @ {phase}",
                Target = group,
                Owner = owner,
                Anchor = anchor,
                Options = options,
                GetState = () => $"enabled={group.enabled}",
                Tags = new[] { "CanvasGroup", "Enabled", phase.ToString() }
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