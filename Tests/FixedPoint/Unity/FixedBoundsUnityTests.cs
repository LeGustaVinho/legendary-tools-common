using NUnit.Framework;
using UnityEngine;

namespace LegendaryTools.FixedPoint.Tests
{
    public sealed class FixedBoundsUnityTests
    {
        [OneTimeSetUp] public void Initialize()
        { FixedRuntime.Initialize(); if (!FixedBoundsRuntime.IsInitialized) FixedBoundsRuntime.Initialize(); }
        [Test] public void ExplicitBoundsAndRectConversionsRoundTripRepresentableCoordinates()
        {
            var bounds = new Bounds(new Vector3(2, 4, 6), new Vector3(4, 6, 8)); var fixedBounds = bounds.ToFixedBounds3(out var status);
            Assert.IsTrue(status.IsSuccess()); Assert.IsTrue(fixedBounds.IsValid); Assert.AreEqual(bounds.min, fixedBounds.ToUnityBounds().min); Assert.AreEqual(bounds.max, fixedBounds.ToUnityBounds().max);
            var rect = Rect.MinMaxRect(-3, -4, 5, 6); var fixedRect = rect.ToFixedBounds2(out status); Assert.IsTrue(status.IsSuccess()); Assert.AreEqual(rect, fixedRect.ToUnityRect());
            var ray = new Ray(Vector3.zero, Vector3.right).ToFixedRay3(out status); Assert.IsTrue(status.IsSuccess()); Assert.AreEqual(FixedVector3.right, ray.direction);
            var ray2 = new Ray2D(Vector2.zero, Vector2.up).ToFixedRay2(out status); Assert.IsTrue(status.IsSuccess()); Assert.AreEqual(FixedVector2.up, ray2.direction);
        }
        [Test] public void InvalidUnityGeometryCannotBecomeValidSimulationState()
        {
            var reversed = new Bounds(Vector3.zero, new Vector3(-2, 2, 2)); Assert.IsFalse(reversed.ToFixedBounds3(out var status).IsValid); Assert.IsFalse(status.IsSuccess());
            var infinite = new Bounds(new Vector3(float.PositiveInfinity, 0, 0), Vector3.one); Assert.IsFalse(infinite.ToFixedBounds3(out status).IsValid); Assert.IsFalse(status.IsSuccess());
            Assert.IsFalse(new Rect(0, 0, -1, 1).ToFixedBounds2(out status).IsValid); Assert.IsFalse(status.IsSuccess());
        }
    }
}
