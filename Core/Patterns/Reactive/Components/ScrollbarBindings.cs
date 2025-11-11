using System;
using UnityEngine;
using UnityEngine.UI;
using LegendaryTools;
using LegendaryTools.Reactive;

namespace LegendaryTools.Reactive.UGUI
{
    /// <summary>
    /// Extension methods to bind Observables with UnityEngine.UI.Scrollbar.
    /// Provides TwoWay polling via UpdatePhase for size, numberOfSteps and direction.
    /// </summary>
    public static class ScrollbarBindings
    {
        private static BindingUpdateDriver GetDriver(Component c)
        {
            return c.GetComponent<BindingUpdateDriver>() ?? c.gameObject.AddComponent<BindingUpdateDriver>();
        }

        // ---------------------------
        // SIZE (0..1)
        // ---------------------------
        public static BindingHandle BindSize(
            this Scrollbar scrollbar,
            Observable<float> observable,
            BindDirection direction = Reactive.BindDirection.TwoWay,
            UpdatePhase phase = UpdatePhase.LateUpdate,
            MonoBehaviour owner = null,
            BindingOptions options = null,
            Func<float, float> toUI = null,
            Func<float, float> fromUI = null,
            float epsilon = 0.0001f,
            bool clamp01 = true)
        {
            if (scrollbar == null) throw new ArgumentNullException(nameof(scrollbar));
            if (observable == null) throw new ArgumentNullException(nameof(observable));

            BindingAnchor anchor = owner != null
                ? owner.GetComponent<BindingAnchor>() ?? owner.gameObject.AddComponent<BindingAnchor>()
                : null;
            BindingUpdateDriver driver = GetDriver(scrollbar);
            options ??= new BindingOptions();

            bool isUpdating = false;
            float lastSent = scrollbar.size;

            float Read()
            {
                float v = scrollbar.size;
                v = clamp01 ? Mathf.Clamp01(v) : v;
                return toUI != null ? toUI(v) : v;
            }

            void Write(float v)
            {
                if (anchor != null && !anchor.ShouldProcessNow()) return;
                float inV = fromUI != null ? fromUI(v) : v;
                if (clamp01) inV = Mathf.Clamp01(inV);

                try
                {
                    isUpdating = true;
                    scrollbar.size = inV;
                }
                finally
                {
                    isUpdating = false;
                }
            }

            void ApplyToUI(float v)
            {
                Write(v);
            }

            void OnObsChanged(IObservable<float> _, float oldV, float newV)
            {
                if (direction == Reactive.BindDirection.FromUI) return;
                ApplyToUI(newV);
            }

            void PollFromUI()
            {
                if (direction == Reactive.BindDirection.ToUI) return;
                if (isUpdating) return;

                float now = Read();
                if (Mathf.Abs(now - lastSent) > epsilon)
                {
                    lastSent = now;
                    observable.Value = now;
                }
            }

            void Subscribe()
            {
                if (direction != Reactive.BindDirection.FromUI)
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
                if (direction != Reactive.BindDirection.FromUI)
                    ApplyToUI(observable.Value);
                lastSent = Read();
            }

            BindingInfo info = new()
            {
                Kind = "Scrollbar.Size",
                Direction = direction.ToString(),
                Description = $"size ↔ Observable<float> @ {phase}",
                Target = scrollbar,
                Owner = owner,
                Anchor = anchor,
                Options = options,
                GetState = () => $"size={scrollbar.size:0.###}",
                Tags = new[] { "Scrollbar", "Size", phase.ToString() }
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
        // NUMBER OF STEPS (>= 0, 0 = continuous)
        // ---------------------------
        public static BindingHandle BindNumberOfSteps(
            this Scrollbar scrollbar,
            Observable<int> observable,
            BindDirection direction = Reactive.BindDirection.TwoWay,
            UpdatePhase phase = UpdatePhase.Update,
            MonoBehaviour owner = null,
            BindingOptions options = null,
            Func<int, int> toUI = null,
            Func<int, int> fromUI = null,
            int minSteps = 0)
        {
            if (scrollbar == null) throw new ArgumentNullException(nameof(scrollbar));
            if (observable == null) throw new ArgumentNullException(nameof(observable));

            BindingAnchor anchor = owner != null
                ? owner.GetComponent<BindingAnchor>() ?? owner.gameObject.AddComponent<BindingAnchor>()
                : null;
            BindingUpdateDriver driver = GetDriver(scrollbar);
            options ??= new BindingOptions();

            bool isUpdating = false;
            int lastSent = scrollbar.numberOfSteps;

            int Read()
            {
                int v = scrollbar.numberOfSteps;
                v = Mathf.Max(minSteps, v);
                return toUI != null ? toUI(v) : v;
            }

            void Write(int v)
            {
                if (anchor != null && !anchor.ShouldProcessNow()) return;
                int inV = fromUI != null ? fromUI(v) : v;
                inV = Mathf.Max(minSteps, inV);

                try
                {
                    isUpdating = true;
                    scrollbar.numberOfSteps = inV;
                }
                finally
                {
                    isUpdating = false;
                }
            }

            void ApplyToUI(int v)
            {
                Write(v);
            }

            void OnObsChanged(IObservable<int> _, int oldV, int newV)
            {
                if (direction == Reactive.BindDirection.FromUI) return;
                ApplyToUI(newV);
            }

            void PollFromUI()
            {
                if (direction == Reactive.BindDirection.ToUI) return;
                if (isUpdating) return;

                int now = Read();
                if (now != lastSent)
                {
                    lastSent = now;
                    observable.Value = now;
                }
            }

            void Subscribe()
            {
                if (direction != Reactive.BindDirection.FromUI)
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
                if (direction != Reactive.BindDirection.FromUI)
                    ApplyToUI(observable.Value);
                lastSent = Read();
            }

            BindingInfo info = new()
            {
                Kind = "Scrollbar.NumberOfSteps",
                Direction = direction.ToString(),
                Description = $"numberOfSteps ↔ Observable<int> @ {phase}",
                Target = scrollbar,
                Owner = owner,
                Anchor = anchor,
                Options = options,
                GetState = () => $"numberOfSteps={scrollbar.numberOfSteps}",
                Tags = new[] { "Scrollbar", "NumberOfSteps", phase.ToString() }
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
        // DIRECTION (enum)
        // ---------------------------
        public static BindingHandle BindDirection(
            this Scrollbar scrollbar,
            Observable<Scrollbar.Direction> observable,
            BindDirection direction = Reactive.BindDirection.TwoWay,
            UpdatePhase phase = UpdatePhase.Update,
            MonoBehaviour owner = null,
            BindingOptions options = null,
            Func<Scrollbar.Direction, Scrollbar.Direction> toUI = null,
            Func<Scrollbar.Direction, Scrollbar.Direction> fromUI = null)
        {
            if (scrollbar == null) throw new ArgumentNullException(nameof(scrollbar));
            if (observable == null) throw new ArgumentNullException(nameof(observable));

            BindingAnchor anchor = owner != null
                ? owner.GetComponent<BindingAnchor>() ?? owner.gameObject.AddComponent<BindingAnchor>()
                : null;
            BindingUpdateDriver driver = GetDriver(scrollbar);
            options ??= new BindingOptions();

            bool isUpdating = false;
            Scrollbar.Direction lastSent = scrollbar.direction;

            Scrollbar.Direction Read()
            {
                Scrollbar.Direction v = scrollbar.direction;
                return toUI != null ? toUI(v) : v;
            }

            void Write(Scrollbar.Direction v)
            {
                if (anchor != null && !anchor.ShouldProcessNow()) return;
                Scrollbar.Direction inV = fromUI != null ? fromUI(v) : v;

                try
                {
                    isUpdating = true;
                    scrollbar.direction = inV;
                }
                finally
                {
                    isUpdating = false;
                }
            }

            void ApplyToUI(Scrollbar.Direction v)
            {
                Write(v);
            }

            void OnObsChanged(IObservable<Scrollbar.Direction> _, Scrollbar.Direction oldV, Scrollbar.Direction newV)
            {
                if (direction == Reactive.BindDirection.FromUI) return;
                ApplyToUI(newV);
            }

            void PollFromUI()
            {
                if (direction == Reactive.BindDirection.ToUI) return;
                if (isUpdating) return;

                Scrollbar.Direction now = Read();
                if (now != lastSent)
                {
                    lastSent = now;
                    observable.Value = now;
                }
            }

            void Subscribe()
            {
                if (direction != Reactive.BindDirection.FromUI)
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
                if (direction != Reactive.BindDirection.FromUI)
                    ApplyToUI(observable.Value);
                lastSent = Read();
            }

            BindingInfo info = new()
            {
                Kind = "Scrollbar.Direction",
                Direction = direction.ToString(),
                Description = $"direction ↔ Observable<Scrollbar.Direction> @ {phase}",
                Target = scrollbar,
                Owner = owner,
                Anchor = anchor,
                Options = options,
                GetState = () => $"direction={scrollbar.direction}",
                Tags = new[] { "Scrollbar", "Direction", phase.ToString() }
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