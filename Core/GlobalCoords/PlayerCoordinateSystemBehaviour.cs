using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LargeWorldCoordinates
{
    /// <summary>
    /// Unity composition root for the player's local/global coordinate system.
    /// This component wires Unity objects into the pure coordinate and floating-origin services.
    /// </summary>
    public sealed class PlayerCoordinateSystemBehaviour : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("The player transform used as the floating-origin anchor.")]
        [SerializeField] private Transform playerTransform;

        [Tooltip("The root that contains the player's local world representation. " +
                 "This root should include the player and all objects that must shift during recentering.")]
        [SerializeField] private Transform worldRoot;

        [Header("Settings")]
        [Tooltip("The local distance from origin that triggers recentering.")]
        [Min(1f)]
        [SerializeField] private float recenterDistance = 10000f;

        [Tooltip("If enabled, recentering runs automatically during LateUpdate.")]
        [SerializeField] private bool autoRecenter = true;

        [Header("Initial State")]
        [Tooltip("The global position represented by local coordinate (0, 0, 0).")]
        [SerializeField] private GlobalPosition localOriginGlobal = default;

        private GlobalCoordinateConverter coordinateConverter;
        private FloatingOriginService floatingOriginService;

        /// <summary>
        /// Gets the current coordinate converter.
        /// </summary>
        public ICoordinateConverter CoordinateConverter => GetCoordinateConverter();

        /// <summary>
        /// Gets the current floating-origin service.
        /// </summary>
        public FloatingOriginService FloatingOriginService
        {
            get
            {
                EnsureInitialized();
                return floatingOriginService;
            }
        }

        /// <summary>
        /// Gets the current global position represented by local coordinate (0, 0, 0).
        /// </summary>
        public GlobalPosition LocalOriginGlobal =>
            coordinateConverter != null ? coordinateConverter.LocalOriginGlobal : localOriginGlobal;

        private void Reset()
        {
            playerTransform = transform;
            worldRoot = transform.root;
        }

        private void Awake()
        {
            EnsureInitialized();
        }

        private void OnDestroy()
        {
            if (floatingOriginService != null)
            {
                floatingOriginService.Recentered -= OnRecentered;
            }
        }

        private void OnValidate()
        {
            recenterDistance = Mathf.Max(1f, recenterDistance);

            if (floatingOriginService != null)
            {
                floatingOriginService.RecenterDistance = recenterDistance;
            }
        }

        private void LateUpdate()
        {
            if (!autoRecenter || floatingOriginService == null)
            {
                return;
            }

            if (floatingOriginService.TryRecenter())
            {
                localOriginGlobal = coordinateConverter.LocalOriginGlobal;
            }
        }

        /// <summary>
        /// Converts a local Unity position to a shared global position.
        /// </summary>
        /// <param name="localPosition">The local Unity position.</param>
        /// <returns>The equivalent shared global position.</returns>
        public GlobalPosition LocalToGlobal(Vector3 localPosition)
        {
            return GetCoordinateConverter().LocalToGlobal(localPosition);
        }

        /// <summary>
        /// Converts a shared global position to this player's local Unity position.
        /// </summary>
        /// <param name="globalPosition">The shared global position.</param>
        /// <returns>The equivalent local Unity position for this player.</returns>
        public Vector3 GlobalToLocal(GlobalPosition globalPosition)
        {
            return GetCoordinateConverter().GlobalToLocal(globalPosition);
        }

        /// <summary>
        /// Converts a local Unity delta to a shared global delta.
        /// </summary>
        /// <param name="localDelta">The local Unity delta.</param>
        /// <returns>The equivalent shared global delta.</returns>
        public GlobalVector LocalDeltaToGlobalDelta(Vector3 localDelta)
        {
            return GetCoordinateConverter().LocalDeltaToGlobalDelta(localDelta);
        }

        /// <summary>
        /// Converts a shared global delta to this player's local Unity delta.
        /// </summary>
        /// <param name="globalDelta">The shared global delta.</param>
        /// <returns>The equivalent local Unity delta.</returns>
        public Vector3 GlobalDeltaToLocalDelta(GlobalVector globalDelta)
        {
            return GetCoordinateConverter().GlobalDeltaToLocalDelta(globalDelta);
        }

        /// <summary>
        /// Converts local Unity bounds into shared global bounds.
        /// </summary>
        /// <param name="localBounds">The local Unity bounds.</param>
        /// <returns>The equivalent shared global bounds.</returns>
        public GlobalBounds LocalToGlobal(Bounds localBounds)
        {
            return GetCoordinateConverter().LocalToGlobal(localBounds);
        }

        /// <summary>
        /// Converts shared global bounds into this player's local Unity bounds.
        /// </summary>
        /// <param name="globalBounds">The shared global bounds.</param>
        /// <returns>The equivalent local Unity bounds.</returns>
        public Bounds GlobalToLocal(GlobalBounds globalBounds)
        {
            return GetCoordinateConverter().GlobalToLocal(globalBounds);
        }

        /// <summary>
        /// Converts a local Unity position from this coordinate system into a destination local coordinate system.
        /// </summary>
        /// <param name="localPosition">The source local Unity position.</param>
        /// <param name="destinationSystem">The destination coordinate system.</param>
        /// <returns>The equivalent position in the destination local coordinate system.</returns>
        public Vector3 ConvertLocalPositionTo(Vector3 localPosition, PlayerCoordinateSystemBehaviour destinationSystem)
        {
            if (destinationSystem == null)
            {
                throw new ArgumentNullException(nameof(destinationSystem));
            }

            return GetCoordinateConverter().ConvertLocalPositionTo(
                localPosition,
                destinationSystem.GetCoordinateConverter());
        }

        /// <summary>
        /// Converts a local Unity delta from this coordinate system into a destination local coordinate system.
        /// </summary>
        /// <param name="localDelta">The source local Unity delta.</param>
        /// <param name="destinationSystem">The destination coordinate system.</param>
        /// <returns>The equivalent delta in the destination local coordinate system.</returns>
        public Vector3 ConvertLocalDeltaTo(Vector3 localDelta, PlayerCoordinateSystemBehaviour destinationSystem)
        {
            if (destinationSystem == null)
            {
                throw new ArgumentNullException(nameof(destinationSystem));
            }

            return GetCoordinateConverter().ConvertLocalDeltaTo(
                localDelta,
                destinationSystem.GetCoordinateConverter());
        }

        /// <summary>
        /// Converts local Unity bounds from this coordinate system into a destination local coordinate system.
        /// </summary>
        /// <param name="localBounds">The source local Unity bounds.</param>
        /// <param name="destinationSystem">The destination coordinate system.</param>
        /// <returns>The equivalent bounds in the destination local coordinate system.</returns>
        public Bounds ConvertBoundsTo(Bounds localBounds, PlayerCoordinateSystemBehaviour destinationSystem)
        {
            if (destinationSystem == null)
            {
                throw new ArgumentNullException(nameof(destinationSystem));
            }

            return GetCoordinateConverter().ConvertLocalBoundsTo(
                localBounds,
                destinationSystem.GetCoordinateConverter());
        }

        /// <summary>
        /// Gets the current global position of the player.
        /// </summary>
        /// <returns>The player's shared global position.</returns>
        public GlobalPosition GetPlayerGlobalPosition()
        {
            EnsureInitialized();
            return GetGlobalPosition(playerTransform);
        }

        /// <summary>
        /// Gets the current global position of the provided transform.
        /// </summary>
        /// <param name="target">The transform to inspect.</param>
        /// <returns>The transform shared global position.</returns>
        public GlobalPosition GetGlobalPosition(Transform target)
        {
            return GetCoordinateConverter().LocalToGlobal(GetRequiredTarget(target).position);
        }

        /// <summary>
        /// Gets the current global rotation of the provided transform.
        /// </summary>
        /// <param name="target">The transform to inspect.</param>
        /// <returns>The transform shared global rotation.</returns>
        public Quaternion GetGlobalRotation(Transform target)
        {
            return GetCoordinateConverter().LocalToGlobalRotation(GetRequiredTarget(target).rotation);
        }

        /// <summary>
        /// Converts an object-local direction into a shared global direction.
        /// </summary>
        /// <param name="target">The transform that defines the local direction basis.</param>
        /// <param name="localDirection">The direction in the target local space.</param>
        /// <returns>The equivalent shared global direction.</returns>
        public Vector3 GetGlobalDirection(Transform target, Vector3 localDirection)
        {
            Transform requiredTarget = GetRequiredTarget(target);
            return GetCoordinateConverter().LocalToGlobalDirection(requiredTarget.TransformDirection(localDirection));
        }

        /// <summary>
        /// Converts an object-local delta into a shared global delta.
        /// </summary>
        /// <param name="target">The transform that defines the local delta basis.</param>
        /// <param name="localDelta">The delta in the target local space.</param>
        /// <returns>The equivalent shared global delta.</returns>
        public GlobalVector GetGlobalVector(Transform target, Vector3 localDelta)
        {
            Transform requiredTarget = GetRequiredTarget(target);
            return GetCoordinateConverter().LocalDeltaToGlobalDelta(requiredTarget.TransformVector(localDelta));
        }

        /// <summary>
        /// Sets the local origin global value.
        /// </summary>
        /// <param name="newLocalOriginGlobal">The new global position represented by local coordinate (0, 0, 0).</param>
        public void SetLocalOriginGlobal(GlobalPosition newLocalOriginGlobal)
        {
            EnsureInitialized();
            localOriginGlobal = newLocalOriginGlobal;
            coordinateConverter.LocalOriginGlobal = newLocalOriginGlobal;
        }

        /// <summary>
        /// Teleports the provided transform to the specified shared global position.
        /// </summary>
        /// <param name="target">The transform to teleport.</param>
        /// <param name="globalPosition">The destination shared global position.</param>
        public void TeleportGlobal(Transform target, GlobalPosition globalPosition)
        {
            TeleportGlobal(target, globalPosition, GetGlobalRotation(target));
        }

        /// <summary>
        /// Teleports the provided transform to the specified shared global pose.
        /// </summary>
        /// <param name="target">The transform to teleport.</param>
        /// <param name="globalPosition">The destination shared global position.</param>
        /// <param name="globalRotation">The destination shared global rotation.</param>
        public void TeleportGlobal(Transform target, GlobalPosition globalPosition, Quaternion globalRotation)
        {
            Transform requiredTarget = GetRequiredTarget(target);
            ICoordinateConverter converter = GetCoordinateConverter();

            requiredTarget.SetPositionAndRotation(
                converter.GlobalToLocal(globalPosition),
                converter.GlobalToLocalRotation(globalRotation));
        }

        /// <summary>
        /// Teleports the local player directly to the specified shared global position.
        /// </summary>
        /// <param name="globalPosition">The destination shared global position.</param>
        public void TeleportPlayerGlobal(GlobalPosition globalPosition)
        {
            TeleportPlayerGlobal(globalPosition, GetGlobalRotation(GetPlayerTransform()));
        }

        /// <summary>
        /// Teleports the local player directly to the specified shared global pose.
        /// The player is recentered to local origin to preserve floating-origin precision.
        /// </summary>
        /// <param name="globalPosition">The destination shared global position.</param>
        /// <param name="globalRotation">The destination shared global rotation.</param>
        public void TeleportPlayerGlobal(GlobalPosition globalPosition, Quaternion globalRotation)
        {
            EnsureInitialized();

            floatingOriginService.SetAnchorGlobalPosition(globalPosition);
            playerTransform.SetPositionAndRotation(
                Vector3.zero,
                coordinateConverter.GlobalToLocalRotation(globalRotation));
        }

        /// <summary>
        /// Spawns an object directly at the specified shared global position.
        /// </summary>
        /// <typeparam name="T">The prefab type.</typeparam>
        /// <param name="prefab">The prefab to instantiate.</param>
        /// <param name="globalPosition">The destination shared global position.</param>
        /// <returns>The instantiated object.</returns>
        public T SpawnAtGlobal<T>(T prefab, GlobalPosition globalPosition) where T : Object
        {
            return SpawnAtGlobal(prefab, globalPosition, Quaternion.identity, null);
        }

        /// <summary>
        /// Spawns an object directly at the specified shared global pose.
        /// </summary>
        /// <typeparam name="T">The prefab type.</typeparam>
        /// <param name="prefab">The prefab to instantiate.</param>
        /// <param name="globalPosition">The destination shared global position.</param>
        /// <param name="globalRotation">The destination shared global rotation.</param>
        /// <returns>The instantiated object.</returns>
        public T SpawnAtGlobal<T>(T prefab, GlobalPosition globalPosition, Quaternion globalRotation) where T : Object
        {
            return SpawnAtGlobal(prefab, globalPosition, globalRotation, null);
        }

        /// <summary>
        /// Spawns an object directly at the specified shared global pose and parent.
        /// </summary>
        /// <typeparam name="T">The prefab type.</typeparam>
        /// <param name="prefab">The prefab to instantiate.</param>
        /// <param name="globalPosition">The destination shared global position.</param>
        /// <param name="globalRotation">The destination shared global rotation.</param>
        /// <param name="parent">The optional parent transform.</param>
        /// <returns>The instantiated object.</returns>
        public T SpawnAtGlobal<T>(T prefab, GlobalPosition globalPosition, Quaternion globalRotation, Transform parent) where T : Object
        {
            if (prefab == null)
            {
                throw new ArgumentNullException(nameof(prefab));
            }

            ICoordinateConverter converter = GetCoordinateConverter();
            Vector3 localPosition = converter.GlobalToLocal(globalPosition);
            Quaternion localRotation = converter.GlobalToLocalRotation(globalRotation);

            return parent == null
                ? Object.Instantiate(prefab, localPosition, localRotation)
                : Object.Instantiate(prefab, localPosition, localRotation, parent);
        }

        /// <summary>
        /// Forces an immediate recenter operation.
        /// </summary>
        [ContextMenu("Force Recenter")]
        public void ForceRecenter()
        {
            EnsureInitialized();
            if (floatingOriginService != null && floatingOriginService.TryRecenter())
            {
                localOriginGlobal = coordinateConverter.LocalOriginGlobal;
            }
        }

        private void OnRecentered(Vector3 shiftedLocalOffset, GlobalPosition newLocalOriginGlobal)
        {
            localOriginGlobal = newLocalOriginGlobal;
        }

        private void EnsureInitialized()
        {
            if (playerTransform == null)
            {
                playerTransform = transform;
            }

            if (worldRoot == null)
            {
                worldRoot = transform.root;
            }

            if (coordinateConverter == null)
            {
                coordinateConverter = new GlobalCoordinateConverter(localOriginGlobal);
            }
            else
            {
                coordinateConverter.LocalOriginGlobal = localOriginGlobal;
            }

            if (floatingOriginService == null)
            {
                floatingOriginService = new FloatingOriginService(
                    coordinateConverter,
                    new TransformLocalPositionProvider(playerTransform),
                    new TransformWorldShifter(worldRoot),
                    recenterDistance);

                floatingOriginService.Recentered += OnRecentered;
            }
            else
            {
                floatingOriginService.RecenterDistance = recenterDistance;
            }
        }

        private ICoordinateConverter GetCoordinateConverter()
        {
            EnsureInitialized();
            return coordinateConverter;
        }

        private Transform GetPlayerTransform()
        {
            EnsureInitialized();
            return GetRequiredTarget(playerTransform);
        }

        private static Transform GetRequiredTarget(Transform target)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            return target;
        }

        /// <summary>
        /// Adapts a Unity transform to an <see cref="ILocalPositionProvider"/>.
        /// </summary>
        private sealed class TransformLocalPositionProvider : ILocalPositionProvider
        {
            private readonly Transform target;

            /// <summary>
            /// Initializes a new instance of the <see cref="TransformLocalPositionProvider"/> class.
            /// </summary>
            /// <param name="target">The transform that provides the local position.</param>
            public TransformLocalPositionProvider(Transform target)
            {
                this.target = target ?? throw new ArgumentNullException(nameof(target));
            }

            /// <inheritdoc />
            public Vector3 LocalPosition => target.position;
        }

        /// <summary>
        /// Adapts a Unity transform to an <see cref="IWorldShifter"/>.
        /// </summary>
        private sealed class TransformWorldShifter : IWorldShifter
        {
            private readonly Transform target;

            /// <summary>
            /// Initializes a new instance of the <see cref="TransformWorldShifter"/> class.
            /// </summary>
            /// <param name="target">The transform that shifts the local world.</param>
            public TransformWorldShifter(Transform target)
            {
                this.target = target ?? throw new ArgumentNullException(nameof(target));
            }

            /// <inheritdoc />
            public void ShiftWorld(Vector3 worldOffset)
            {
                target.position += worldOffset;
            }
        }
    }
}
