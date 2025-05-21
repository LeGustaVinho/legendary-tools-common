using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR && ODIN_INSPECTOR
using Sirenix.OdinInspector;
#endif
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace LegendaryTools.AttributeSystem
{
    [Serializable]
    public class Attribute
    {
        public AttributeConfig Config;
        public AttributeType Type = AttributeType.Attribute;

        public float Flat;
        public float Factor;

        /// <summary>
        /// Defines how a modifier is propagated when applied to an attribute.
        /// </summary>
        public ModifierPropagation Propagation = ModifierPropagation.Parent;

        /// <summary>
        /// Allows you to force the application of the Modifier even if the Entity does not have an Attribute with the same Config.
        /// </summary>
        public bool ForceApplyIfMissing = false;

        public float CurrentValue
        {
            get => currentValue;
            private set => currentValue = value;
        }

        [SerializeField] [HideInInspector] private float currentValue;

        /// <summary>
        /// List the conditions that this modifier needs to find to be applied.
        /// </summary>
        public List<AttributeCondition> ModifierConditions = new List<AttributeCondition>();

        /// <summary>
        /// Determines how flags are modified if this Attribute uses flag options.
        /// </summary>
        public AttributeFlagModOperator FlagOperator = AttributeFlagModOperator.AddFlag;

        /// <summary>
        /// All modifiers currently applied to this Attribute.
        /// </summary>
        public List<Attribute> Modifiers = new List<Attribute>();

        public IEntity Parent { get; protected internal set; }

        public int FlatAsOptionIndex
        {
            get => (int)Flat;
            set => Flat = value;
        }

        public int FlatAsOptionFlag
        {
            get => (int)Flat;
            set => Flat = value;
        }

        /// <summary>
        /// Returns the current value of the Attribute considering its modifiers.
        /// </summary>
        public float Value => GetValueWithModifiers();

        public bool ValueAsBool => Convert.ToBoolean(Value);
        public short ValueAsShort => Convert.ToInt16(Value);
        public int ValueAsInt => Convert.ToInt32(Value);
        public long ValueAsLong => Convert.ToInt64(Value);

        public string ValueAsOption
        {
            get
            {
                if (Config == null) return string.Empty;
                if (Config.Data.Options == null) return string.Empty;
                int index = (int)GetValueWithModifiers();
                if (index >= Config.Data.Options.Length || index < 0) return string.Empty;
                return Config.Data.Options[index];
            }
        }

        public int ValueAsOptionFlag => (int)GetValueWithModifiers();

        public bool CanUseCapacity => HasCapacity && Type == AttributeType.Attribute && !HasOptions;

        public bool HasOptions => Config?.Data.HasOptions ?? false;
        public bool OptionsAreFlags => Config?.Data.OptionsAreFlags ?? false;
        public bool HasOptionsAndIsNotFlags => HasOptions && !OptionsAreFlags;
        public bool OptionsAreFlagsAndIsModifier => OptionsAreFlags && Type == AttributeType.Modifier;

#if UNITY_EDITOR && ODIN_INSPECTOR
        public IEnumerable EditorOptions
        {
            get
            {
                ValueDropdownList<int> valueDropDownList = new ValueDropdownList<int>();
                if (Config == null) return valueDropDownList;
                if (Config.Data.Options == null) return valueDropDownList;
                for (int index = 0; index < Config.Data.Options.Length; index++)
                {
                    valueDropDownList.Add(Config.Data.Options[index], index);
                }

                return valueDropDownList;
            }
        }

        public string[] EditorOptionsArray
        {
            get
            {
                if (Config == null) return new string[2] { "None", "Everything" };
                if (Config.Data.Options == null) return new string[2] { "None", "Everything" };

                return Config.Data.Options;
            }
        }
#endif
        public bool HasCapacity => Config?.Data.HasCapacity ?? false;
        public bool HasParent => Parent != null;

        public event Action<Attribute> OnAttributeModAdd;
        public event Action<Attribute> OnAttributeModRemove;
        public event Action<float, float> OnAttributeCapacityChange;
        
#if UNITY_EDITOR && ODIN_INSPECTOR
        private int DrawFlatAsOptionFlag(int value, GUIContent label)
        {
            if (Config != null && Config.Data.HasOptions && Config.Data.OptionsAreFlags)
            {
                int flagResult = label == null
                    ? EditorGUILayout.MaskField(value, EditorOptionsArray)
                    : EditorGUILayout.MaskField(label, value, EditorOptionsArray);
                return flagResult == -1 ? Config.Data.FlagOptionEverythingValue : flagResult;
            }

            return 0;
        }

        private int DrawValueAsOptionFlag(int value, GUIContent label)
        {
            if (Config != null && Config.Data.HasOptions && Config.Data.OptionsAreFlags)
            {
                GUI.enabled = false;
                if (label == null)
                {
                    EditorGUILayout.MaskField(value == -1 ? Config.Data.FlagOptionEverythingValue : value,
                        EditorOptionsArray);
                }
                else
                {
                    EditorGUILayout.MaskField(label, value == -1 ? Config.Data.FlagOptionEverythingValue : value,
                        EditorOptionsArray);
                }

                GUI.enabled = true;
            }

            return 0;
        }
#endif

        public Attribute(IEntity parent, AttributeConfig config)
        {
            Parent = parent;
            Config = config;
        }
        
        /// <summary>
        /// Should only be used in Unit Tests
        /// </summary>
        public Attribute(IEntity parent, AttributeData data, string attributeConfigName = "")
        {
            Parent = parent;
            Config = AttributeSystemFactory.CreateAttributeConfig(data);
            Config.name = attributeConfigName;
        }

        public T GetValueAsOptionEnum<T>() where T : struct, Enum, IConvertible
        {
            return ValueAsInt.GetEnumValue<T>();
        }
        
        public bool AddModifier(Attribute modifier)
        {
            if (!ModApplicationCanBeAccepted(modifier))
            {
                return false;
            }

            Modifiers ??= new List<Attribute>();
            Modifiers.Add(modifier);
            OnAttributeModAdd?.Invoke(modifier);
            return true;
        }

        public bool RemoveModifier(Attribute attribute)
        {
            Modifiers ??= new List<Attribute>();
            if (!Modifiers.Contains(attribute))
            {
                return false;
            }

            bool removed = Modifiers.Remove(attribute);
            if (removed) OnAttributeModRemove?.Invoke(attribute);
            return removed;
        }

        public void RemoveModifiers(IEntity entity)
        {
            Modifiers ??= new List<Attribute>();
            List<Attribute> modsToRemove = Modifiers.FindAll(item => item.Parent == entity);

            Modifiers.RemoveAll(item => item.Parent == entity);

            foreach (Attribute attr in modsToRemove)
            {
                OnAttributeModRemove?.Invoke(attr);
            }
        }

        /// <summary>
        /// Attempts to add <paramref name="valueToAdd"/> to this attribute's capacity.
        /// Instead of logging errors/warnings, returns an enum describing the result of the call.
        /// </summary>
        /// <param name="valueToAdd">The amount of capacity to add.</param>
        /// <returns>An <see cref="AttributeUsageStatus"/> describing the outcome.</returns>
        public AttributeUsageStatus AddUsage(float valueToAdd)
        {
            // 1) Check if capacity usage is supported by this Attribute.
            if (!CanUseCapacity)
            {
                return AttributeUsageStatus.ErrorNoCapacity;
            }

            // 2) Negative values are invalid for usage addition.
            if (valueToAdd < 0)
            {
                return AttributeUsageStatus.ErrorNegativeValue;
            }

            // 3) Calculate the new capacity.
            float newCapacity = CurrentValue + valueToAdd;
            // Default status is success, but we may override it if we need to clamp or if an error occurs.
            AttributeUsageStatus status = AttributeUsageStatus.Success;

            // 4) Check if exceeding capacity is allowed. If not, clamp to the maximum (Value).
            if (!Config.Data.AllowExceedCapacity && newCapacity > Value)
            {
                newCapacity = Value;
                status = AttributeUsageStatus.WarningClampedToMax;
            }

            // 5) If the new capacity is still below the allowed minimum, reject the operation.
            if (newCapacity < Config.Data.MinCapacity)
            {
                return AttributeUsageStatus.ErrorBelowMinimum;
            }

            // 6) All is good; apply the change and fire the event.
            float previousCapacity = CurrentValue;
            CurrentValue = newCapacity;
            OnAttributeCapacityChange?.Invoke(CurrentValue, previousCapacity);

            return status;
        }

        /// <summary>
        /// Attempts to remove <paramref name="valueToRemove"/> from this attribute's capacity.
        /// Instead of logging errors/warnings, returns an enum describing the result of the call.
        /// </summary>
        /// <param name="valueToRemove">The amount of capacity to remove.</param>
        /// <returns>An <see cref="AttributeUsageStatus"/> describing the outcome.</returns>
        public AttributeUsageStatus RemoveUsage(float valueToRemove)
        {
            // 1) Check if capacity usage is supported by this Attribute.
            if (!CanUseCapacity)
            {
                return AttributeUsageStatus.ErrorNoCapacity;
            }

            // 2) Negative values are invalid for usage removal.
            if (valueToRemove < 0)
            {
                return AttributeUsageStatus.ErrorNegativeValue;
            }

            // 3) Calculate the new capacity.
            float newCapacity = CurrentValue - valueToRemove;
            // Default status to success unless we have to clamp.
            AttributeUsageStatus status = AttributeUsageStatus.Success;

            // 4) If removing value causes capacity to fall below the minimum, clamp and mark as a warning.
            if (newCapacity < Config.Data.MinCapacity)
            {
                newCapacity = Config.Data.MinCapacity;
                status = AttributeUsageStatus.WarningClampedToMinimum;
            }

            // 5) Apply the change and fire event.
            float previousCapacity = CurrentValue;
            CurrentValue = newCapacity;
            OnAttributeCapacityChange?.Invoke(CurrentValue, previousCapacity);

            return status;
        }

        /// <summary>
        /// Checks whether the provided <paramref name="attributeModifier"/> can be applied on this Attribute's Parent (IEntity).
        /// </summary>
        public bool ModApplicationCanBeAccepted(Attribute attributeModifier)
        {
            if (attributeModifier == null)
            {
                return false;
            }

            foreach (AttributeCondition modifierCondition in attributeModifier.ModifierConditions)
            {
                if (!modifierCondition.CanBeAppliedOn(Parent))
                {
                    return false;
                }
            }

            return true;
        }

        public Attribute Clone(IEntity parent)
        {
            Attribute clone = new Attribute(parent ?? Parent, Config)
            {
                CurrentValue = CurrentValue,
                Factor = Factor,
                Flat = Flat,
                FlagOperator = FlagOperator,
                Type = Type,
                ForceApplyIfMissing = ForceApplyIfMissing
            };

            foreach (AttributeCondition targetAttributeModifier in ModifierConditions)
            {
                clone.ModifierConditions.Add(targetAttributeModifier.Clone());
            }

            return clone;
        }

        /// <summary>
        /// Calculates the current value of the attribute, incorporating all modifiers.
        /// </summary>
        /// <remarks>
        ///     The method accounts for flat and factor values of modifiers, options, flag operations, and configuration
        ///     constraints.
        ///     - **Modifiers:** Applied recursively and sorted by their factor values in descending order.
        ///     - **Flag Operations:** If the attribute uses flags (bitwise operations), modifiers are processed accordingly.
        ///     - **Stack Penalties:** If the configuration defines penalties for stacking modifiers, these are applied per
        ///     modifier level.
        ///     - **Min-Max Clamping:** Ensures the result respects the min and max limits defined in the configuration.
        /// </remarks>
        /// <returns>The calculated attribute value after applying all modifiers.</returns>
        private float GetValueWithModifiers()
        {
            if (Config == null)
            {
                // If no configuration is provided, return the default value of 0.
                return 0;
            }

            // Collect all modifiers recursively from the attribute and its children modifiers, this is necessary because a modifier can have another modifier
            List<Attribute> allRecursiveModifiers = new List<Attribute>();
            GetModifiersRecursive(allRecursiveModifiers);

            if (HasOptions)
            {
                // Handle flag-based attributes using bitwise operations.
                float currentFlag = Flat;

                foreach (Attribute modifier in allRecursiveModifiers)
                {
                    if (modifier.OptionsAreFlags)
                    {
                        // Apply bitwise operations based on the modifier's flag operator.
                        switch (modifier.FlagOperator)
                        {
                            case AttributeFlagModOperator.AddFlag when Config.Data.OptionsAreFlags:
                                currentFlag = FlagUtil.Add(currentFlag, modifier.Flat);
                                break;
                            case AttributeFlagModOperator.RemoveFlag when Config.Data.OptionsAreFlags:
                                currentFlag = FlagUtil.Remove(currentFlag, modifier.Flat);
                                break;
                            case AttributeFlagModOperator.Set:
                                currentFlag = modifier.Flat;
                                break;
                        }
                    }
                    else
                    {
                        // For non-flag options, override the current flag value with the modifier's flat value.
                        currentFlag = modifier.Flat;
                    }
                }

                return currentFlag;
            }

            // Sort modifiers by factor in descending order so that big factor values are applied first.
            allRecursiveModifiers.Sort((a, b) => -1 * a.Factor.CompareTo(b.Factor));

            float totalFlat = 0;
            float totalFactor = 0;

            for (int i = 0; i < allRecursiveModifiers.Count; i++)
            {
                totalFlat += allRecursiveModifiers[i].Flat;

                if (Config.Data.HasStackPenault)
                {
                    // Apply stack penalties to the factor value, if configured.
                    totalFactor += allRecursiveModifiers[i].Factor *
                                   Config.Data.StackPenaults[
                                       Mathf.Clamp(i, 0, Config.Data.StackPenaults.Length - 1)];
                }
                else
                {
                    totalFactor += allRecursiveModifiers[i].Factor;
                }
            }

            float finalValue = (Flat + totalFlat) * (1 + Factor + totalFactor);

            // Clamp the result within the min and max range if configured.
            return Config.Data.HasMinMax
                ? Mathf.Clamp(finalValue, Config.Data.MinMaxValue.x, Config.Data.MinMaxValue.y)
                : finalValue;
        }

        /// <summary>
        /// Gathers all modifiers recursively into <paramref name="allModifiers"/>.
        /// </summary>
        private void GetModifiersRecursive(List<Attribute> allModifiers)
        {
            if (Modifiers.Count > 0)
            {
                allModifiers.AddRange(Modifiers);

                foreach (Attribute modifier in Modifiers)
                {
                    modifier.GetModifiersRecursive(allModifiers);
                }
            }
        }
    }
}