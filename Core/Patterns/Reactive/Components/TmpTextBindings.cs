using System;
using TMPro;
using UnityEngine;
using LegendaryTools;
using LegendaryTools.Reactive;

namespace LegendaryTools.Reactive.TMPro
{
    /// <summary>
    /// Extension methods to bind Observables with TMPro.TMP_Text (e.g., TextMeshProUGUI).
    /// Provides TwoWay polling via UpdatePhase for text, color and fontSize.
    /// </summary>
    public static class TmpTextBindings
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
        // TEXT (string)
        // ---------------------------
        public static BindingHandle BindText(
            this TMP_Text label,
            Observable<string> observable,
            BindDirection direction = BindDirection.TwoWay,
            UpdatePhase phase = UpdatePhase.LateUpdate,
            MonoBehaviour owner = null,
            BindingOptions options = null,
            Func<string, string> toUI = null,
            Func<string, string> fromUI = null)
        {
            if (label == null) throw new ArgumentNullException(nameof(label));
            if (observable == null) throw new ArgumentNullException(nameof(observable));

            BindingAnchor anchor = owner != null
                ? owner.GetComponent<BindingAnchor>() ?? owner.gameObject.AddComponent<BindingAnchor>()
                : null;

            BindingUpdateDriver driver = GetDriver(label);
            options ??= new BindingOptions();

            bool isUpdating = false;
            string lastSent = label.text;

            string ReadText()
            {
                string v = label.text;
                return toUI != null ? toUI(v) : v;
            }

            void WriteText(string v)
            {
                if (anchor != null && !anchor.ShouldProcessNow()) return;
                string inV = fromUI != null ? fromUI(v) : v;
                inV ??= options.NullOrInvalidPlaceholder ?? string.Empty;

                try
                {
                    isUpdating = true;
                    label.SetText(inV);
                }
                finally
                {
                    isUpdating = false;
                }
            }

            void ApplyToUI(string v)
            {
                WriteText(v);
            }

            void OnObsChanged(IObservable<string> _, string oldV, string newV)
            {
                if (direction == BindDirection.FromUI) return;
                ApplyToUI(newV);
            }

            void PollFromUI()
            {
                if (direction == BindDirection.ToUI) return;
                if (isUpdating) return;
                string now = ReadText();
                if (!string.Equals(now, lastSent, StringComparison.Ordinal))
                {
                    lastSent = now;
                    observable.Value = now;
                }
            }

            void Subscribe()
            {
                if (direction != BindDirection.FromUI)
                {
                    lastSent = ReadText();
                    ApplyToUI(observable.Value);
                }
                else
                {
                    lastSent = ReadText();
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
                lastSent = ReadText();
            }

            BindingInfo info = new()
            {
                Kind = "TMP_Text.Text",
                Direction = direction.ToString(),
                Description = $"text ↔ Observable<string> @ {phase}",
                Target = label,
                Owner = owner,
                Anchor = anchor,
                Options = options,
                GetState = () => $"text=\"{label.text}\"",
                Tags = new[] { "TMP_Text", "Text", phase.ToString() }
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
        // COLOR
        // ---------------------------
        public static BindingHandle BindColor(
            this TMP_Text label,
            Observable<Color> observable,
            BindDirection direction = BindDirection.TwoWay,
            UpdatePhase phase = UpdatePhase.LateUpdate,
            MonoBehaviour owner = null,
            BindingOptions options = null,
            Func<Color, Color> toUI = null,
            Func<Color, Color> fromUI = null,
            float epsilon = 0.001f)
        {
            if (label == null) throw new ArgumentNullException(nameof(label));
            if (observable == null) throw new ArgumentNullException(nameof(observable));

            BindingAnchor anchor = owner != null
                ? owner.GetComponent<BindingAnchor>() ?? owner.gameObject.AddComponent<BindingAnchor>()
                : null;

            BindingUpdateDriver driver = GetDriver(label);
            options ??= new BindingOptions();

            bool isUpdating = false;
            Color lastSent = label.color;

            Color ReadColor()
            {
                Color c = label.color;
                return toUI != null ? toUI(c) : c;
            }

            void WriteColor(Color c)
            {
                if (anchor != null && !anchor.ShouldProcessNow()) return;
                Color inC = fromUI != null ? fromUI(c) : c;

                try
                {
                    isUpdating = true;
                    label.color = inC;
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
                Kind = "TMP_Text.Color",
                Direction = direction.ToString(),
                Description = $"color ↔ Observable<Color> @ {phase}",
                Target = label,
                Owner = owner,
                Anchor = anchor,
                Options = options,
                GetState = () =>
                {
                    Color c = label.color;
                    return $"color=({c.r:0.###},{c.g:0.###},{c.b:0.###},{c.a:0.###})";
                },
                Tags = new[] { "TMP_Text", "Color", phase.ToString() }
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
        // FONT SIZE (float)
        // ---------------------------
        public static BindingHandle BindFontSize(
            this TMP_Text label,
            Observable<float> observable,
            BindDirection direction = BindDirection.TwoWay,
            UpdatePhase phase = UpdatePhase.LateUpdate,
            MonoBehaviour owner = null,
            BindingOptions options = null,
            Func<float, float> toUI = null,
            Func<float, float> fromUI = null,
            float epsilon = 0.001f)
        {
            if (label == null) throw new ArgumentNullException(nameof(label));
            if (observable == null) throw new ArgumentNullException(nameof(observable));

            BindingAnchor anchor = owner != null
                ? owner.GetComponent<BindingAnchor>() ?? owner.gameObject.AddComponent<BindingAnchor>()
                : null;

            BindingUpdateDriver driver = GetDriver(label);
            options ??= new BindingOptions();

            bool isUpdating = false;
            float lastSent = label.fontSize;

            float ReadSize()
            {
                float v = label.fontSize;
                return toUI != null ? toUI(v) : v;
            }

            void WriteSize(float v)
            {
                if (anchor != null && !anchor.ShouldProcessNow()) return;
                float inV = fromUI != null ? fromUI(v) : v;

                try
                {
                    isUpdating = true;
                    label.fontSize = inV;
                }
                finally
                {
                    isUpdating = false;
                }
            }

            void ApplyToUI(float v)
            {
                WriteSize(v);
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
                float now = ReadSize();
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
                    lastSent = ReadSize();
                    ApplyToUI(observable.Value);
                }
                else
                {
                    lastSent = ReadSize();
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
                lastSent = ReadSize();
            }

            BindingInfo info = new()
            {
                Kind = "TMP_Text.FontSize",
                Direction = direction.ToString(),
                Description = $"fontSize ↔ Observable<float> @ {phase}",
                Target = label,
                Owner = owner,
                Anchor = anchor,
                Options = options,
                GetState = () => $"fontSize={label.fontSize:0.###}",
                Tags = new[] { "TMP_Text", "FontSize", phase.ToString() }
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