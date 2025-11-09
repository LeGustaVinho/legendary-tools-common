using System;
using UnityEngine;

namespace LegendaryTools.SOAP.SubAssets
{
    /// <summary>
    /// Marks a ScriptableObject field to be auto-created as a sub-asset of the owner asset when null.
    /// Works both via PropertyDrawer and via AssetPostprocessor.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
    public sealed class SubAssetAttribute : PropertyAttribute
    {
        /// <summary>Optional explicit child name. If empty, a readable name will be generated.</summary>
        public string ChildName { get; }

        public SubAssetAttribute(string childName = "")
        {
            ChildName = childName ?? string.Empty;
        }
    }
}