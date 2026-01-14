using System;
using System.Reflection;
using DeterministicFixedPoint;

namespace LegendaryTools.Tests.DeterministicFixedPoint
{
    internal static class DetConfigTestUtil
    {
        private static readonly FieldInfo LockedField =
            typeof(DetConfig).GetField("sLocked", BindingFlags.NonPublic | BindingFlags.Static);

        private static readonly FieldInfo ModeField =
            typeof(DetConfig).GetField("sMode", BindingFlags.NonPublic | BindingFlags.Static);

        private static readonly FieldInfo CompileFixedField =
            typeof(DetConfig).GetField("kCompileTimeFixed", BindingFlags.NonPublic | BindingFlags.Static);

        private static readonly FieldInfo CompileModeField =
            typeof(DetConfig).GetField("kCompileTimeMode", BindingFlags.NonPublic | BindingFlags.Static);

        public static bool IsCompileTimeFixed
        {
            get
            {
                if (CompileFixedField == null) return false;
                return (bool)CompileFixedField.GetRawConstantValue();
            }
        }

        public static DetOverflowMode CompileTimeMode
        {
            get
            {
                if (CompileModeField == null) return DetOverflowMode.Wrap;
                return (DetOverflowMode)CompileModeField.GetRawConstantValue();
            }
        }

        /// <summary>
        /// For unit tests only: resets lock and forces the runtime mode (only works when not compile-time fixed).
        /// </summary>
        public static void ResetAndSetMode(DetOverflowMode mode)
        {
            if (LockedField == null || ModeField == null)
                throw new InvalidOperationException("DetConfig private fields not found. Update reflection names.");

            if (IsCompileTimeFixed)
            {
                // Can only reset lock; cannot change compile-time mode.
                LockedField.SetValue(null, 0);
                return;
            }

            LockedField.SetValue(null, 0);
            ModeField.SetValue(null, mode);
        }

        public static void ResetLockOnly()
        {
            if (LockedField == null)
                throw new InvalidOperationException("DetConfig private fields not found. Update reflection names.");

            LockedField.SetValue(null, 0);
        }
    }
}