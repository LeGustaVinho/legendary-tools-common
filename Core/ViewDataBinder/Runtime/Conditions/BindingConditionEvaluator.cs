using System;
using System.Reflection;

namespace LegendaryTools.ViewBinding
{
    public static class BindingConditionEvaluator
    {
        public static bool RequiresComparisonValue(BindingConditionOperator conditionOperator)
        {
            switch (conditionOperator)
            {
                case BindingConditionOperator.LogicalNot:
                case BindingConditionOperator.IsNull:
                case BindingConditionOperator.IsNotNull:
                case BindingConditionOperator.IsTrue:
                case BindingConditionOperator.IsFalse:
                    return false;

                default:
                    return true;
            }
        }

        public static bool SupportsOperator(Type valueType, BindingConditionOperator conditionOperator)
        {
            if (valueType == null)
            {
                return false;
            }

            Type effectiveType = Nullable.GetUnderlyingType(valueType) ?? valueType;

            switch (conditionOperator)
            {
                case BindingConditionOperator.Equal:
                case BindingConditionOperator.NotEqual:
                    return SupportsEquality(effectiveType);

                case BindingConditionOperator.IsNull:
                case BindingConditionOperator.IsNotNull:
                    return true;

                case BindingConditionOperator.GreaterThan:
                case BindingConditionOperator.GreaterThanOrEqual:
                case BindingConditionOperator.LessThan:
                case BindingConditionOperator.LessThanOrEqual:
                    return IsBuiltInRelationalType(effectiveType) ||
                           HasComparisonOperator(effectiveType, conditionOperator);

                case BindingConditionOperator.LogicalAnd:
                case BindingConditionOperator.LogicalOr:
                case BindingConditionOperator.LogicalNot:
                case BindingConditionOperator.BooleanAnd:
                case BindingConditionOperator.BooleanOr:
                case BindingConditionOperator.BooleanXor:
                case BindingConditionOperator.IsTrue:
                case BindingConditionOperator.IsFalse:
                    return effectiveType == typeof(bool);

                default:
                    return false;
            }
        }

        public static bool TryEvaluate(
            BindingConditionOperator conditionOperator,
            object observedValue,
            object comparisonValue,
            Type valueType,
            out bool result,
            out string error)
        {
            result = false;

            if (valueType == null)
            {
                error = "The observed value Type is unknown.";
                return false;
            }

            if (!SupportsOperator(valueType, conditionOperator))
            {
                error = $"Operator '{GetSymbol(conditionOperator)}' is not supported by '{valueType.FullName}'.";
                return false;
            }

            try
            {
                switch (conditionOperator)
                {
                    case BindingConditionOperator.Equal:
                        return TryEquality(observedValue, comparisonValue, valueType, false, out result, out error);

                    case BindingConditionOperator.NotEqual:
                        return TryEquality(observedValue, comparisonValue, valueType, true, out result, out error);

                    case BindingConditionOperator.GreaterThan:
                        return TryCompare(observedValue, comparisonValue, valueType, conditionOperator, value => value > 0, out result, out error);

                    case BindingConditionOperator.GreaterThanOrEqual:
                        return TryCompare(observedValue, comparisonValue, valueType, conditionOperator, value => value >= 0, out result, out error);

                    case BindingConditionOperator.LessThan:
                        return TryCompare(observedValue, comparisonValue, valueType, conditionOperator, value => value < 0, out result, out error);

                    case BindingConditionOperator.LessThanOrEqual:
                        return TryCompare(observedValue, comparisonValue, valueType, conditionOperator, value => value <= 0, out result, out error);

                    case BindingConditionOperator.LogicalAnd:
                        result = (bool)observedValue && (bool)comparisonValue;
                        break;

                    case BindingConditionOperator.LogicalOr:
                        result = (bool)observedValue || (bool)comparisonValue;
                        break;

                    case BindingConditionOperator.LogicalNot:
                        result = !(bool)observedValue;
                        break;

                    case BindingConditionOperator.BooleanAnd:
                        result = (bool)observedValue & (bool)comparisonValue;
                        break;

                    case BindingConditionOperator.BooleanOr:
                        result = (bool)observedValue | (bool)comparisonValue;
                        break;

                    case BindingConditionOperator.BooleanXor:
                        result = (bool)observedValue ^ (bool)comparisonValue;
                        break;

                    case BindingConditionOperator.IsNull:
                        result = BindingValueComparer.IsUnityNull(observedValue);
                        break;

                    case BindingConditionOperator.IsNotNull:
                        result = !BindingValueComparer.IsUnityNull(observedValue);
                        break;

                    case BindingConditionOperator.IsTrue:
                        result = (bool)observedValue;
                        break;

                    case BindingConditionOperator.IsFalse:
                        result = !(bool)observedValue;
                        break;

                    default:
                        error = $"Unsupported condition operator: {conditionOperator}.";
                        return false;
                }

                error = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                error = $"Condition evaluation failed: {exception.Message}";
                return false;
            }
        }

