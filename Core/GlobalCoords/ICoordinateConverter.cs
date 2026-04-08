using UnityEngine;

namespace LargeWorldCoordinates
{
    /// <summary>
    /// Converts positions between a player's local Unity space and the shared global space.
    /// </summary>
    public interface ICoordinateConverter
    {
        /// <summary>
        /// Gets or sets the global position represented by local coordinate (0, 0, 0).
        /// </summary>
        GlobalPosition LocalOriginGlobal { get; set; }

        /// <summary>
        /// Converts a local Unity position into a shared global position.
        /// </summary>
        /// <param name="localPosition">The local Unity position.</param>
        /// <returns>The equivalent shared global position.</returns>
        GlobalPosition LocalToGlobal(Vector3 localPosition);

        /// <summary>
        /// Converts a shared global position into a local Unity position.
        /// </summary>
        /// <param name="globalPosition">The shared global position.</param>
        /// <returns>The equivalent local Unity position.</returns>
        Vector3 GlobalToLocal(GlobalPosition globalPosition);

        /// <summary>
        /// Converts a local Unity delta into a shared global delta.
        /// </summary>
        /// <param name="localDelta">The local Unity delta.</param>
        /// <returns>The equivalent shared global delta.</returns>
        GlobalVector LocalDeltaToGlobalDelta(Vector3 localDelta);

        /// <summary>
        /// Converts a shared global delta into a local Unity delta.
        /// </summary>
        /// <param name="globalDelta">The shared global delta.</param>
        /// <returns>The equivalent local Unity delta.</returns>
        Vector3 GlobalDeltaToLocalDelta(GlobalVector globalDelta);

        /// <summary>
        /// Converts a local Unity rotation into a shared global rotation.
        /// </summary>
        /// <param name="localRotation">The local Unity rotation.</param>
        /// <returns>The equivalent shared global rotation.</returns>
        Quaternion LocalToGlobalRotation(Quaternion localRotation);

        /// <summary>
        /// Converts a shared global rotation into a local Unity rotation.
        /// </summary>
        /// <param name="globalRotation">The shared global rotation.</param>
        /// <returns>The equivalent local Unity rotation.</returns>
        Quaternion GlobalToLocalRotation(Quaternion globalRotation);

        /// <summary>
        /// Converts a local Unity direction into a shared global direction.
        /// </summary>
        /// <param name="localDirection">The local Unity direction.</param>
        /// <returns>The equivalent shared global direction.</returns>
        Vector3 LocalToGlobalDirection(Vector3 localDirection);

        /// <summary>
        /// Converts a shared global direction into a local Unity direction.
        /// </summary>
        /// <param name="globalDirection">The shared global direction.</param>
        /// <returns>The equivalent local Unity direction.</returns>
        Vector3 GlobalToLocalDirection(Vector3 globalDirection);

        /// <summary>
        /// Converts local Unity bounds into shared global bounds.
        /// </summary>
        /// <param name="localBounds">The local Unity bounds.</param>
        /// <returns>The equivalent shared global bounds.</returns>
        GlobalBounds LocalToGlobal(Bounds localBounds);

        /// <summary>
        /// Converts shared global bounds into local Unity bounds.
        /// </summary>
        /// <param name="globalBounds">The shared global bounds.</param>
        /// <returns>The equivalent local Unity bounds.</returns>
        Bounds GlobalToLocal(GlobalBounds globalBounds);

        /// <summary>
        /// Converts a local Unity position from this coordinate system into a destination local coordinate system.
        /// </summary>
        /// <param name="localPosition">The source local Unity position.</param>
        /// <param name="destinationConverter">The destination coordinate converter.</param>
        /// <returns>The equivalent position in the destination local coordinate system.</returns>
        Vector3 ConvertLocalPositionTo(Vector3 localPosition, ICoordinateConverter destinationConverter);

        /// <summary>
        /// Converts a local Unity delta from this coordinate system into a destination local coordinate system.
        /// </summary>
        /// <param name="localDelta">The source local Unity delta.</param>
        /// <param name="destinationConverter">The destination coordinate converter.</param>
        /// <returns>The equivalent delta in the destination local coordinate system.</returns>
        Vector3 ConvertLocalDeltaTo(Vector3 localDelta, ICoordinateConverter destinationConverter);

        /// <summary>
        /// Converts local Unity bounds from this coordinate system into a destination local coordinate system.
        /// </summary>
        /// <param name="localBounds">The source local Unity bounds.</param>
        /// <param name="destinationConverter">The destination coordinate converter.</param>
        /// <returns>The equivalent bounds in the destination local coordinate system.</returns>
        Bounds ConvertLocalBoundsTo(Bounds localBounds, ICoordinateConverter destinationConverter);
    }

    /// <summary>
    /// Provides the local Unity position used as the floating-origin anchor.
    /// </summary>
    public interface ILocalPositionProvider
    {
        /// <summary>
        /// Gets the current local Unity position of the anchor.
        /// </summary>
        Vector3 LocalPosition { get; }
    }

    /// <summary>
    /// Applies a shift to the local world representation.
    /// </summary>
    public interface IWorldShifter
    {
        /// <summary>
        /// Shifts the local world by the specified offset in Unity units.
        /// </summary>
        /// <param name="worldOffset">The offset to apply to the local world.</param>
        void ShiftWorld(Vector3 worldOffset);
    }
}
