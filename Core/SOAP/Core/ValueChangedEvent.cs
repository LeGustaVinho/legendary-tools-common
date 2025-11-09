// ------------------------------------------------------------
// SOAP - ScriptableObject Architecture Pattern: Variables
// Version: 1.0.0
// Author: ChatGPT (framework scaffold)
// License: MIT
// ------------------------------------------------------------
//
// Drop this single file into your Unity project (e.g., Assets/SOAP/Runtime/SOAP_Variables.cs).
// It provides a lightweight, production-ready ScriptableObject Variable framework
// inspired by the "SO Architecture" pattern popularized by Ryan Hipple (GDC 2017).
//
// ✅ Features
// - Generic base variable ScriptableObject with initial/runtime values
// - Strongly-typed concrete variables for C# and Unity common types
// - Reference structs that can use a Constant or a Variable asset
// - Change notifications (UnityEvent oldValue -> newValue)
// - Reset to initial value on domain reload / play enter
// - Optional ranged numeric variables (min/max + clamping)
// - Editor-friendly CreateAssetMenu entries
//
// ⚠ Notes about serialization support in the Unity Inspector
// - Unity fully supports: bool, int, float, string, Vector2, Vector3, Color, Quaternion, byte, short, long.
// - "double" is supported on recent Unity versions. "decimal" is not serialized by Unity Inspector.
// - "char" is serialized as a single-character string in the inspector.
//
// ------------------------------------------------------------

using System;
using UnityEngine.Events;

namespace LegendaryTools.SOAP.Variables
{
    /// <summary>
    /// Invoked when a variable's value changes. Provides (oldValue, newValue).
    /// </summary>
    /// <typeparam name="T">Value type.</typeparam>
    [Serializable]
    public class ValueChangedEvent<T> : UnityEvent<T, T>
    {
    }
}