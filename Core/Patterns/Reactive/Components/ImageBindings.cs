using System;
using UnityEngine;
using UnityEngine.UI;
using LegendaryTools;
using LegendaryTools.Reactive;

namespace LegendaryTools.Reactive.UGUI
{
    /// <summary>
    /// Extension methods to bind Observables with UnityEngine.UI.Image.
    /// Supports TwoWay via polling (UpdatePhase) when the component does not raise change events.
    /// </summary>
    public static class ImageBindings
    {
        private static BindingUpdateDriver GetDriver(Component c)
        {
            return c.GetComponent<BindingUpdateDriver>() ?? c.gameObject.AddComponent<BindingUpdateDriver>();
        }

        private static bool Approximately(Color a, Color b, float eps)
        {
            return Mathf.Abs(a.r - b.r) <= eps &&
                   Mathf.Abs(a.g - b.g) <= eps &&
                   Mathf.Abs(a.b - b.b) <= eps &&
                   Mathf.Abs(a.a - b.a) <= eps;
        }

        // ---------------------------
        // COLOR
        // ---------------------------
        public static BindingHandle BindColor(
            this Image image,
            Observable<Color> observable,
            BindDirection direction = BindDirection.TwoWay,
            UpdatePhase phase = UpdatePhase.LateUpdate,
            MonoBehaviour owner = null,
            BindingOptions options = null,
            Func<Color, Color> toUI = null,
            Func<Color, Color> fromUI = null,
            float epsilon = 0.001f)
        {
            if (image == null) throw new ArgumentNullException(nameof(image));
            if (observable == null) throw new ArgumentNullException(nameof(observable));

            BindingAnchor anchor = owner != null
                ? owner.GetComponent<BindingAnchor>() ?? owner.gameObject.AddComponent<BindingAnchor>()
                : null;

            BindingUpdateDriver driver = GetDriver(image);
            options ??= new BindingOptions();

            bool isUpdating = false;
            Color lastSent = image.color;

            Color ReadColor()
            {
                Color c = image.color;
                return toUI != null ? toUI(c) : c;
            }

            void WriteColor(Color c)
            {
                if (anchor != null && !anchor.ShouldProcessNow()) return;
                Color inC = fromUI != null ? fromUI(c) : c;

                try
                {
                    isUpdating = true;
                    image.color = inC;
                }
                finally
                {
                    isUpdating = false;
                }
            }

            void ApplyToUI(Color c)
            {
                WriteColor(c);
            }

            void OnObsChanged(IObservable<Color> _, Color oldV, Color newV)
            {
                if (direction == BindDirection.FromUI) return;
                ApplyToUI(newV);
            }

            void PollFromUI()
            {
                if (direction == BindDirection.ToUI) return;
                if (isUpdating) return;
                Color now = ReadColor();
                if (!Approximately(now, lastSent, epsilon))
                {
                    lastSent = now;
                    observable.Value = now;
                }
            }

            void Subscribe()
            {
                if (direction != BindDirection.FromUI)
                {
                    lastSent = ReadColor();
                    ApplyToUI(observable.Value);
                }
                else
                {
                    lastSent = ReadColor();
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
                lastSent = ReadColor();
            }

            BindingInfo info = new()
            {
                Kind = "Image.Color",
                Direction = direction.ToString(),
                Description = $"color ↔ Observable<Color> @ {phase}",
                Target = image,
                Owner = owner,
                Anchor = anchor,
                Options = options,
                GetState = () =>
                {
                    Color c = image.color;
                    return $"color=({c.r:0.###},{c.g:0.###},{c.b:0.###},{c.a:0.###})";
                },
                Tags = new[] { "Image", "Color", phase.ToString() }
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
        // SPRITE
        // ---------------------------
        public static BindingHandle BindSprite(
            this Image image,
            Observable<Sprite> observable,
            BindDirection direction = BindDirection.TwoWay,
            UpdatePhase phase = UpdatePhase.LateUpdate,
            MonoBehaviour owner = null,
            BindingOptions options = null,
            Func<Sprite, Sprite> toUI = null,
            Func<Sprite, Sprite> fromUI = null)
        {
            if (image == null) throw new ArgumentNullException(nameof(image));
            if (observable == null) throw new ArgumentNullException(nameof(observable));

            BindingAnchor anchor = owner != null
                ? owner.GetComponent<BindingAnchor>() ?? owner.gameObject.AddComponent<BindingAnchor>()
                : null;

            BindingUpdateDriver driver = GetDriver(image);
            options ??= new BindingOptions();

            bool isUpdating = false;
            Sprite lastSent = image.sprite;

            Sprite ReadSprite()
            {
                Sprite s = image.sprite;
                return toUI != null ? toUI(s) : s;
            }

            void WriteSprite(Sprite s)
            {
                if (anchor != null && !anchor.ShouldProcessNow()) return;
                Sprite inS = fromUI != null ? fromUI(s) : s;

                try
                {
                    isUpdating = true;
                    image.sprite = inS;
                }
                finally
                {
                    isUpdating = false;
                }
            }

            void ApplyToUI(Sprite s)
            {
                WriteSprite(s);
            }

            void OnObsChanged(IObservable<Sprite> _, Sprite oldV, Sprite newV)
            {
                if (direction == BindDirection.FromUI) return;
                ApplyToUI(newV);
            }

            void PollFromUI()
            {
                if (direction == BindDirection.ToUI) return;
                if (isUpdating) return;
                Sprite now = ReadSprite();
                if (!ReferenceEquals(now, lastSent))
                {
                    lastSent = now;
                    observable.Value = now;
                }
            }

            void Subscribe()
            {
                if (direction != BindDirection.FromUI)
                {
                    lastSent = ReadSprite();
                    ApplyToUI(observable.Value);
                }
                else
                {
                    lastSent = ReadSprite();
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
                lastSent = ReadSprite();
            }

            BindingInfo info = new()
            {
                Kind = "Image.Sprite",
                Direction = direction.ToString(),
                Description = $"sprite ↔ Observable<Sprite> @ {phase}",
                Target = image,
                Owner = owner,
                Anchor = anchor,
                Options = options,
                GetState = () => $"sprite={(image.sprite ? image.sprite.name : "<null>")}",
                Tags = new[] { "Image", "Sprite", phase.ToString() }
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
        // FILL AMOUNT (0..1)
        // ---------------------------
        public static BindingHandle BindFillAmount(
            this Image image,
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
            if (image == null) throw new ArgumentNullException(nameof(image));
            if (observable == null) throw new ArgumentNullException(nameof(observable));

            BindingAnchor anchor = owner != null
                ? owner.GetComponent<BindingAnchor>() ?? owner.gameObject.AddComponent<BindingAnchor>()
                : null;

            BindingUpdateDriver driver = GetDriver(image);
            options ??= new BindingOptions();

            bool isUpdating = false;
            float lastSent = image.fillAmount;

            float ReadFill()
            {
                float v = image.fillAmount;
                v = clamp01 ? Mathf.Clamp01(v) : v;
                return toUI != null ? toUI(v) : v;
            }

            void WriteFill(float f)
            {
                if (anchor != null && !anchor.ShouldProcessNow()) return;
                float inF = fromUI != null ? fromUI(f) : f;
                if (clamp01) inF = Mathf.Clamp01(inF);

                try
                {
                    isUpdating = true;
                    image.fillAmount = inF;
                }
                finally
                {
                    isUpdating = false;
                }
            }

            void ApplyToUI(float f)
            {
                WriteFill(f);
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
                float now = ReadFill();
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
                    lastSent = ReadFill();
                    ApplyToUI(observable.Value);
                }
                else
                {
                    lastSent = ReadFill();
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
                lastSent = ReadFill();
            }

            BindingInfo info = new()
            {
                Kind = "Image.FillAmount",
                Direction = direction.ToString(),
                Description = $"fillAmount ↔ Observable<float> @ {phase}",
                Target = image,
                Owner = owner,
                Anchor = anchor,
                Options = options,
                GetState = () => $"fillAmount={image.fillAmount:0.###}, type={image.type}",
                Tags = new[] { "Image", "FillAmount", phase.ToString() }
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
            this Image image,
            Observable<bool> observable,
            BindDirection direction = BindDirection.TwoWay,
            UpdatePhase phase = UpdatePhase.Update,
            MonoBehaviour owner = null,
            BindingOptions options = null,
            Func<bool, bool> toUI = null,
            Func<bool, bool> fromUI = null)
        {
            if (image == null) throw new ArgumentNullException(nameof(image));
            if (observable == null) throw new ArgumentNullException(nameof(observable));

            BindingAnchor anchor = owner != null
                ? owner.GetComponent<BindingAnchor>() ?? owner.gameObject.AddComponent<BindingAnchor>()
                : null;

            BindingUpdateDriver driver = GetDriver(image);
            options ??= new BindingOptions();

            bool isUpdating = false;
            bool lastSent = image.enabled;

            bool ReadEnabled()
            {
                bool v = image.enabled;
                return toUI != null ? toUI(v) : v;
            }

            void WriteEnabled(bool v)
            {
                if (anchor != null && !anchor.ShouldProcessNow()) return;
                bool inV = fromUI != null ? fromUI(v) : v;

                try
                {
                    isUpdating = true;
                    image.enabled = inV;
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
                Kind = "Image.Enabled",
                Direction = direction.ToString(),
                Description = $"enabled ↔ Observable<bool> @ {phase}",
                Target = image,
                Owner = owner,
                Anchor = anchor,
                Options = options,
                GetState = () => $"enabled={image.enabled}",
                Tags = new[] { "Image", "Enabled", phase.ToString() }
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
        // RAYCAST TARGET
        // ---------------------------
        public static BindingHandle BindRaycastTarget(
            this Image image,
            Observable<bool> observable,
            BindDirection direction = BindDirection.TwoWay,
            UpdatePhase phase = UpdatePhase.Update,
            MonoBehaviour owner = null,
            BindingOptions options = null,
            Func<bool, bool> toUI = null,
            Func<bool, bool> fromUI = null)
        {
            if (image == null) throw new ArgumentNullException(nameof(image));
            if (observable == null) throw new ArgumentNullException(nameof(observable));

            BindingAnchor anchor = owner != null
                ? owner.GetComponent<BindingAnchor>() ?? owner.gameObject.AddComponent<BindingAnchor>()
                : null;

            BindingUpdateDriver driver = GetDriver(image);
            options ??= new BindingOptions();

            bool isUpdating = false;
            bool lastSent = image.raycastTarget;

            bool ReadRT()
            {
                bool v = image.raycastTarget;
                return toUI != null ? toUI(v) : v;
            }

            void WriteRT(bool v)
            {
                if (anchor != null && !anchor.ShouldProcessNow()) return;
                bool inV = fromUI != null ? fromUI(v) : v;

                try
                {
                    isUpdating = true;
                    image.raycastTarget = inV;
                }
                finally
                {
                    isUpdating = false;
                }
            }

            void ApplyToUI(bool v)
            {
                WriteRT(v);
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
                bool now = ReadRT();
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
                    lastSent = ReadRT();
                    ApplyToUI(observable.Value);
                }
                else
                {
                    lastSent = ReadRT();
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
                lastSent = ReadRT();
            }

            BindingInfo info = new()
            {
                Kind = "Image.RaycastTarget",
                Direction = direction.ToString(),
                Description = $"raycastTarget ↔ Observable<bool> @ {phase}",
                Target = image,
                Owner = owner,
                Anchor = anchor,
                Options = options,
                GetState = () => $"raycastTarget={image.raycastTarget}",
                Tags = new[] { "Image", "RaycastTarget", phase.ToString() }
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