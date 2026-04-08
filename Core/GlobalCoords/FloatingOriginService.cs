using System;
using UnityEngine;

namespace LargeWorldCoordinates
{
    /// <summary>
    /// Manages floating-origin recentering for a local player while preserving shared global coordinates.
    /// This type is independent of MonoBehaviour and depends only on abstractions.
    /// </summary>
    public sealed class FloatingOriginService
    {
        private readonly ICoordinateConverter coordinateConverter;
        private readonly ILocalPositionProvider anchor;
        private readonly IWorldShifter worldShifter;
        private float recenterDistance;

        /// <summary>
        /// Initializes a new instance of the <see cref="FloatingOriginService"/> class.
        /// </summary>
        /// <param name="coordinateConverter">The coordinate converter used by the local player.</param>
        /// <param name="anchor">The local position source used as recenter reference.</param>
        /// <param name="worldShifter">The world shifter used to move the local world representation.</param>
        /// <param name="recenterDistance">The distance from local origin that triggers recentering.</param>
        public FloatingOriginService(
            ICoordinateConverter coordinateConverter,
            ILocalPositionProvider anchor,
            IWorldShifter worldShifter,
            float recenterDistance)
        {
            this.coordinateConverter = coordinateConverter ?? throw new ArgumentNullException(nameof(coordinateConverter));
            this.anchor = anchor ?? throw new ArgumentNullException(nameof(anchor));
            this.worldShifter = worldShifter ?? throw new ArgumentNullException(nameof(worldShifter));
            RecenterDistance = recenterDistance;
        }

        /// <summary>
        /// Raised after the local origin is recentered.
        /// The first parameter is the local offset removed from the world.
        /// The second parameter is the new global value of local origin.
        /// </summary>
        public event Action<Vector3, GlobalPosition> Recentered;

        /// <summary>
        /// Gets or sets the distance from local origin that triggers recentering.
        /// </summary>
        public float RecenterDistance
        {
            get => recenterDistance;
            set => recenterDistance = Mathf.Max(0.01f, value);
        }

        /// <summary>
        /// Determines whether the anchor is far enough from local origin to require recentering.
        /// </summary>
        /// <returns><see langword="true"/> if recentering is required; otherwise, <see langword="false"/>.</returns>
        public bool NeedsRecentering()
        {
            float thresholdSqr = recenterDistance * recenterDistance;
            return anchor.LocalPosition.sqrMagnitude >= thresholdSqr;
        }

        /// <summary>
        /// Recenters the local world so the anchor returns to the local origin.
        /// The anchor global position remains unchanged.
        /// </summary>
        /// <returns><see langword="true"/> if recentering occurred; otherwise, <see langword="false"/>.</returns>
        public bool TryRecenter()
        {
            if (!NeedsRecentering())
            {
                return false;
            }

            Vector3 anchorLocalPosition = anchor.LocalPosition;

            if (anchorLocalPosition == Vector3.zero)
            {
                return false;
            }

            GlobalPosition anchorGlobalPosition = coordinateConverter.LocalToGlobal(anchorLocalPosition);
            SetAnchorGlobalPosition(anchorGlobalPosition);
            return true;
        }

        /// <summary>
        /// Moves the anchor to the local origin while assigning it the provided global position.
        /// This is useful for direct global teleports of the local player.
        /// </summary>
        /// <param name="anchorGlobalPosition">The global position that the anchor should have after the operation.</param>
        public void SetAnchorGlobalPosition(GlobalPosition anchorGlobalPosition)
        {
            Vector3 anchorLocalPosition = anchor.LocalPosition;

            if (anchorLocalPosition != Vector3.zero)
            {
                worldShifter.ShiftWorld(-anchorLocalPosition);
            }

            coordinateConverter.LocalOriginGlobal = anchorGlobalPosition;
            Recentered?.Invoke(anchorLocalPosition, anchorGlobalPosition);
        }
    }
}
