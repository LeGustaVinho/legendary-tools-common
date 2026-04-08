using System;
using UnityEngine;

namespace LargeWorldCoordinates
{
    /// <summary>
    /// Converts positions between local Unity coordinates and shared global coordinates.
    /// This type is independent of MonoBehaviour and is suitable for tests and reuse.
    /// </summary>
    public sealed class GlobalCoordinateConverter : ICoordinateConverter
    {
        private const MidpointRounding RoundingMode = MidpointRounding.AwayFromZero;

        /// <summary>
        /// Initializes a new instance of the <see cref="GlobalCoordinateConverter"/> class.
        /// </summary>
        /// <param name="localOriginGlobal">The global position represented by local coordinate (0, 0, 0).</param>
        public GlobalCoordinateConverter(GlobalPosition localOriginGlobal)
        {
            LocalOriginGlobal = localOriginGlobal;
        }

        /// <inheritdoc />
        public GlobalPosition LocalOriginGlobal { get; set; }

        /// <inheritdoc />
        public GlobalPosition LocalToGlobal(Vector3 localPosition)
        {
            long x = checked(LocalOriginGlobal.X + UnityUnitsToGlobalUnits(localPosition.x));
            long y = checked(LocalOriginGlobal.Y + UnityUnitsToGlobalUnits(localPosition.y));
            long z = checked(LocalOriginGlobal.Z + UnityUnitsToGlobalUnits(localPosition.z));

            return new GlobalPosition(x, y, z);
        }

        /// <inheritdoc />
        public Vector3 GlobalToLocal(GlobalPosition globalPosition)
        {
            long dx = checked(globalPosition.X - LocalOriginGlobal.X);
            long dy = checked(globalPosition.Y - LocalOriginGlobal.Y);
            long dz = checked(globalPosition.Z - LocalOriginGlobal.Z);

            return new Vector3(
                GlobalUnitsToUnityUnits(dx),
                GlobalUnitsToUnityUnits(dy),
                GlobalUnitsToUnityUnits(dz));
        }

        /// <inheritdoc />
        public GlobalVector LocalDeltaToGlobalDelta(Vector3 localDelta)
        {
            return new GlobalVector(
                UnityUnitsToGlobalUnits(localDelta.x),
                UnityUnitsToGlobalUnits(localDelta.y),
                UnityUnitsToGlobalUnits(localDelta.z));
        }

        /// <inheritdoc />
        public Vector3 GlobalDeltaToLocalDelta(GlobalVector globalDelta)
        {
            return new Vector3(
                GlobalUnitsToUnityUnits(globalDelta.X),
                GlobalUnitsToUnityUnits(globalDelta.Y),
                GlobalUnitsToUnityUnits(globalDelta.Z));
        }

        /// <inheritdoc />
        public Quaternion LocalToGlobalRotation(Quaternion localRotation)
        {
            return localRotation;
        }

        /// <inheritdoc />
        public Quaternion GlobalToLocalRotation(Quaternion globalRotation)
        {
            return globalRotation;
        }

        /// <inheritdoc />
        public Vector3 LocalToGlobalDirection(Vector3 localDirection)
        {
            return localDirection;
        }

        /// <inheritdoc />
        public Vector3 GlobalToLocalDirection(Vector3 globalDirection)
        {
            return globalDirection;
        }

        /// <inheritdoc />
        public GlobalBounds LocalToGlobal(Bounds localBounds)
        {
            return GlobalBounds.FromMinMax(
                LocalToGlobal(localBounds.min),
                LocalToGlobal(localBounds.max));
        }

        /// <inheritdoc />
        public Bounds GlobalToLocal(GlobalBounds globalBounds)
        {
            return CreateBoundsFromMinMax(
                GlobalToLocal(globalBounds.Min),
                GlobalToLocal(globalBounds.Max));
        }

        /// <inheritdoc />
        public Vector3 ConvertLocalPositionTo(Vector3 localPosition, ICoordinateConverter destinationConverter)
        {
            if (destinationConverter == null)
            {
                throw new ArgumentNullException(nameof(destinationConverter));
            }

            return destinationConverter.GlobalToLocal(LocalToGlobal(localPosition));
        }

        /// <inheritdoc />
        public Vector3 ConvertLocalDeltaTo(Vector3 localDelta, ICoordinateConverter destinationConverter)
        {
            if (destinationConverter == null)
            {
                throw new ArgumentNullException(nameof(destinationConverter));
            }

            return destinationConverter.GlobalDeltaToLocalDelta(LocalDeltaToGlobalDelta(localDelta));
        }

        /// <inheritdoc />
        public Bounds ConvertLocalBoundsTo(Bounds localBounds, ICoordinateConverter destinationConverter)
        {
            if (destinationConverter == null)
            {
                throw new ArgumentNullException(nameof(destinationConverter));
            }

            return destinationConverter.GlobalToLocal(LocalToGlobal(localBounds));
        }

        /// <summary>
        /// Converts Unity units to global units using standardized rounding.
        /// </summary>
        /// <param name="unityUnits">The value in Unity units.</param>
        /// <returns>The equivalent value in global units.</returns>
        public static long UnityUnitsToGlobalUnits(double unityUnits)
        {
            double scaledValue = unityUnits * WorldScale.GlobalUnitsPerUnityUnit;
            return checked((long)Math.Round(scaledValue, 0, RoundingMode));
        }

        /// <summary>
        /// Converts global units to Unity units.
        /// </summary>
        /// <param name="globalUnits">The value in global units.</param>
        /// <returns>The equivalent value in Unity units.</returns>
        public static float GlobalUnitsToUnityUnits(long globalUnits)
        {
            double unityUnits = globalUnits / WorldScale.GlobalUnitsPerUnityUnit;
            return (float)unityUnits;
        }

        private static Bounds CreateBoundsFromMinMax(Vector3 min, Vector3 max)
        {
            Bounds bounds = new Bounds();
            bounds.SetMinMax(Vector3.Min(min, max), Vector3.Max(min, max));
            return bounds;
        }
    }
}
