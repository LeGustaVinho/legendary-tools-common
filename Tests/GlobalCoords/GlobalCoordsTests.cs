using System.Reflection;
using LargeWorldCoordinates;
using NUnit.Framework;
using UnityEngine;

namespace LegendaryTools.Tests.LargeWorldCoordinates
{
    public class GlobalCoordsTests
    {
        [Test]
        public void Converter_ConvertsDeltasBoundsAndSystems()
        {
            var source = new GlobalCoordinateConverter(new GlobalPosition(1000L, -500L, 250L));
            var destination = new GlobalCoordinateConverter(new GlobalPosition(-300L, 250L, 1000L));

            Vector3 localDelta = new Vector3(1.25f, -0.5f, 2f);
            GlobalVector globalDelta = source.LocalDeltaToGlobalDelta(localDelta);

            Assert.AreEqual(new GlobalVector(125L, -50L, 200L), globalDelta);
            AssertVector3(localDelta, source.GlobalDeltaToLocalDelta(globalDelta));

            Bounds localBounds = new Bounds(new Vector3(4f, 1f, -2f), new Vector3(2f, 6f, 4f));
            GlobalBounds globalBounds = source.LocalToGlobal(localBounds);
            Bounds sourceRoundTripBounds = source.GlobalToLocal(globalBounds);

            Assert.AreEqual(new GlobalPosition(1300L, -700L, -150L), globalBounds.Min);
            Assert.AreEqual(new GlobalPosition(1500L, -100L, 250L), globalBounds.Max);
            AssertVector3(localBounds.min, sourceRoundTripBounds.min);
            AssertVector3(localBounds.max, sourceRoundTripBounds.max);

            Vector3 sourceLocalPosition = new Vector3(5f, 0f, -2f);
            GlobalPosition sharedGlobalPosition = source.LocalToGlobal(sourceLocalPosition);
            Vector3 destinationLocalPosition = source.ConvertLocalPositionTo(sourceLocalPosition, destination);
            Vector3 destinationExpectedPosition = destination.GlobalToLocal(sharedGlobalPosition);
            Vector3 destinationLocalDelta = source.ConvertLocalDeltaTo(localDelta, destination);
            Vector3 destinationExpectedDelta = destination.GlobalDeltaToLocalDelta(globalDelta);

            AssertVector3(destinationExpectedPosition, destinationLocalPosition);
            AssertVector3(destinationExpectedDelta, destinationLocalDelta);

            Bounds destinationBounds = source.ConvertLocalBoundsTo(localBounds, destination);
            Bounds destinationExpectedBounds = destination.GlobalToLocal(globalBounds);

            AssertVector3(destinationExpectedBounds.min, destinationBounds.min);
            AssertVector3(destinationExpectedBounds.max, destinationBounds.max);
        }

        [Test]
        public void FloatingOriginService_SetAnchorGlobalPositionRecentersWorld()
        {
            var converter = new GlobalCoordinateConverter(new GlobalPosition(1000L, 0L, 0L));
            var anchor = new StubLocalPositionProvider(new Vector3(10f, 0f, 0f));
            var worldShifter = new RecordingWorldShifter();
            var service = new FloatingOriginService(converter, anchor, worldShifter, 5f);

            Vector3 receivedOffset = Vector3.zero;
            GlobalPosition receivedOrigin = GlobalPosition.Zero;
            service.Recentered += (offset, origin) =>
            {
                receivedOffset = offset;
                receivedOrigin = origin;
            };

            service.SetAnchorGlobalPosition(new GlobalPosition(5000L, 250L, -100L));

            AssertVector3(new Vector3(-10f, 0f, 0f), worldShifter.LastShift);
            Assert.AreEqual(new GlobalPosition(5000L, 250L, -100L), converter.LocalOriginGlobal);
            AssertVector3(new Vector3(10f, 0f, 0f), receivedOffset);
            Assert.AreEqual(new GlobalPosition(5000L, 250L, -100L), receivedOrigin);
        }

