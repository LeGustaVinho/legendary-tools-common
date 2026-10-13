using System;
using UnityEditor;

namespace LegendaryTools.FixedPoint.Editor
{
    /// <summary>Select a frozen bounds mode before EditMode fixtures run, only when explicitly requested by the validation CLI.</summary>
    internal static class FixedBoundsValidationBootstrap
    {
        [InitializeOnLoadMethod]
        private static void InitializeRequestedMode()
        {
            const string prefix = "--fixed-bounds-test-mode=";
            foreach (string argument in Environment.GetCommandLineArgs())
            {
                if (!argument.StartsWith(prefix, StringComparison.Ordinal)) continue;
                string mode = argument.Substring(prefix.Length);
                if (mode != "min-max" && mode != "center-extents") throw new ArgumentException("Unknown bounds test mode: " + mode);
                FixedRuntime.Initialize();
                FixedBoundsRuntime.Initialize(new FixedBoundsSettings(mode == "min-max" ? FixedBoundsRepresentation.MinMax : FixedBoundsRepresentation.CenterExtents));
                return;
            }
        }
    }
}
