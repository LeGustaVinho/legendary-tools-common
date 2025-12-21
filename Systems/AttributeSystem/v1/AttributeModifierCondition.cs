using System;
using System.Collections;
using UnityEditor;
using UnityEngine;
#if UNITY_EDITOR && ODIN_INSPECTOR
using Sirenix.OdinInspector;
#endif

namespace LegendaryTools.AttributeSystem
{
    [Serializable]
    public class AttributeModifierCondition
    {
        public AttributeConfig Attribute;
        public AttributeModOperator Operator;
        public float Value;
        
        public int ValueAsOptionIndex
        {
            get => (int)Value;
            set => Value = value;
        }
        
        public int ValueAsOptionFlag
        {
            get => (int)Value;
            set => Value = value;
        }
        
        public bool HasOptions => Attribute?.Data.HasOptions ?? false;
        public bool OptionsAreFlags => Attribute?.Data.OptionsAreFlags ?? false;
        public bool HasOptionsAndIsNotFlags => HasOptions && !OptionsAreFlags;
        
#if ODIN_INSPECTOR && UNITY_EDITOR
        public IEnumerable EditorOptions
        {
            get
            {
                ValueDropdownList<int> valueDropDownList = new ValueDropdownList<int>();
                if (Attribute == null) return valueDropDownList;
                if (Attribute.Data.Options == null) return valueDropDownList;
                for (int index = 0; index < Attribute.Data.Options.Length; index++)
                {
                    valueDropDownList.Add(Attribute.Data.Options[index], index);
                }

                return valueDropDownList;
            }
        }

        public string[] EditorOptionsArray
        {
            get
            {
                if (Attribute == null) return new string[2] {"None", "Everything"};
                if (Attribute.Data.Options == null) return new string[2] {"None", "Everything"};

                return Attribute.Data.Options;
            }
        }
        
        private int DrawValueAsOptionFlag(int value, GUIContent label)
        {
            if (Attribute != null && Attribute.Data.HasOptions && Attribute.Data.OptionsAreFlags)
            {
                int flagResult = label == null
                    ? EditorGUILayout.MaskField(value, EditorOptionsArray)
                    : EditorGUILayout.MaskField(label, value, EditorOptionsArray);
                return flagResult == -1 ? Attribute.Data.FlagOptionEverythingValue : flagResult;
            }

            return 0;
        }
#endif

        public AttributeModifierCondition Clone()
        {
            AttributeModifierCondition clone = new AttributeModifierCondition
            {
                Attribute = Attribute,
                Operator = Operator,
                Value = Value
            };
            return clone;
        }
    }
}