        [Test]
        public void Behaviour_TeleportsAndSpawnsUsingGlobalCoordinates()
        {
            var root = new GameObject("WorldRoot");
            var player = new GameObject("Player");
            var target = new GameObject("Target");
            var prefab = new GameObject("Prefab");

            try
            {
                player.transform.SetParent(root.transform, false);
                target.transform.SetParent(root.transform, false);
                player.transform.localPosition = new Vector3(10f, 0f, 0f);

                var behaviour = player.AddComponent<PlayerCoordinateSystemBehaviour>();
                SetPrivateField(behaviour, "playerTransform", player.transform);
                SetPrivateField(behaviour, "worldRoot", root.transform);
                SetPrivateField(behaviour, "localOriginGlobal", new GlobalPosition(1000L, 0L, 0L));
                SetPrivateField(behaviour, "autoRecenter", false);
                InvokePrivateMethod(behaviour, "Awake");

                Assert.AreEqual(new GlobalPosition(2000L, 0L, 0L), behaviour.GetPlayerGlobalPosition());

                behaviour.TeleportGlobal(target.transform, new GlobalPosition(1300L, 0L, 0L), Quaternion.Euler(0f, 90f, 0f));
                AssertVector3(new Vector3(3f, 0f, 0f), target.transform.position);
                AssertQuaternion(Quaternion.Euler(0f, 90f, 0f), target.transform.rotation);
                AssertVector3(Vector3.right, behaviour.GetGlobalDirection(target.transform, Vector3.forward));
                Assert.AreEqual(new GlobalVector(100L, 0L, 0L), behaviour.GetGlobalVector(target.transform, Vector3.forward));

                behaviour.TeleportPlayerGlobal(new GlobalPosition(5000L, 0L, 0L), Quaternion.Euler(0f, 45f, 0f));
                Assert.AreEqual(new GlobalPosition(5000L, 0L, 0L), behaviour.GetPlayerGlobalPosition());
                AssertVector3(Vector3.zero, player.transform.position);
                AssertQuaternion(Quaternion.Euler(0f, 45f, 0f), player.transform.rotation);

                GameObject spawned = behaviour.SpawnAtGlobal(prefab, new GlobalPosition(5200L, 0L, 0L), Quaternion.Euler(0f, 180f, 0f));
                AssertVector3(new Vector3(2f, 0f, 0f), spawned.transform.position);
                AssertQuaternion(Quaternion.Euler(0f, 180f, 0f), spawned.transform.rotation);

                Object.DestroyImmediate(spawned);
            }
            finally
            {
                Object.DestroyImmediate(prefab);
                Object.DestroyImmediate(root);
            }
        }

        private static void AssertVector3(Vector3 expected, Vector3 actual, float tolerance = 0.0001f)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(tolerance));
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(tolerance));
            Assert.That(actual.z, Is.EqualTo(expected.z).Within(tolerance));
        }

        private static void AssertQuaternion(Quaternion expected, Quaternion actual, float toleranceDegrees = 0.001f)
        {
            Assert.That(Quaternion.Angle(expected, actual), Is.LessThanOrEqualTo(toleranceDegrees));
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, $"Field '{fieldName}' was not found.");
            field.SetValue(target, value);
        }

        private static void InvokePrivateMethod(object target, string methodName)
        {
            MethodInfo method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(method, $"Method '{methodName}' was not found.");
            method.Invoke(target, null);
        }

        private sealed class StubLocalPositionProvider : ILocalPositionProvider
        {
            public StubLocalPositionProvider(Vector3 localPosition)
            {
                LocalPosition = localPosition;
            }

            public Vector3 LocalPosition { get; }
        }

        private sealed class RecordingWorldShifter : IWorldShifter
        {
            public Vector3 LastShift { get; private set; }

            public void ShiftWorld(Vector3 worldOffset)
            {
                LastShift = worldOffset;
            }
        }
    }
}
