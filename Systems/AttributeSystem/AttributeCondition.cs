using System;
using System.Collections.Generic;
using UnityEngine;

namespace LegendaryTools.AttributeSystem
{
    public enum AttributeConditionOperator
    {
        AllMustBeTrue,
        AnyMustBeTrue
    }

    [Serializable]
    public class AttributeCondition
    {
        public AttributeConditionOperator Operator = AttributeConditionOperator.AllMustBeTrue;

        /// Lists all rules that must be met for the modifier to be applied to the attribute of the target entity
        public List<AttributeModifierCondition> ModApplicationConditions = new List<AttributeModifierCondition>();

        /// Checks if the system attribute has all the requirements
        public bool CanBeAppliedOn(IEntity targetEntity)
        {
            if (ModApplicationConditions.Count == 0) return true;
            foreach (AttributeModifierCondition attrModCond in ModApplicationConditions)
            {
                // 1) Try to fetch the matching attribute from the target entity.
                Attribute currentAttribute = targetEntity.GetAttributeByID(attrModCond.Attribute);
                
                // 2) If it's missing, treat that as a failure for the condition.
                if (currentAttribute == null) return false;
                
                switch (attrModCond.Operator)
                {
                    case AttributeModOperator.Equals:
                        if (!(currentAttribute.Value == attrModCond.Value))
                        {
                            if (Operator == AttributeConditionOperator.AllMustBeTrue)
                            {
                                return false;
                            }
                        }
                        else
                        {
                            if (Operator == AttributeConditionOperator.AnyMustBeTrue)
                            {
                                return true;
                            }
                        }

                        break;
                    case AttributeModOperator.Greater:
                        if (!(currentAttribute.Value > attrModCond.Value))
                        {
                            if (Operator == AttributeConditionOperator.AllMustBeTrue)
                            {
                                return false;
                            }
                        }
                        else
                        {
                            if (Operator == AttributeConditionOperator.AnyMustBeTrue)
                            {
                                return true;
                            }
                        }

                        break;
                    case AttributeModOperator.Less:
                        if (!(currentAttribute.Value < attrModCond.Value))
                        {
                            if (Operator == AttributeConditionOperator.AllMustBeTrue)
                            {
                                return false;
                            }
                        }
                        else
                        {
                            if (Operator == AttributeConditionOperator.AnyMustBeTrue)
                            {
                                return true;
                            }
                        }

                        break;
                    case AttributeModOperator.GreaterOrEquals:
                        if (!(currentAttribute.Value >= attrModCond.Value))
                        {
                            if (Operator == AttributeConditionOperator.AllMustBeTrue)
                            {
                                return false;
                            }
                        }
                        else
                        {
                            if (Operator == AttributeConditionOperator.AnyMustBeTrue)
                            {
                                return true;
                            }
                        }

                        break;
                    case AttributeModOperator.LessOrEquals:
                        if (!(currentAttribute.Value <= attrModCond.Value))
                        {
                            if (Operator == AttributeConditionOperator.AllMustBeTrue)
                            {
                                return false;
                            }
                        }
                        else
                        {
                            if (Operator == AttributeConditionOperator.AnyMustBeTrue)
                            {
                                return true;
                            }
                        }

                        break;
                    case AttributeModOperator.NotEquals:
                        if (Mathf.Approximately(currentAttribute.Value, attrModCond.Value))
                        {
                            if (Operator == AttributeConditionOperator.AllMustBeTrue)
                            {
                                return false;
                            }
                        }
                        else
                        {
                            if (Operator == AttributeConditionOperator.AnyMustBeTrue)
                            {
                                return true;
                            }
                        }

                        break;
                    case AttributeModOperator.ContainsFlag:
                        if (!FlagUtil.Has(currentAttribute.Value, attrModCond.Value))
                        {
                            if (Operator == AttributeConditionOperator.AllMustBeTrue)
                            {
                                return false;
                            }
                        }
                        else
                        {
                            if (Operator == AttributeConditionOperator.AnyMustBeTrue)
                            {
                                return true;
                            }
                        }

                        break;
                    case AttributeModOperator.NotContainsFlag:
                        if (FlagUtil.Has(currentAttribute.Value, attrModCond.Value))
                        {
                            if (Operator == AttributeConditionOperator.AllMustBeTrue)
                            {
                                return false;
                            }
                        }
                        else
                        {
                            if (Operator == AttributeConditionOperator.AnyMustBeTrue)
                            {
                                return true;
                            }
                        }

                        break;
                }
            }

            return Operator == AttributeConditionOperator.AllMustBeTrue;
        }

        public AttributeCondition Clone()
        {
            AttributeCondition clone = new AttributeCondition
            {
                Operator = Operator
            };
            foreach (AttributeModifierCondition modApplicationCondition in ModApplicationConditions)
            {
                clone.ModApplicationConditions.Add(modApplicationCondition.Clone());
            }

            return clone;
        }
    }
}