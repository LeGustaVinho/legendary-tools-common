using System;
using UnityEngine;

namespace LegendaryTools.AttributeSystemV2
{
    [CreateAssetMenu(menuName = "RPG/Attribute Definition", fileName = "NewAttributeDefinition")]
    public class AttributeDefinition : ScriptableObject
    {
        [SerializeField]
        [Tooltip("Unique ID for this AttributeDefinition. Auto-generated and kept unique in the project.")]
        private string _id;

        /// <summary>
        /// Unique ID for this attribute definition.
        /// </summary>
        public string Id => _id;

        [Header("Meta")] public string displayName;
        public AttributeKind kind;

        [Header("Presentation")] [Tooltip("Category name for grouping in UI/Inspector.")]
        public string categoryName;

        [Tooltip("Visibility of this attribute in UI / debug tools.")]
        public AttributeVisibility visibility = AttributeVisibility.Public;

        [Header("Clamp Behavior")] [Tooltip("Defines when min/max limits are applied.")]
        public AttributeClampMode clampMode = AttributeClampMode.ClampOnSet;

        [Header("Base Values (Editor-time)")] [Tooltip("Base value as integer, used when kind = Integer.")]
        public long baseInteger;

        [Tooltip("Base value as float, used when kind = Float.")]
        public double baseFloat;

        [Tooltip("Base value as flags bitmask, used when kind = Flags.")]
        public ulong baseFlags;

        [Header("Limits")] [Tooltip("How the minimum value is determined.")]
        public AttributeLimitMode minMode = AttributeLimitMode.None;

        [Tooltip("How the maximum value is determined.")]
        public AttributeLimitMode maxMode = AttributeLimitMode.None;

        [Tooltip("Fixed minimum value for Integer kind.")]
        public long minInteger;

        [Tooltip("Fixed maximum value for Integer kind.")]
        public long maxInteger;

        [Tooltip("Fixed minimum value for Float kind.")]
        public double minFloat;

        [Tooltip("Fixed maximum value for Float kind.")]
        public double maxFloat;

        [Tooltip("Fixed minimum flags bitmask (optional).")]
        public ulong minFlags;

        [Tooltip("Fixed maximum flags bitmask (optional).")]
        public ulong maxFlags;

        [Tooltip("Attribute used as dynamic minimum (same kind recommended).")]
        public AttributeDefinition minReference;

        [Tooltip("Attribute used as dynamic maximum (same kind recommended).")]
        public AttributeDefinition maxReference;

        [Header("Flags (Optional, for kind = Flags)")] [Tooltip("Up to 64 names, each index corresponds to a bit.")]
        public string[] flagNames = new string[0];

        // =========================================================
        // Modifier Settings
        // =========================================================

        [Header("Modifier Settings")]
        [Tooltip("If true, this attribute is treated as a modifier (buff/debuff) to another attribute.")]
        public bool isModifier;

        [Tooltip("Attribute that this modifier wants to change on the target entity.")]
        public AttributeDefinition modifierTargetAttribute;

        [Tooltip("How this modifier's value is interpreted when applied to the target attribute.")]
        public AttributeModifierValueKind modifierValueKind = AttributeModifierValueKind.Flat;

        [Tooltip("What to do if the target entity does not have the attribute this modifier wants to change.")]
        public MissingTargetAttributeBehavior missingTargetBehavior = MissingTargetAttributeBehavior.Ignore;

        [Header("Conditional (Not implemented yet)")]
        [Tooltip("Indicates that this modifier has conditions (not evaluated yet).")]
        public bool hasCondition;

        [Tooltip("Description or identifier of the condition. Evaluation is not implemented yet.")] [TextArea]
        public string conditionDescription;

        [Tooltip("How the condition is evaluated: Expression, Code or both.")]
        public AttributeModifierConditionMode conditionMode = AttributeModifierConditionMode.ExpressionOnly;

        [Tooltip("Expression used to decide if this modifier should be applied.\n" +
                 "Must evaluate to a boolean-like value (0/1, true/false, etc.).")]
        [TextArea]
        public string conditionExpression;

        [Tooltip("Optional key to use with the expression cache. If empty, conditionExpression text is used directly.")]
        public string conditionExpressionKey;

        public AttributeValue GetDefaultBaseValue()
        {
            switch (kind)
            {
                case AttributeKind.Integer:
                    return AttributeValue.FromInt(baseInteger);
                case AttributeKind.Float:
                    return AttributeValue.FromFloat(baseFloat);
                case AttributeKind.Flags:
                    return AttributeValue.FromFlags(baseFlags);
                default:
                    return AttributeValue.FromInt(0);
            }
        }

        public string GetFlagName(int bitIndex)
        {
            if (flagNames == null)
                return null;

            if (bitIndex < 0 || bitIndex >= flagNames.Length)
                return null;

            return flagNames[bitIndex];
        }

        public AttributeValue ClampForSet(AttributeValue value, IAttributeResolver resolver)
        {
            if (clampMode == AttributeClampMode.None ||
                clampMode == AttributeClampMode.ClampOnGet)
                return value;

            return ClampInternal(value, resolver);
        }