        public static string GetSymbol(BindingConditionOperator conditionOperator)
        {
            switch (conditionOperator)
            {
                case BindingConditionOperator.Equal: return "==";
                case BindingConditionOperator.NotEqual: return "!=";
                case BindingConditionOperator.GreaterThan: return ">";
                case BindingConditionOperator.GreaterThanOrEqual: return ">=";
                case BindingConditionOperator.LessThan: return "<";
                case BindingConditionOperator.LessThanOrEqual: return "<=";
                case BindingConditionOperator.LogicalAnd: return "&&";
                case BindingConditionOperator.LogicalOr: return "||";
                case BindingConditionOperator.LogicalNot: return "!";
                case BindingConditionOperator.BooleanAnd: return "&";
                case BindingConditionOperator.BooleanOr: return "|";
                case BindingConditionOperator.BooleanXor: return "^";
                case BindingConditionOperator.IsNull: return "is null";
                case BindingConditionOperator.IsNotNull: return "is not null";
                case BindingConditionOperator.IsTrue: return "is true";
                case BindingConditionOperator.IsFalse: return "is false";
                default: return conditionOperator.ToString();
            }
        }

        private static bool TryEquality(
            object left,
            object right,
            Type valueType,
            bool negate,
            out bool result,
            out string error)
        {
            Type effectiveType = Nullable.GetUnderlyingType(valueType) ?? valueType;
            string methodName = negate ? "op_Inequality" : "op_Equality";
            MethodInfo operatorMethod = effectiveType.GetMethod(
                methodName,
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { effectiveType, effectiveType },
                null);

            if (operatorMethod != null &&
                !BindingValueComparer.IsUnityNull(left) &&
                !BindingValueComparer.IsUnityNull(right))
            {
                result = (bool)operatorMethod.Invoke(null, new[] { left, right });
                error = string.Empty;
                return true;
            }

            bool equals;
            if (typeof(UnityEngine.Object).IsAssignableFrom(effectiveType))
            {
                equals = BindingValueComparer.AreEqual(left, right);
            }
            else if (!effectiveType.IsValueType)
            {
                equals = ReferenceEquals(left, right);
            }
            else
            {
                equals = Equals(left, right);
            }

            result = negate ? !equals : equals;
            error = string.Empty;
            return true;
        }

        private static bool SupportsEquality(Type valueType)
        {
            if (!valueType.IsValueType ||
                valueType.IsPrimitive ||
                valueType.IsEnum ||
                valueType == typeof(decimal))
            {
                return true;
            }

            return valueType.GetMethod(
                       "op_Equality",
                       BindingFlags.Public | BindingFlags.Static,
                       null,
                       new[] { valueType, valueType },
                       null) != null ||
                   valueType.GetMethod(
                       "op_Inequality",
                       BindingFlags.Public | BindingFlags.Static,
                       null,
                       new[] { valueType, valueType },
                       null) != null;
        }

        private static bool IsBuiltInRelationalType(Type valueType)
        {
            return valueType == typeof(byte) ||
                   valueType == typeof(sbyte) ||
                   valueType == typeof(short) ||
                   valueType == typeof(ushort) ||
                   valueType == typeof(int) ||
                   valueType == typeof(uint) ||
                   valueType == typeof(long) ||
                   valueType == typeof(ulong) ||
                   valueType == typeof(float) ||
                   valueType == typeof(double) ||
                   valueType == typeof(decimal) ||
                   valueType == typeof(char);
        }

        private static bool TryCompare(
            object left,
            object right,
            Type valueType,
            BindingConditionOperator conditionOperator,
            Func<int, bool> comparison,
            out bool result,
            out string error)
        {
            result = false;

            if (BindingValueComparer.IsUnityNull(left) || BindingValueComparer.IsUnityNull(right))
            {
                error = $"Operator '{GetSymbol(conditionOperator)}' cannot compare null values.";
                return false;
            }

            Type effectiveType = Nullable.GetUnderlyingType(valueType) ?? valueType;
            if (IsBuiltInRelationalType(effectiveType) && left is IComparable comparable)
            {
                result = comparison(comparable.CompareTo(right));
                error = string.Empty;
                return true;
            }

            MethodInfo operatorMethod = FindComparisonOperator(effectiveType, conditionOperator);
            if (operatorMethod == null)
            {
                error = $"Type '{effectiveType.FullName}' does not provide operator '{GetSymbol(conditionOperator)}'.";
                return false;
            }

            result = (bool)operatorMethod.Invoke(null, new[] { left, right });
            error = string.Empty;
            return true;
        }

        private static bool HasComparisonOperator(Type valueType, BindingConditionOperator conditionOperator)
        {
            return FindComparisonOperator(valueType, conditionOperator) != null;
        }

        private static MethodInfo FindComparisonOperator(Type valueType, BindingConditionOperator conditionOperator)
        {
            string methodName;
            switch (conditionOperator)
            {
                case BindingConditionOperator.GreaterThan:
                    methodName = "op_GreaterThan";
                    break;
                case BindingConditionOperator.GreaterThanOrEqual:
                    methodName = "op_GreaterThanOrEqual";
                    break;
                case BindingConditionOperator.LessThan:
                    methodName = "op_LessThan";
                    break;
                case BindingConditionOperator.LessThanOrEqual:
                    methodName = "op_LessThanOrEqual";
                    break;
                default:
                    return null;
            }

            return valueType.GetMethod(
                methodName,
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { valueType, valueType },
                null);
        }
    }
}
