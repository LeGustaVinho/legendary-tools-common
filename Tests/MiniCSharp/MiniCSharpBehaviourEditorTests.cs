using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using LegendaryTools.MiniCSharp;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace LegendaryTools.Tests.MiniCSharp
{
    public sealed class MiniCSharpBehaviourEditorTests
    {
        private sealed class ScriptableBindingAsset : ScriptableObject
        {
            public string Label;
        }

        [Test]
        public void UnityCallbacks_WhenMappedToScriptFunctions_InvokeExpectedFunctions()
        {
            GameObject gameObject = new("MiniCSharpBehaviourCallbacks");

            try
            {
                gameObject.SetActive(false);

                MiniCSharpBehaviour behaviour = gameObject.AddComponent<MiniCSharpBehaviour>();
                behaviour.Source = @"
                    int counter = 0;

                    void Awake()
                    {
                        counter += 1;
                    }

                    void Start()
                    {
                        counter += 10;
                    }

                    void Update()
                    {
                        counter += 100;
                    }
                ";

                InvokeUnityMessage(behaviour, "Awake");
                InvokeUnityMessage(behaviour, "Start");
                InvokeUnityMessage(behaviour, "Update");

                Assert.AreEqual(111, behaviour.GetScriptVariable<int>("counter"));
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void ScriptContext_WhenUsingMonoBehaviourAliases_ExposesExpectedUnityObjects()
        {
            GameObject gameObject = new("BeforeScript");

            try
            {
                gameObject.SetActive(false);

                MiniCSharpBehaviour behaviour = gameObject.AddComponent<MiniCSharpBehaviour>();
                behaviour.Source = @"
                    bool sameGameObject = this.gameObject == gameObject;
                    bool sameTransform = this.transform == transform;

                    void Awake()
                    {
                        gameObject.name = ""AfterScript"";
                        transform.position = new UnityEngine.Vector3(1f, 2f, 3f);
                    }
                ";

                InvokeUnityMessage(behaviour, "Awake");

                Assert.IsTrue(behaviour.GetScriptVariable<bool>("sameGameObject"));
                Assert.IsTrue(behaviour.GetScriptVariable<bool>("sameTransform"));
                Assert.AreEqual("AfterScript", gameObject.name);
                Assert.AreEqual(new Vector3(1f, 2f, 3f), gameObject.transform.position);
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void InvokeScriptFunction_WhenCalledFromCSharp_UsesSameScriptState()
        {
            GameObject gameObject = new("MiniCSharpBehaviourInvoke");

            try
            {
                gameObject.SetActive(false);

                MiniCSharpBehaviour behaviour = gameObject.AddComponent<MiniCSharpBehaviour>();
                behaviour.Source = @"
                    int total = 0;

                    void AddToTotal(int value)
                    {
                        total += value;
                    }
                ";

                behaviour.InvokeScriptFunction("AddToTotal", 5);
                behaviour.InvokeScriptFunction("AddToTotal", 7);

                Assert.AreEqual(12, behaviour.GetScriptVariable<int>("total"));
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void InvokeScriptFunction_WhenUsingCoroutineAndWaitTypes_ReturnsExpectedSequence()
        {
            GameObject gameObject = new("MiniCSharpBehaviourCoroutine");

            try
            {
                gameObject.SetActive(false);

                MiniCSharpBehaviour behaviour = gameObject.AddComponent<MiniCSharpBehaviour>();
                behaviour.Source = @"
                    IEnumerator Routine()
                    {
                        yield return new WaitForSeconds(0.25f);
                        yield return transform;
                        yield break;
                    }
                ";

                IEnumerator routine = behaviour.InvokeScriptFunction<IEnumerator>("Routine");

                Assert.IsTrue(routine.MoveNext());
                Assert.IsInstanceOf<WaitForSeconds>(routine.Current);
                Assert.IsTrue(routine.MoveNext());
                Assert.AreSame(gameObject.transform, routine.Current);
                Assert.IsFalse(routine.MoveNext());
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void ScriptContext_WhenUsingInspectorBindings_ExposesRegisteredUnityObjects()
        {
            GameObject gameObject = new("MiniCSharpBehaviourBindings");
            GameObject targetObject = new("Target");
            ScriptableBindingAsset bindingAsset = ScriptableObject.CreateInstance<ScriptableBindingAsset>();

            try
            {
                gameObject.SetActive(false);
                bindingAsset.Label = "AssetFromInspector";

                MiniCSharpBehaviour behaviour = gameObject.AddComponent<MiniCSharpBehaviour>();
                SetObjectBindings(
                    behaviour,
                    ("targetObject", targetObject),
                    ("targetTransform", targetObject.transform),
                    ("bindingAsset", bindingAsset));

                behaviour.Source = @"
                    bool sameObject = targetObject == targetTransform.gameObject;
                    bool sameTransform = targetTransform == targetObject.transform;
                    string assetLabel = bindingAsset.Label;

                    void Awake()
                    {
                        targetObject.name = ""UpdatedByScript"";
                        targetTransform.position = new Vector3(4f, 5f, 6f);
                    }
                ";

                InvokeUnityMessage(behaviour, "Awake");

                Assert.IsTrue(behaviour.GetScriptVariable<bool>("sameObject"));
                Assert.IsTrue(behaviour.GetScriptVariable<bool>("sameTransform"));
                Assert.AreEqual("AssetFromInspector", behaviour.GetScriptVariable<string>("assetLabel"));
                Assert.AreEqual("UpdatedByScript", targetObject.name);
                Assert.AreEqual(new Vector3(4f, 5f, 6f), targetObject.transform.position);
            }
            finally
            {
                Object.DestroyImmediate(bindingAsset);
                Object.DestroyImmediate(targetObject);
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void ScriptContext_WhenUsingDuplicateBindingName_ThrowsMeaningfulError()
        {
            GameObject gameObject = new("MiniCSharpBehaviourDuplicateBinding");
            GameObject firstTarget = new("First");
            GameObject secondTarget = new("Second");

            try
            {
                gameObject.SetActive(false);

                MiniCSharpBehaviour behaviour = gameObject.AddComponent<MiniCSharpBehaviour>();
                SetObjectBindings(
                    behaviour,
                    ("target", firstTarget),
                    ("target", secondTarget));

                behaviour.Source = "int value = 1;";
                LogAssert.Expect(
                    LogType.Error,
                    new Regex("MiniCSharpBehaviour 'MiniCSharpBehaviourDuplicateBinding' failed to initialize script: MiniCSharpBehaviour binding name 'target' is duplicated or reserved\\."));

                ScriptException exception = Assert.Throws<ScriptException>(() => behaviour.GetScriptVariable<int>("value"));
                StringAssert.Contains("binding name 'target' is duplicated or reserved", exception.Message);
            }
            finally
            {
                Object.DestroyImmediate(secondTarget);
                Object.DestroyImmediate(firstTarget);
                Object.DestroyImmediate(gameObject);
            }
        }

        private static void InvokeUnityMessage(MiniCSharpBehaviour behaviour, string methodName, params object[] arguments)
        {
            MethodInfo method = typeof(MiniCSharpBehaviour).GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.IsNotNull(method, $"Could not find method '{methodName}' on {nameof(MiniCSharpBehaviour)}.");
            method.Invoke(behaviour, arguments);
        }

        private static void SetObjectBindings(MiniCSharpBehaviour behaviour, params (string name, Object value)[] bindings)
        {
            FieldInfo field = typeof(MiniCSharpBehaviour).GetField(
                "_objectBindings",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.IsNotNull(field, $"Could not find field '_objectBindings' on {nameof(MiniCSharpBehaviour)}.");

            List<MiniCSharpBehaviour.ObjectBinding> objectBindings = new List<MiniCSharpBehaviour.ObjectBinding>();

            foreach ((string name, Object value) binding in bindings)
            {
                objectBindings.Add(new MiniCSharpBehaviour.ObjectBinding
                {
                    Name = binding.name,
                    Value = binding.value
                });
            }

            field.SetValue(behaviour, objectBindings);
        }
    }
}
