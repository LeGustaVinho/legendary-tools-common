using System;
using UnityEngine;
using LegendaryTools; // Observable<T>
using LegendaryTools.Reactive;

namespace LegendaryTools.Reactive.Unity
{
    /// <summary>
    /// Extension methods to bind Observables with UnityEngine.Transform and GameObject (active).
    /// Supports Position (World/Local), Rotation (Quaternion/Euler, World/Local), Scale (local),
    /// and IsActive (GameObject.SetActive).
    /// </summary>
    public static class TransformBindings
    {
        // -------------------------
        // Helpers
        // -------------------------
        private const float DefaultPosEpsilon = 1e-5f;
        private const float DefaultEulerEpsilon = 0.01f; // degrees
        private const float DefaultQuatEpsilonDeg = 0.01f; // degrees for Quaternion.Angle
        private const float DefaultScaleEpsilon = 1e-5f;

        private static BindingUpdateDriver GetDriver(Component c)
        {
            return c.GetComponent<BindingUpdateDriver>() ?? c.gameObject.AddComponent<BindingUpdateDriver>();
        }

        private static GameObjectActiveWatcher GetActiveWatcher(Component c)
        {
            return c.GetComponent<GameObjectActiveWatcher>() ?? c.gameObject.AddComponent<GameObjectActiveWatcher>();
        }

        private static bool Approximately(Vector3 a, Vector3 b, float eps)
        {
            return (a - b).sqrMagnitude <= eps * eps;
        }

        private static bool Approximately(float a, float b, float eps)
        {
            return Mathf.Abs(a - b) <= eps;
        }

        // Normalize Euler so comparisons don't flip due to 0..360 wrap
        private static Vector3 NormalizeEuler(Vector3 e)
        {
            e.x = Mathf.Repeat(e.x, 360f);
            e.y = Mathf.Repeat(e.y, 360f);
            e.z = Mathf.Repeat(e.z, 360f);
            return e;
        }

        // -------------------------
        // POSITION (Vector3)
        // -------------------------
        public static BindingHandle BindPosition(
            this Transform target,
            Observable<Vector3> observable,
            Space space = Space.World,
            BindDirection direction = BindDirection.TwoWay,
            UpdatePhase phase = UpdatePhase.Update,
            MonoBehaviour owner = null,
            BindingOptions options = null,
            Func<Vector3, Vector3> toUI = null,
            Func<Vector3, Vector3> fromUI = null,
            float epsilon = DefaultPosEpsilon)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (observable == null) throw new ArgumentNullException(nameof(observable));

            BindingAnchor anchor = owner != null
                ? owner.GetComponent<BindingAnchor>() ?? owner.gameObject.AddComponent<BindingAnchor>()
                : null;
            BindingUpdateDriver driver = GetDriver(target);
            options ??= new BindingOptions();

            bool isUpdating = false;
            Vector3 lastSent = default;

            Vector3 ReadPos()
            {
                Vector3 v = space == Space.World ? target.position : target.localPosition;
                return toUI != null ? toUI(v) : v;
            }

            void WritePos(Vector3 v)
            {
                if (anchor != null && !anchor.ShouldProcessNow()) return;
                Vector3 inV = fromUI != null ? fromUI(v) : v;

                try
                {
                    isUpdating = true;
                    if (space == Space.World) target.position = inV;
                    else target.localPosition = inV;
                }
                finally
                {
                    isUpdating = false;
                }
            }

            void ApplyToUI(Vector3 v)
            {
                WritePos(v);
            }

            void OnObsChanged(IObservable<Vector3> _, Vector3 oldV, Vector3 newV)
            {
                if (direction == BindDirection.FromUI) return;
                ApplyToUI(newV);
            }

            void PollFromUI()
            {
                if (direction == BindDirection.ToUI) return;
                if (isUpdating) return;
                Vector3 now = ReadPos();
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
                    lastSent = ReadPos();
                    ApplyToUI(observable.Value);
                }
                else
                {
                    lastSent = ReadPos();
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
                lastSent = ReadPos();
            }

