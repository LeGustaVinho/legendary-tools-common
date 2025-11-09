using System;
using LegendaryTools.SOAP.Variables.Scopes;
using UnityEngine;
using UnityEngine.Events;

namespace LegendaryTools.SOAP.Variables
{
    /// <summary>
    /// Reference that can use a constant value, a variable asset, and optional scoped overrides.
    /// Scope priority (highest to lowest): Prefab > Scene > Session > Global (Constant/Variable).
    /// </summary>
    [Serializable]
    public class SOReference<T> : SOReferenceBase
    {
        public T ConstantValue;

        public SOVariable<T> Variable;

        [Header("Scopes")]
        [Tooltip("Enable scoped resolution. When disabled, behaves like plain Constant/Variable (Global only).")]
        public bool UseScoped = false;

        [Header("Prefab Scope (highest priority)")]
        [Tooltip("If enabled, this value defined on the prefab asset overrides other scopes.")]
        public bool UsePrefabOverride = false;

        public T PrefabValue;

        [Header("Scene Scope")] [Tooltip("If enabled on a scene instance, overrides Session and Global.")]
        public bool UseSceneOverride = false;

        public T SceneValue;

        [Header("Session Scope (runtime)")]
        [Tooltip("If enabled at runtime, overrides Global when Prefab/Scene are not overriding.")]
        public bool UseSessionOverride = false;

        public T SessionValue;

        /// <summary>
        /// Gets the currently active scope for this reference.
        /// </summary>
        public VariableScope ActiveScope
        {
            get
            {
                if (UseScoped)
                {
                    if (UsePrefabOverride) return VariableScope.Prefab;
                    if (UseSceneOverride) return VariableScope.Scene;
                    if (UseSessionOverride) return VariableScope.Session;
                }

                return VariableScope.Global;
            }
        }

        /// <summary>
        /// Get/set current value respecting scope priority.
        /// </summary>
        public T Value
        {
            get
            {
                if (UseScoped)
                {
                    if (UsePrefabOverride) return PrefabValue;
                    if (UseSceneOverride) return SceneValue;
                    if (UseSessionOverride) return SessionValue;
                }

                // Global resolution (Constant or SOVariable)
                return UseConstant || Variable == null ? ConstantValue : Variable.Value;
            }
            set
            {
                if (UseScoped)
                {
                    if (UsePrefabOverride)
                    {
                        PrefabValue = value;
                        return;
                    }

                    if (UseSceneOverride)
                    {
                        SceneValue = value;
                        return;
                    }

                    if (UseSessionOverride)
                    {
                        SessionValue = value;
                        return;
                    }
                }

                // Global write
                if (UseConstant || Variable == null) ConstantValue = value;
                else Variable.Value = value;
            }
        }

        /// <summary>
        /// True if bound to a variable and not forcing constant.
        /// </summary>
        public bool HasVariable => Variable != null && !UseConstant;

        /// <summary>
        /// Subscribe to change events if bound to a variable.
        /// </summary>
        public void AddListener(UnityAction<T, T> handler)
        {
            if (HasVariable) Variable.OnValueChanged.AddListener(handler);
        }

        /// <summary>
        /// Unsubscribe from change events if bound to a variable.
        /// </summary>
        public void RemoveListener(UnityAction<T, T> handler)
        {
            if (HasVariable) Variable.OnValueChanged.RemoveListener(handler);
        }

        /// <summary>
        /// Sets a session override and enables it.
        /// </summary>
        public void SetSessionOverride(T value)
        {
            UseSessionOverride = true;
            SessionValue = value;
        }

        /// <summary>
        /// Clears the session override flag.
        /// </summary>
        public void ClearSessionOverride()
        {
            UseSessionOverride = false;
        }

        /// <summary>
        /// Clears all override flags. Falls back to Global.
        /// </summary>
        public void ClearAllOverrides()
        {
            UsePrefabOverride = false;
            UseSceneOverride = false;
            UseSessionOverride = false;
        }
    }
}