using System;
using LegendaryTools.FixedPoint.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace LegendaryTools.FixedPoint.Tests
{
    public sealed class FixedVectorUnityTests
    {
        [OneTimeSetUp] public void Initialize() => FixedRuntime.Initialize();
        [Test] public void FloatConversionsAreExplicitAndExposeInputQuantization()
        {
            var value = new Vector3(1.2345f, -2.5f, 3f).ToFixedVector3(out FixedStatus status);
            Assert.AreEqual(FixedStatus.Inexact, status); Assert.AreEqual(-2500, value.y.RawValue);
            Assert.AreEqual(Fixed64.FromSingle(1.2345f).RawValue, value.x.RawValue);
            Vector3 unity = value.ToUnityVector3(); Assert.AreEqual(-2.5f, unity.y);
            Assert.AreEqual(new FixedVector2(1, 2), new Vector2(1, 2).ToFixedVector2());
        }
        [Test] public void StoredPrecisionIsPreservedAndReconciledWithRuntimeSettings()
        {
            var serialized = new SerializedFixedVector3(1234567890123456789L, -1250000000000000000L, 0, 18);
            Assert.IsTrue(serialized.TryGetValue(out FixedVector3 value, out FixedStatus status));
            Assert.AreEqual(FixedStatus.Inexact, status); Assert.AreEqual(1235, value.x.RawValue); Assert.AreEqual(-1250, value.y.RawValue);
            Assert.AreEqual(18, serialized.DecimalPlaces);
            Assert.IsTrue(serialized.TrySetDecimalPlaces(3, out status)); Assert.AreEqual(FixedStatus.Inexact, status);
            Assert.AreEqual(value, serialized.Value);
            Assert.IsFalse(serialized.TrySetDecimalPlaces(19, out status)); Assert.AreEqual(3, serialized.DecimalPlaces);
            Assert.AreEqual(FixedStatus.InvalidConfiguration, status);
            Assert.AreEqual(3, default(SerializedFixedVector2).DecimalPlaces); Assert.AreEqual(FixedVector2.zero, default(SerializedFixedVector2).Value);
        }
        [Test] public void BothDrawersParseDecimalTextWithoutRoundingThroughDouble()
        {
            var target = ScriptableObject.CreateInstance<FixedVectorInspectorFixture>();
            try
            {
                target.Vector3 = new SerializedFixedVector3(0, 0, 0, 18);
                var serialized = new SerializedObject(target); var property = serialized.FindProperty("Vector3");
                Assert.IsTrue(FixedVectorDrawer.TryEdit(property, 3, 0, "1.234567890123456789", -1, out FixedStatus status));
                Assert.AreEqual(FixedStatus.None, status); Assert.AreEqual(1234567890123456789L, property.FindPropertyRelative("_xRaw").longValue);
                Assert.IsFalse(FixedVectorDrawer.TryEdit(property, 3, 0, "invalid", -1, out _));
                Assert.AreEqual(1234567890123456789L, property.FindPropertyRelative("_xRaw").longValue);
                Assert.IsTrue(FixedVectorDrawer.TryEdit(property, 3, 1, "-0", -1, out _));
                Assert.AreEqual(Fixed64.NegativeZero.RawValue, property.FindPropertyRelative("_yRaw").longValue);
                var drawer = new FixedVector3Drawer(); VisualElement ui = drawer.CreatePropertyGUI(property);
                Assert.AreEqual(3, ui.Query<TextField>().ToList().Count);
                Assert.Greater(drawer.GetPropertyHeight(property, new GUIContent("Vector")), EditorGUIUtility.singleLineHeight);
            }
            finally { Object.DestroyImmediate(target); }
        }
        [Test] public void MultiObjectEditingUsesEachTargetsDeclaredPrecisionAndSupportsUndo()
        {
            var first = ScriptableObject.CreateInstance<FixedVectorInspectorFixture>();
            var second = ScriptableObject.CreateInstance<FixedVectorInspectorFixture>();
            try
            {
                first.Vector2 = new SerializedFixedVector2(1000, 0, 3); second.Vector2 = new SerializedFixedVector2(1000000, 0, 6);
                var serialized = new SerializedObject(new Object[] { first, second }); var property = serialized.FindProperty("Vector2");
                Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup();
                Assert.IsTrue(FixedVectorDrawer.TryEdit(property, 2, 0, "2.125", -1, out _)); Undo.CollapseUndoOperations(group);
                Assert.AreEqual(2125, first.Vector2.Value.x.RawValue); Assert.AreEqual(2125, second.Vector2.Value.x.RawValue);
                Assert.AreEqual(3, first.Vector2.DecimalPlaces); Assert.AreEqual(6, second.Vector2.DecimalPlaces);
                Undo.PerformUndo(); Assert.AreEqual(1000, first.Vector2.Value.x.RawValue); Assert.AreEqual(1000, second.Vector2.Value.x.RawValue);
            }
            finally { Object.DestroyImmediate(first); Object.DestroyImmediate(second); }
        }
        [Test] public void AssetsAndPrefabOverridesPersistRawCoordinatesAndPrecision()
        {
            string name = "__FixedVectorTests_" + Guid.NewGuid().ToString("N"), folder = "Assets/" + name;
            AssetDatabase.CreateFolder("Assets", name);
            Scene temporary = EditorSceneManager.NewPreviewScene();
            GameObject go = null, instance = null;
            try
            {
                var holder = ScriptableObject.CreateInstance<FixedVectorInspectorFixture>();
                holder.Vector3 = new SerializedFixedVector3(1234567890123456789L, Fixed64.NegativeZero.RawValue, 0, 18);
                string path = folder + "/Vector.asset"; AssetDatabase.CreateAsset(holder, path); AssetDatabase.SaveAssetIfDirty(holder);
                Resources.UnloadAsset(holder); holder = AssetDatabase.LoadAssetAtPath<FixedVectorInspectorFixture>(path);
                var stored = new SerializedObject(holder).FindProperty("Vector3");
                Assert.AreEqual(1234567890123456789L, stored.FindPropertyRelative("_xRaw").longValue); Assert.AreEqual(18, holder.Vector3.DecimalPlaces);
                go = new GameObject("FixedVectorPrefab"); SceneManager.MoveGameObjectToScene(go, temporary);
                go.AddComponent<FixedVectorPrefabFixture>().Value = new SerializedFixedVector3(1000, 2000, 3000, 3);
                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, folder + "/Vector.prefab");
                instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, temporary);
                var component = instance.GetComponent<FixedVectorPrefabFixture>();
                var serialized = new SerializedObject(component); var property = serialized.FindProperty("Value");
                Assert.IsTrue(FixedVectorDrawer.TryEdit(property, 3, 0, "4.125", -1, out _));
                Assert.AreEqual(4125, component.Value.Value.x.RawValue);
                Assert.IsTrue(PrefabUtility.HasPrefabInstanceAnyOverrides(instance, false));
            }
            finally
            {
                if (instance != null) Object.DestroyImmediate(instance); if (go != null) Object.DestroyImmediate(go);
                if (temporary.IsValid() && temporary.isLoaded) EditorSceneManager.ClosePreviewScene(temporary);
                AssetDatabase.DeleteAsset(folder);
            }
        }
    }
}