        public AttributeValue ClampForGet(AttributeValue value, IAttributeResolver resolver)
        {
            if (clampMode == AttributeClampMode.None ||
                clampMode == AttributeClampMode.ClampOnSet)
                return value;

            return ClampInternal(value, resolver);
        }

        private AttributeValue ClampInternal(AttributeValue value, IAttributeResolver resolver)
        {
            switch (kind)
            {
                case AttributeKind.Integer:
                    return ClampInt(value, resolver);
                case AttributeKind.Float:
                    return ClampFloat(value, resolver);
                case AttributeKind.Flags:
                    return value;
                default:
                    return value;
            }
        }

        private AttributeValue ClampInt(AttributeValue value, IAttributeResolver resolver)
        {
            long current = value.ToInt();
            bool hasMin = TryGetMinInt(resolver, out long min);
            bool hasMax = TryGetMaxInt(resolver, out long max);

            if (hasMin && current < min)
                current = min;

            if (hasMax && current > max)
                current = max;

            return AttributeValue.FromInt(current);
        }

        private AttributeValue ClampFloat(AttributeValue value, IAttributeResolver resolver)
        {
            double current = value.ToFloat();
            bool hasMin = TryGetMinFloat(resolver, out double min);
            bool hasMax = TryGetMaxFloat(resolver, out double max);

            if (hasMin && current < min)
                current = min;

            if (hasMax && current > max)
                current = max;

            return AttributeValue.FromFloat(current);
        }

        private bool TryGetMinInt(IAttributeResolver resolver, out long min)
        {
            switch (minMode)
            {
                case AttributeLimitMode.FixedValue:
                    min = minInteger;
                    return true;

                case AttributeLimitMode.ReferenceAttribute:
                    if (minReference != null && resolver != null)
                    {
                        AttributeInstance inst = resolver.GetAttribute(minReference);
                        if (inst != null && inst.Definition.kind == AttributeKind.Integer)
                        {
                            min = inst.Value.ToInt();
                            return true;
                        }
                    }

                    break;
            }

            min = default;
            return false;
        }

        private bool TryGetMaxInt(IAttributeResolver resolver, out long max)
        {
            switch (maxMode)
            {
                case AttributeLimitMode.FixedValue:
                    max = maxInteger;
                    return true;

                case AttributeLimitMode.ReferenceAttribute:
                    if (maxReference != null && resolver != null)
                    {
                        AttributeInstance inst = resolver.GetAttribute(maxReference);
                        if (inst != null && inst.Definition.kind == AttributeKind.Integer)
                        {
                            max = inst.Value.ToInt();
                            return true;
                        }
                    }

                    break;
            }

            max = default;
            return false;
        }

        private bool TryGetMinFloat(IAttributeResolver resolver, out double min)
        {
            switch (minMode)
            {
                case AttributeLimitMode.FixedValue:
                    min = minFloat;
                    return true;

                case AttributeLimitMode.ReferenceAttribute:
                    if (minReference != null && resolver != null)
                    {
                        AttributeInstance inst = resolver.GetAttribute(minReference);
                        if (inst != null && inst.Definition.kind == AttributeKind.Float)
                        {
                            min = inst.Value.ToFloat();
                            return true;
                        }
                    }

                    break;
            }

            min = default;
            return false;
        }

        private bool TryGetMaxFloat(IAttributeResolver resolver, out double max)
        {
            switch (maxMode)
            {
                case AttributeLimitMode.FixedValue:
                    max = maxFloat;
                    return true;

                case AttributeLimitMode.ReferenceAttribute:
                    if (maxReference != null && resolver != null)
                    {
                        AttributeInstance inst = resolver.GetAttribute(maxReference);
                        if (inst != null && inst.Definition.kind == AttributeKind.Float)
                        {
                            max = inst.Value.ToFloat();
                            return true;
                        }
                    }

                    break;
            }

            max = default;
            return false;
        }

        // =======================
        // GUID HANDLING (EDITOR)
        // =======================
#if UNITY_EDITOR
        private void OnValidate()
        {
            EnsureUniqueId();
        }

        private void Reset()
        {
            EnsureUniqueId();
        }

        private void EnsureUniqueId()
        {
            // If empty, create a new GUID.
            if (string.IsNullOrEmpty(_id))
            {
                _id = Guid.NewGuid().ToString("N");
                UnityEditor.EditorUtility.SetDirty(this);
            }

            // Check for collisions with other AttributeDefinition assets.
            string thisPath = UnityEditor.AssetDatabase.GetAssetPath(this);
            string[] guids = UnityEditor.AssetDatabase.FindAssets("t:" + nameof(AttributeDefinition));

            foreach (string assetGuid in guids)
            {
                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(assetGuid);
                if (path == thisPath)
                    continue;

                AttributeDefinition other = UnityEditor.AssetDatabase.LoadAssetAtPath<AttributeDefinition>(path);
                if (other == null)
                    continue;

                if (other._id == _id && !string.IsNullOrEmpty(_id))
                {
                    // Collision detected: regenerate this ID.
                    _id = Guid.NewGuid().ToString("N");
                    UnityEditor.EditorUtility.SetDirty(this);
                    // Extremely unlikely to collide again, so we stop here.
                    break;
                }
            }
        }
#endif
    }
}