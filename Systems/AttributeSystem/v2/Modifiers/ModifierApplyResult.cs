using System;

namespace LegendaryTools.AttributeSystemV2
{
    /// <summary>
    /// Result information for a single modifier attempt.
    /// </summary>
    public readonly struct ModifierApplyResult
    {
        public readonly string ModifierName;
        public readonly string TargetAttributeName;
        public readonly bool Applied;
        public readonly string Reason;

        public ModifierApplyResult(string modifierName, string targetAttributeName, bool applied, string reason)
        {
            ModifierName = modifierName ?? "<null>";
            TargetAttributeName = targetAttributeName ?? "<null>";
            Applied = applied;
            Reason = reason ?? string.Empty;
        }

        public override string ToString()
        {
            return $"{ModifierName} -> {TargetAttributeName} | Applied={Applied} | {Reason}";
        }
    }
}