            BindingInfo info = new()
            {
                Kind = $"Transform.Position({space})",
                Direction = direction.ToString(),
                Description = $"position ({space}) ↔ Observable<Vector3> @ {phase}",
                Target = target,
                Owner = owner,
                Anchor = anchor,
                Options = options,
                GetState = () =>
                {
                    Vector3 p = space == Space.World ? target.position : target.localPosition;
                    return $"pos=({p.x:0.###},{p.y:0.###},{p.z:0.###})";
                },
                Tags = new[] { "Transform", "Position", space.ToString(), phase.ToString() }
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

        // -------------------------
        // ROTATION (Quaternion)
        // -------------------------
        public static BindingHandle BindRotation(
            this Transform target,
            Observable<Quaternion> observable,
            Space space = Space.World,
            BindDirection direction = BindDirection.TwoWay,
            UpdatePhase phase = UpdatePhase.Update,
            MonoBehaviour owner = null,
            BindingOptions options = null,
            Func<Quaternion, Quaternion> toUI = null,
            Func<Quaternion, Quaternion> fromUI = null,
            float angleEpsilonDeg = DefaultQuatEpsilonDeg)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (observable == null) throw new ArgumentNullException(nameof(observable));

            BindingAnchor anchor = owner != null
                ? owner.GetComponent<BindingAnchor>() ?? owner.gameObject.AddComponent<BindingAnchor>()
                : null;
            BindingUpdateDriver driver = GetDriver(target);
            options ??= new BindingOptions();

            bool isUpdating = false;
            Quaternion lastSent = Quaternion.identity;

            Quaternion ReadRot()
            {
                Quaternion q = space == Space.World ? target.rotation : target.localRotation;
                return toUI != null ? toUI(q) : q;
            }

            void WriteRot(Quaternion q)
            {
                if (anchor != null && !anchor.ShouldProcessNow()) return;
                Quaternion inQ = fromUI != null ? fromUI(q) : q;

                try
                {
                    isUpdating = true;
                    if (space == Space.World) target.rotation = inQ;
                    else target.localRotation = inQ;
                }
                finally
                {
                    isUpdating = false;
                }
            }

            void ApplyToUI(Quaternion q)
            {
                WriteRot(q);
            }

            void OnObsChanged(IObservable<Quaternion> _, Quaternion oldV, Quaternion newV)
            {
                if (direction == BindDirection.FromUI) return;
                ApplyToUI(newV);
            }

            void PollFromUI()
            {
                if (direction == BindDirection.ToUI) return;
                if (isUpdating) return;
                Quaternion now = ReadRot();
                if (Quaternion.Angle(now, lastSent) > angleEpsilonDeg)
                {
                    lastSent = now;
                    observable.Value = now;
                }
            }

            void Subscribe()
            {
                if (direction != BindDirection.FromUI)
                {
                    lastSent = ReadRot();
                    ApplyToUI(observable.Value);
                }
                else
                {
                    lastSent = ReadRot();
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
                lastSent = ReadRot();
            }

            BindingInfo info = new()
            {
                Kind = $"Transform.RotationQuat({space})",
                Direction = direction.ToString(),
                Description = $"rotation ({space}) ↔ Observable<Quaternion> @ {phase}",
                Target = target,
                Owner = owner,
                Anchor = anchor,
                Options = options,
                GetState = () =>
                {
                    Vector3 e = space == Space.World ? target.rotation.eulerAngles : target.localEulerAngles;
                    e = NormalizeEuler(e);
                    return $"rotEuler=({e.x:0.#},{e.y:0.#},{e.z:0.#})";
                },
                Tags = new[] { "Transform", "Rotation", "Quaternion", space.ToString(), phase.ToString() }
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

        // -------------------------
        // ROTATION EULER (Vector3 in degrees)
        // -------------------------
        public static BindingHandle BindRotationEuler(
            this Transform target,
            Observable<Vector3> observable,
            Space space = Space.World,
            BindDirection direction = BindDirection.TwoWay,
            UpdatePhase phase = UpdatePhase.Update,
            MonoBehaviour owner = null,
            BindingOptions options = null,
            Func<Vector3, Vector3> toUI = null,
            Func<Vector3, Vector3> fromUI = null,
            float eulerEpsilonDeg = DefaultEulerEpsilon)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (observable == null) throw new ArgumentNullException(nameof(observable));

            BindingAnchor anchor = owner != null
                ? owner.GetComponent<BindingAnchor>() ?? owner.gameObject.AddComponent<BindingAnchor>()
                : null;
            BindingUpdateDriver driver = GetDriver(target);
            options ??= new BindingOptions();

            bool isUpdating = false;
            Vector3 lastSent = Vector3.zero;

            Vector3 ReadEuler()
            {
                Vector3 e = space == Space.World ? target.eulerAngles : target.localEulerAngles;
                e = NormalizeEuler(e);
                return toUI != null ? toUI(e) : e;
            }

            void WriteEuler(Vector3 e)
            {
                if (anchor != null && !anchor.ShouldProcessNow()) return;
                Vector3 inE = fromUI != null ? fromUI(e) : e;

                try
                {
                    isUpdating = true;
                    if (space == Space.World) target.rotation = Quaternion.Euler(inE);
                    else target.localRotation = Quaternion.Euler(inE);
                }
                finally
                {
                    isUpdating = false;
                }
            }

            void ApplyToUI(Vector3 e)
            {
                WriteEuler(e);
            }

            void OnObsChanged(IObservable<Vector3> _, Vector3 oldV, Vector3 newV)
            {
                if (direction == BindDirection.FromUI) return;
                ApplyToUI(newV);
            }

            void PollFromUI()
            {
                if (direction == BindDirection.ToUI) return;
                if (isUpdating) return;
                Vector3 now = ReadEuler();
                now = NormalizeEuler(now);
                Vector3 delta = now - lastSent;
                if (Mathf.Abs(delta.x) > eulerEpsilonDeg ||
                    Mathf.Abs(delta.y) > eulerEpsilonDeg ||
                    Mathf.Abs(delta.z) > eulerEpsilonDeg)
                {
                    lastSent = now;
                    observable.Value = now;
                }
            }

            void Subscribe()
            {
                if (direction != BindDirection.FromUI)
                {
                    lastSent = ReadEuler();
                    ApplyToUI(observable.Value);
                }
                else
                {
                    lastSent = ReadEuler();
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
                lastSent = ReadEuler();
            }

            BindingInfo info = new()
            {
                Kind = $"Transform.RotationEuler({space})",
                Direction = direction.ToString(),
                Description = $"rotation-euler ({space}) ↔ Observable<Vector3> @ {phase}",
                Target = target,
                Owner = owner,
                Anchor = anchor,
                Options = options,
                GetState = () =>
                {
                    Vector3 e = space == Space.World ? target.eulerAngles : target.localEulerAngles;
                    e = NormalizeEuler(e);
                    return $"euler=({e.x:0.#},{e.y:0.#},{e.z:0.#})";
                },
                Tags = new[] { "Transform", "Rotation", "Euler", space.ToString(), phase.ToString() }
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

        // -------------------------
        // SCALE (local) Vector3
        // -------------------------
        public static BindingHandle BindScale(
            this Transform target,
            Observable<Vector3> observable,
            BindDirection direction = BindDirection.TwoWay,
            UpdatePhase phase = UpdatePhase.Update,
            MonoBehaviour owner = null,
            BindingOptions options = null,
            Func<Vector3, Vector3> toUI = null,
            Func<Vector3, Vector3> fromUI = null,
            float epsilon = DefaultScaleEpsilon)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (observable == null) throw new ArgumentNullException(nameof(observable));

            BindingAnchor anchor = owner != null
                ? owner.GetComponent<BindingAnchor>() ?? owner.gameObject.AddComponent<BindingAnchor>()
                : null;
            BindingUpdateDriver driver = GetDriver(target);
            options ??= new BindingOptions();

            bool isUpdating = false;
            Vector3 lastSent = Vector3.one;

            Vector3 ReadScale()
            {
                Vector3 s = target.localScale;
                return toUI != null ? toUI(s) : s;
            }

            void WriteScale(Vector3 s)
            {
                if (anchor != null && !anchor.ShouldProcessNow()) return;
                Vector3 inS = fromUI != null ? fromUI(s) : s;

                try
                {
                    isUpdating = true;
                    target.localScale = inS;
                }
                finally
                {
                    isUpdating = false;
                }
            }

            void ApplyToUI(Vector3 s)
            {
                WriteScale(s);
            }

            void OnObsChanged(IObservable<Vector3> _, Vector3 oldV, Vector3 newV)
            {
                if (direction == BindDirection.FromUI) return;
                ApplyToUI(newV);
            }

            void PollFromUI()
            {
                if (direction == BindDirection.ToUI) return;
                if (isUpdating) return;
                Vector3 now = ReadScale();
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
                    lastSent = ReadScale();
                    ApplyToUI(observable.Value);
                }
                else
                {
                    lastSent = ReadScale();
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
                lastSent = ReadScale();
            }

            BindingInfo info = new()
            {
                Kind = "Transform.Scale(Local)",
                Direction = direction.ToString(),
                Description = $"scale (local) ↔ Observable<Vector3> @ {phase}",
                Target = target,
                Owner = owner,
                Anchor = anchor,
                Options = options,
                GetState = () =>
                {
                    Vector3 s = target.localScale;
                    return $"scale=({s.x:0.###},{s.y:0.###},{s.z:0.###})";
                },
                Tags = new[] { "Transform", "Scale", "Local", phase.ToString() }
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

        // -------------------------
        // UNIFORM SCALE (local) float
        // -------------------------
        public static BindingHandle BindUniformScale(
            this Transform target,
            Observable<float> observable,
            BindDirection direction = BindDirection.TwoWay,
            UpdatePhase phase = UpdatePhase.Update,
            MonoBehaviour owner = null,
            BindingOptions options = null,
            Func<float, float> toUI = null,
            Func<float, float> fromUI = null,
            float epsilon = DefaultScaleEpsilon)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (observable == null) throw new ArgumentNullException(nameof(observable));

            BindingAnchor anchor = owner != null
                ? owner.GetComponent<BindingAnchor>() ?? owner.gameObject.AddComponent<BindingAnchor>()
                : null;
            BindingUpdateDriver driver = GetDriver(target);
            options ??= new BindingOptions();

            bool isUpdating = false;
            float lastSent = 1f;

            float ReadUniform()
            {
                // average of xyz (simple heuristic)
                Vector3 s = target.localScale;
                float v = (s.x + s.y + s.z) / 3f;
                return toUI != null ? toUI(v) : v;
            }

            void WriteUniform(float v)
            {
                if (anchor != null && !anchor.ShouldProcessNow()) return;
                float inV = fromUI != null ? fromUI(v) : v;

                try
                {
                    isUpdating = true;
                    target.localScale = Vector3.one * inV;
                }
                finally
                {
                    isUpdating = false;
                }
            }

            void ApplyToUI(float v)
            {
                WriteUniform(v);
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
                float now = ReadUniform();
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
                    lastSent = ReadUniform();
                    ApplyToUI(observable.Value);
                }
                else
                {
                    lastSent = ReadUniform();
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
                lastSent = ReadUniform();
            }

            BindingInfo info = new()
            {
                Kind = "Transform.UniformScale(Local)",
                Direction = direction.ToString(),
                Description = $"scale-uniform (local) ↔ Observable<float> @ {phase}",
                Target = target,
                Owner = owner,
                Anchor = anchor,
                Options = options,
                GetState = () =>
                    $"uScale={(target.localScale.x + target.localScale.y + target.localScale.z) / 3f:0.###}",
                Tags = new[] { "Transform", "Scale", "Uniform", "Local", phase.ToString() }
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

        // -------------------------
        // ACTIVE (GameObject)
        // -------------------------
        public static BindingHandle BindActive(
            this Transform target,
            Observable<bool> observable,
            BindDirection direction = BindDirection.ToUI, // ToUI is most common
            MonoBehaviour owner = null,
            BindingOptions options = null)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (observable == null) throw new ArgumentNullException(nameof(observable));

            GameObject go = target.gameObject;
            BindingAnchor anchor = owner != null
                ? owner.GetComponent<BindingAnchor>() ?? owner.gameObject.AddComponent<BindingAnchor>()
                : null;
            options ??= new BindingOptions();

            bool isUpdating = false;
            GameObjectActiveWatcher watcher = GetActiveWatcher(target);

            void ApplyToUI(bool v)
            {
                if (anchor != null && !anchor.ShouldProcessNow()) return;
                try
                {
                    isUpdating = true;
                    if (go.activeSelf != v) go.SetActive(v);
                }
                finally
                {
                    isUpdating = false;
                }
            }

            void OnObsChanged(IObservable<bool> _, bool oldV, bool newV)
            {
                if (direction == BindDirection.FromUI) return;
                ApplyToUI(newV);
            }

            void OnActiveChanged(bool active)
            {
                if (direction == BindDirection.ToUI) return;
                if (isUpdating) return;
                observable.Value = active;
            }

            void Subscribe()
            {
                if (direction != BindDirection.FromUI)
                    ApplyToUI(observable.Value);

                observable.OnChanged += OnObsChanged;

                if (direction != BindDirection.ToUI)
                    watcher.OnActiveChanged += OnActiveChanged;
            }

            void Unsubscribe()
            {
                observable.OnChanged -= OnObsChanged;

                if (direction != BindDirection.ToUI)
                    watcher.OnActiveChanged -= OnActiveChanged;
            }

            void Resync()
            {
                if (direction != BindDirection.FromUI)
                    ApplyToUI(observable.Value);
            }

            BindingInfo info = new()
            {
                Kind = "Transform.Active",
                Direction = direction.ToString(),
                Description = "GameObject.active ↔ Observable<bool>",
                Target = target,
                Owner = owner,
                Anchor = anchor,
                Options = options,
                GetState = () => $"active={go.activeSelf}",
                Tags = new[] { "Transform", "Active" }
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