using System;
using UnityEngine;

namespace LegendaryTools.MiniCSharp
{
    /// <summary>
    /// Stores script source in a Unity asset and caches the compiled script at runtime.
    /// </summary>
    [CreateAssetMenu(menuName = "Runtime Scripting/Runtime Script Asset")]
    public sealed class RuntimeScriptAsset : ScriptableObject
    {
        [SerializeField]
        [TextArea(8, 40)]
        private string _source;

        [NonSerialized]
        private RuntimeScript _cachedScript;

        [NonSerialized]
        private string _cachedSource;

        /// <summary>
        /// Gets or sets the script source stored by this asset.
        /// </summary>
        public string Source
        {
            get { return _source; }
            set
            {
                if (_source == value)
                {
                    return;
                }

                _source = value;
                InvalidateCache();
            }
        }

        /// <summary>
        /// Gets a cached compiled script or compiles it when the source has changed.
        /// </summary>
        public RuntimeScript GetOrCompile(MiniCSharpInterpreter interpreter)
        {
            if (interpreter == null)
            {
                throw new ArgumentNullException(nameof(interpreter));
            }

            if (_cachedScript != null && string.Equals(_cachedSource, _source, StringComparison.Ordinal))
            {
                return _cachedScript;
            }

            _cachedScript = interpreter.Compile(_source ?? string.Empty);
            _cachedSource = _source;

            return _cachedScript;
        }

        /// <summary>
        /// Tries to validate and cache the script without throwing script syntax errors.
        /// </summary>
        public bool TryValidate(MiniCSharpInterpreter interpreter, out string errorMessage)
        {
            if (interpreter == null)
            {
                throw new ArgumentNullException(nameof(interpreter));
            }

            if (!interpreter.TryCompile(_source ?? string.Empty, out RuntimeScript script, out errorMessage))
            {
                _cachedScript = null;
                _cachedSource = null;
                return false;
            }

            _cachedScript = script;
            _cachedSource = _source;
            return true;
        }

        /// <summary>
        /// Clears the cached compiled script.
        /// </summary>
        public void InvalidateCache()
        {
            _cachedScript = null;
            _cachedSource = null;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            InvalidateCache();
        }
#endif
    }
}
