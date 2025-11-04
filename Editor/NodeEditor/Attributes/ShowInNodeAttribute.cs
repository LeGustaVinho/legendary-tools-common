using System;
using UnityEngine;

/// <summary>
/// Marks a serialized field to be drawn inline inside a node window in the custom DAG editor.
/// </summary>
[AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
public class ShowInNodeAttribute : PropertyAttribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ShowInNodeAttribute"/> class.
    /// </summary>
    public ShowInNodeAttribute()
    {
    }
}