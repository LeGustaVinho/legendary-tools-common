using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace LegendaryTools.MiniCSharp
{
    /// <summary>
    /// Hosts a MiniCSharp script directly on a GameObject and forwards Unity callbacks to script functions when present.
    /// </summary>
    [AddComponentMenu("Runtime Scripting/Mini CSharp Behaviour")]
    [DisallowMultipleComponent]
    public sealed class MiniCSharpBehaviour : MonoBehaviour
    {
        [Serializable]
        public sealed class ObjectBinding
        {
            public string Name;
            public UnityEngine.Object Value;
        }

        private static readonly string[] ReservedBindingNames =
        {
            "this",
            "monoBehaviour",
            "behaviour",
            "gameObject",
            "transform"
        };

        private static readonly HashSet<string> ReservedKeywords = new HashSet<string>(StringComparer.Ordinal)
        {
            "if",
            "else",
            "try",
            "catch",
            "finally",
            "switch",
            "case",
            "default",
            "for",
            "foreach",
            "in",
            "while",
            "break",
            "continue",
            "return",
            "yield",
            "var",
            "new",
            "is",
            "as",
            "typeof",
            "true",
            "false",
            "null"
        };

        [SerializeField]
        [TextArea(10, 40)]
        private string _source;

        [SerializeField]
        private bool _disableComponentOnScriptError = true;

        [SerializeField]
        private List<ObjectBinding> _objectBindings = new List<ObjectBinding>();

        [NonSerialized]
        private MiniCSharpInterpreter _interpreter;

        [NonSerialized]
        private RuntimeScript _compiledScript;

        [NonSerialized]
        private string _compiledSource;

        [NonSerialized]
        private bool _initialized;

        [NonSerialized]
        private bool _initializationFailed;

        [NonSerialized]
        private string _lastErrorMessage;

        public string Source
        {
            get { return _source; }
            set
            {
                if (string.Equals(_source, value, StringComparison.Ordinal))
                {
                    return;
                }

                _source = value;
                InvalidateScript();
            }
        }

        public bool IsInitialized
        {
            get { return _initialized; }
        }

        public bool HasScriptFunction(string functionName)
        {
            if (!TryEnsureInitialized())
            {
                return false;
            }

            return _interpreter.HasFunction(functionName);
        }

        public object InvokeScriptFunction(string functionName, params object[] arguments)
        {
            EnsureInitializedOrThrow();
            return _interpreter.InvokeFunction(functionName, arguments);
        }

        public T InvokeScriptFunction<T>(string functionName, params object[] arguments)
        {
            EnsureInitializedOrThrow();
            return _interpreter.InvokeFunction<T>(functionName, arguments);
        }

        public T GetScriptVariable<T>(string variableName)
        {
            EnsureInitializedOrThrow();
            return _interpreter.GetVariable<T>(variableName);
        }

        public void ReloadScript()
        {
            InvalidateScript();
            TryEnsureInitialized();
        }

        private void Awake()
        {
            InvokeUnityCallback(nameof(Awake));
        }

        private void OnEnable()
        {
            InvokeUnityCallback(nameof(OnEnable));
        }

        private void Start()
        {
            InvokeUnityCallback(nameof(Start));
        }

        private void Update()
        {
            InvokeUnityCallback(nameof(Update));
        }

        private void LateUpdate()
        {
            InvokeUnityCallback(nameof(LateUpdate));
        }

        private void FixedUpdate()
        {
            InvokeUnityCallback(nameof(FixedUpdate));
        }

        private void OnDisable()
        {
            InvokeUnityCallback(nameof(OnDisable));
        }

        private void OnDestroy()
        {
            InvokeUnityCallback(nameof(OnDestroy));
        }

        private void OnGUI()
        {
            InvokeUnityCallback(nameof(OnGUI));
        }

        private void Reset()
        {
            InvokeUnityCallback(nameof(Reset));
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            InvokeUnityCallback(nameof(OnApplicationFocus), hasFocus);
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            InvokeUnityCallback(nameof(OnApplicationPause), pauseStatus);
        }

        private void OnApplicationQuit()
        {
            InvokeUnityCallback(nameof(OnApplicationQuit));
        }

        private void OnMouseDown()
        {
            InvokeUnityCallback(nameof(OnMouseDown));
        }

        private void OnMouseUp()
        {
            InvokeUnityCallback(nameof(OnMouseUp));
        }

        private void OnMouseEnter()
        {
            InvokeUnityCallback(nameof(OnMouseEnter));
        }

        private void OnMouseExit()
        {
            InvokeUnityCallback(nameof(OnMouseExit));
        }

        private void OnMouseOver()
        {
            InvokeUnityCallback(nameof(OnMouseOver));
        }

        private void OnMouseDrag()
        {
            InvokeUnityCallback(nameof(OnMouseDrag));
        }

        private void OnTriggerEnter(Collider other)
        {
            InvokeUnityCallback(nameof(OnTriggerEnter), other);
        }

        private void OnTriggerStay(Collider other)
        {
            InvokeUnityCallback(nameof(OnTriggerStay), other);
        }

        private void OnTriggerExit(Collider other)
        {
            InvokeUnityCallback(nameof(OnTriggerExit), other);
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            InvokeUnityCallback(nameof(OnTriggerEnter2D), other);
        }

        private void OnTriggerStay2D(Collider2D other)
        {
            InvokeUnityCallback(nameof(OnTriggerStay2D), other);
        }

        private void OnTriggerExit2D(Collider2D other)
        {
            InvokeUnityCallback(nameof(OnTriggerExit2D), other);
        }

        private void OnCollisionEnter(Collision collision)
        {
            InvokeUnityCallback(nameof(OnCollisionEnter), collision);
        }

        private void OnCollisionStay(Collision collision)
        {
            InvokeUnityCallback(nameof(OnCollisionStay), collision);
        }

        private void OnCollisionExit(Collision collision)
        {
            InvokeUnityCallback(nameof(OnCollisionExit), collision);
        }

        private void OnCollisionEnter2D(Collision2D collision)
        {
            InvokeUnityCallback(nameof(OnCollisionEnter2D), collision);
        }

        private void OnCollisionStay2D(Collision2D collision)
        {
            InvokeUnityCallback(nameof(OnCollisionStay2D), collision);
        }

        private void OnCollisionExit2D(Collision2D collision)
        {
            InvokeUnityCallback(nameof(OnCollisionExit2D), collision);
        }

        private void EnsureInitializedOrThrow()
        {
            if (!TryEnsureInitialized())
            {
                throw new ScriptException(_lastErrorMessage ?? "MiniCSharpBehaviour failed to initialize the script.");
            }
        }

        private bool TryEnsureInitialized()
        {
            if (_initialized)
            {
                return true;
            }

            if (_initializationFailed)
            {
                return false;
            }

            try
            {
                _interpreter = CreateInterpreter();
                RuntimeScript script = GetOrCompileScript();
                _interpreter.Execute(script);
                _initialized = true;
                _initializationFailed = false;
                _lastErrorMessage = null;
                return true;
            }
            catch (Exception exception)
            {
                _initialized = false;
                _initializationFailed = true;
                HandleScriptException("initialize script", exception);
                return false;
            }
        }

        private MiniCSharpInterpreter CreateInterpreter()
        {
            MiniCSharpInterpreter interpreter = new MiniCSharpInterpreter();
            RegisterUnityContext(interpreter);
            return interpreter;
        }

        public static void RegisterCommonUnityTypes(MiniCSharpInterpreter interpreter)
        {
            if (interpreter == null)
            {
                throw new ArgumentNullException(nameof(interpreter));
            }

            interpreter.RegisterType("Object", typeof(UnityEngine.Object));
            interpreter.RegisterType("Component", typeof(Component));
            interpreter.RegisterType("Behaviour", typeof(Behaviour));
            interpreter.RegisterType("MonoBehaviour", typeof(MonoBehaviour));
            interpreter.RegisterType("Coroutine", typeof(Coroutine));
            interpreter.RegisterType("IEnumerator", typeof(IEnumerator));
            interpreter.RegisterType("GameObject", typeof(GameObject));
            interpreter.RegisterType("Transform", typeof(Transform));
            interpreter.RegisterType("Time", typeof(Time));
            interpreter.RegisterType("Mathf", typeof(Mathf));
            interpreter.RegisterType("Debug", typeof(Debug));
            interpreter.RegisterType("Input", typeof(Input));
            interpreter.RegisterType("Vector2", typeof(Vector2));
            interpreter.RegisterType("Vector3", typeof(Vector3));
            interpreter.RegisterType("Vector4", typeof(Vector4));
            interpreter.RegisterType("Quaternion", typeof(Quaternion));
            interpreter.RegisterType("Color", typeof(Color));
            interpreter.RegisterType("Camera", typeof(Camera));
            interpreter.RegisterType("Rigidbody", typeof(Rigidbody));
            interpreter.RegisterType("Rigidbody2D", typeof(Rigidbody2D));
            interpreter.RegisterType("Collider", typeof(Collider));
            interpreter.RegisterType("Collider2D", typeof(Collider2D));
            interpreter.RegisterType("Collision", typeof(Collision));
            interpreter.RegisterType("Collision2D", typeof(Collision2D));
            interpreter.RegisterType("Animator", typeof(Animator));
            interpreter.RegisterType("WaitForSeconds", typeof(WaitForSeconds));
            interpreter.RegisterType("WaitForSecondsRealtime", typeof(WaitForSecondsRealtime));
            interpreter.RegisterType("WaitUntil", typeof(WaitUntil));
            interpreter.RegisterType("WaitWhile", typeof(WaitWhile));
            interpreter.RegisterType("WaitForEndOfFrame", typeof(WaitForEndOfFrame));
            interpreter.RegisterType("WaitForFixedUpdate", typeof(WaitForFixedUpdate));
        }

        private void RegisterUnityContext(MiniCSharpInterpreter interpreter)
        {
            RegisterCommonUnityTypes(interpreter);
            interpreter.RegisterObject("this", this);
            interpreter.RegisterObject("monoBehaviour", this);
            interpreter.RegisterObject("behaviour", this);
            interpreter.RegisterObject("gameObject", gameObject);
            interpreter.RegisterObject("transform", transform);
            RegisterInspectorObjectBindings(interpreter);
        }

        private void RegisterInspectorObjectBindings(MiniCSharpInterpreter interpreter)
        {
            if (_objectBindings == null || _objectBindings.Count == 0)
            {
                return;
            }

            HashSet<string> registeredNames = new HashSet<string>(ReservedBindingNames, StringComparer.Ordinal);

            for (int index = 0; index < _objectBindings.Count; index++)
            {
                ObjectBinding binding = _objectBindings[index];

                if (binding == null)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(binding.Name))
                {
                    continue;
                }

                if (binding.Value == null)
                {
                    continue;
                }

                string bindingName = binding.Name.Trim();

                if (!IsValidBindingIdentifier(bindingName))
                {
                    throw new ScriptException(
                        $"MiniCSharpBehaviour binding '{bindingName}' is not a valid MiniCSharp identifier.");
                }

                if (ReservedKeywords.Contains(bindingName))
                {
                    throw new ScriptException(
                        $"MiniCSharpBehaviour binding '{bindingName}' cannot use a reserved MiniCSharp keyword.");
                }

                if (!registeredNames.Add(bindingName))
                {
                    throw new ScriptException(
                        $"MiniCSharpBehaviour binding name '{bindingName}' is duplicated or reserved.");
                }

                interpreter.RegisterObject(bindingName, binding.Value);
            }
        }

        private static bool IsValidBindingIdentifier(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            if (!(char.IsLetter(value[0]) || value[0] == '_'))
            {
                return false;
            }

            for (int index = 1; index < value.Length; index++)
            {
                char character = value[index];

                if (!(char.IsLetterOrDigit(character) || character == '_'))
                {
                    return false;
                }
            }

            return true;
        }

        private RuntimeScript GetOrCompileScript()
        {
            string source = _source ?? string.Empty;

            if (_compiledScript != null && string.Equals(_compiledSource, source, StringComparison.Ordinal))
            {
                return _compiledScript;
            }

            _compiledScript = _interpreter.Compile(source);
            _compiledSource = source;
            return _compiledScript;
        }

        private void InvalidateScript()
        {
            _compiledScript = null;
            _compiledSource = null;
            _interpreter = null;
            _initialized = false;
            _initializationFailed = false;
            _lastErrorMessage = null;
        }

        private void InvokeUnityCallback(string functionName, params object[] arguments)
        {
            if (!TryEnsureInitialized())
            {
                return;
            }

            try
            {
                if (_interpreter.TryInvokeFunction(functionName, out object result, arguments) &&
                    string.Equals(functionName, nameof(Start), StringComparison.Ordinal) &&
                    result is IEnumerator coroutine)
                {
                    StartCoroutine(coroutine);
                }
            }
            catch (Exception exception)
            {
                HandleScriptException($"invoke '{functionName}'", exception);
            }
        }

        private void HandleScriptException(string operation, Exception exception)
        {
            string exceptionMessage = exception is ScriptException
                ? exception.Message
                : exception.InnerException != null
                    ? exception.InnerException.Message
                    : exception.Message;

            _lastErrorMessage = $"MiniCSharpBehaviour '{name}' failed to {operation}: {exceptionMessage}";
            Debug.LogError(_lastErrorMessage, this);

            if (_disableComponentOnScriptError && this != null)
            {
                enabled = false;
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            InvalidateScript();
        }
#endif
    }
}
