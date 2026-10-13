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
    public sealed class FixedQuaternionUnityTests
    {
        [OneTimeSetUp] public void Initialize() => FixedRuntime.Initialize();
        [Test] public void ExplicitConversionsDoNotNormalizeAndStoredPrecisionRescalesAtomically()
        {
            var value = new Quaternion(1.2345f, -2.5f, 0, 2).ToFixedQuaternion(out var status);
            Assert.AreEqual(FixedStatus.Inexact, status); Assert.AreEqual(Fixed64.FromSingle(1.2345f), value.x); Assert.AreEqual(2000, value.w.RawValue);
            Assert.AreEqual(2, value.ToUnityQuaternion().w);
            var stored = new SerializedFixedQuaternion(1234567890123456789L, Fixed64.NegativeZero.RawValue, 0, 2000000000000000000L, 18);
            Assert.IsTrue(stored.TryGetValue(out var q, out status)); Assert.AreEqual(1235, q.x.RawValue); Assert.AreEqual(2000, q.w.RawValue); Assert.AreEqual(Fixed64.NegativeZero.RawValue, q.y.RawValue);
            Assert.IsTrue(stored.TrySetDecimalPlaces(3, out status)); Assert.AreEqual(FixedStatus.Inexact, status); Assert.AreEqual(q, stored.Value);
            string before = JsonUtility.ToJson(stored); Assert.IsFalse(stored.TrySetDecimalPlaces(19, out status)); Assert.AreEqual(before, JsonUtility.ToJson(stored));
            var restored = JsonUtility.FromJson<SerializedFixedQuaternion>(before); Assert.AreEqual(stored.Value.w.RawValue, restored.Value.w.RawValue);
            Assert.AreEqual(default(FixedQuaternion), default(SerializedFixedQuaternion).Value); Assert.AreEqual(3, default(SerializedFixedQuaternion).DecimalPlaces);
        }
        [Test] public void FourthComponentAndBothDrawerFrontendsUseExactDecimalText()
        {
            var target = ScriptableObject.CreateInstance<FixedQuaternionInspectorFixture>();
            try
            {
                target.Rotation = new SerializedFixedQuaternion(0,0,0,0,18); var serialized = new SerializedObject(target); var property = serialized.FindProperty("Rotation");
                Assert.IsTrue(FixedVectorDrawer.TryEdit(property, 4, 3, "1.234567890123456789", -1, out var status)); Assert.AreEqual(FixedStatus.None, status);
                Assert.AreEqual(1234567890123456789L, property.FindPropertyRelative("_wRaw").longValue);
                Assert.IsFalse(FixedVectorDrawer.TryEdit(property, 4, 3, "invalid", -1, out _)); Assert.AreEqual(1234567890123456789L, property.FindPropertyRelative("_wRaw").longValue);
                var drawer = new FixedQuaternionDrawer(); Assert.AreEqual(4, drawer.CreatePropertyGUI(property).Query<TextField>().ToList().Count);
                Assert.Greater(drawer.GetPropertyHeight(property, new GUIContent("Rotation")), EditorGUIUtility.singleLineHeight);
            }
            finally { Object.DestroyImmediate(target); }
        }
        [Test] public void MultiObjectChangesPreserveEachDeclaredPrecisionAndSupportUndo()
        {
            var first = ScriptableObject.CreateInstance<FixedQuaternionInspectorFixture>(); var second = ScriptableObject.CreateInstance<FixedQuaternionInspectorFixture>();
            try
            {
                first.Rotation = new SerializedFixedQuaternion(0,0,0,1000,3); second.Rotation = new SerializedFixedQuaternion(0,0,0,1000000,6);
                var serialized = new SerializedObject(new Object[] { first, second }); var property = serialized.FindProperty("Rotation"); Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup();
                Assert.IsTrue(FixedVectorDrawer.TryEdit(property, 4, 3, "2.125", -1, out _)); Undo.CollapseUndoOperations(group);
                Assert.AreEqual(2125, first.Rotation.Value.w.RawValue); Assert.AreEqual(2125, second.Rotation.Value.w.RawValue); Assert.AreEqual(3, first.Rotation.DecimalPlaces); Assert.AreEqual(6, second.Rotation.DecimalPlaces);
                Undo.PerformUndo(); Assert.AreEqual(1000, first.Rotation.Value.w.RawValue); Assert.AreEqual(1000, second.Rotation.Value.w.RawValue);
            }
            finally { Object.DestroyImmediate(first); Object.DestroyImmediate(second); }
        }
        [Test] public void AssetsAndPrefabOverridesKeepAllFourRawComponents()
        {
            string name = "__FixedQuaternionTests_" + Guid.NewGuid().ToString("N"), folder = "Assets/" + name; AssetDatabase.CreateFolder("Assets", name);
            Scene temporary = EditorSceneManager.NewPreviewScene(); GameObject go = null, instance = null;
            try
            {
                var holder = ScriptableObject.CreateInstance<FixedQuaternionInspectorFixture>(); holder.Rotation = new SerializedFixedQuaternion(0, Fixed64.NegativeZero.RawValue, 0, 1234567890123456789L, 18);
                string path = folder + "/Rotation.asset"; AssetDatabase.CreateAsset(holder, path); AssetDatabase.SaveAssetIfDirty(holder); Resources.UnloadAsset(holder);
                holder = AssetDatabase.LoadAssetAtPath<FixedQuaternionInspectorFixture>(path); Assert.AreEqual(18, holder.Rotation.DecimalPlaces);
                var raw = new SerializedObject(holder).FindProperty("Rotation"); Assert.AreEqual(1234567890123456789L, raw.FindPropertyRelative("_wRaw").longValue); Assert.AreEqual(Fixed64.NegativeZero.RawValue, raw.FindPropertyRelative("_yRaw").longValue);
                go = new GameObject("FixedQuaternionPrefab"); SceneManager.MoveGameObjectToScene(go, temporary); go.AddComponent<FixedQuaternionPrefabFixture>().Value = new SerializedFixedQuaternion(0,0,0,1000,3);
                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, folder + "/Rotation.prefab"); instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, temporary);
                var component = instance.GetComponent<FixedQuaternionPrefabFixture>(); var property = new SerializedObject(component).FindProperty("Value");
                Assert.IsTrue(FixedVectorDrawer.TryEdit(property, 4, 3, "2.125", -1, out _)); Assert.AreEqual(2125, component.Value.Value.w.RawValue); Assert.IsTrue(PrefabUtility.HasPrefabInstanceAnyOverrides(instance, false));
            }
            finally { if (instance != null) Object.DestroyImmediate(instance); if (go != null) Object.DestroyImmediate(go); if (temporary.IsValid() && temporary.isLoaded) EditorSceneManager.ClosePreviewScene(temporary); AssetDatabase.DeleteAsset(folder); }
        }
    }
}
