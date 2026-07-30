using System;
using System.Collections.Generic;
using System.Reflection;
#if !ENABLE_IL2CPP
using System.Linq.Expressions;
#endif
using UnityEngine;

namespace LegendaryTools.ViewBinding
{
    public static class EventBindingConditionEvaluator
    {
        private static readonly Dictionary<string, MethodInfo> OperatorMethodCache =
            new Dictionary<string, MethodInfo>(StringComparer.Ordinal);
        private static readonly object OperatorMethodCacheLock = new object();
        private static readonly Dictionary<string, Func<object, bool>> UnaryInvokerCache =
            new Dictionary<string, Func<object, bool>>(StringComparer.Ordinal);
        private static readonly Dictionary<string, Func<object, object, bool>> BinaryInvokerCache =
            new Dictionary<string, Func<object, object, bool>>(StringComparer.Ordinal);
        [ThreadStatic] private static object[] unaryArguments;
        [ThreadStatic] private static object[] binaryArguments;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRuntimeCaches()
        {
            lock (OperatorMethodCacheLock)
            {
                OperatorMethodCache.Clear();
                UnaryInvokerCache.Clear();
                BinaryInvokerCache.Clear();
            }

            unaryArguments = null;
            binaryArguments = null;
        }

        public static bool TryEvaluate(
            EventBindingCondition condition,
            IReadOnlyList<object> values,
            IReadOnlyList<BindingMemberMetadata> metadata,
            out bool result,
            out string error)
        {
#if UNITY_2020_2_OR_NEWER
            using (BindingRuntimeProfiler.EvaluateConditions.Auto())
#endif
            {
                result = false;
                error = string.Empty;

                if (condition == null)
                {
                    error = "Condition is null.";
                    return false;
                }

                if (condition.Clauses == null || condition.Clauses.Count == 0)
                {
                    error = "Condition requires at least one clause.";
                    return false;
                }

                bool andValue = false;
                bool xorValue = false;
                bool orValue = false;

                for (int i = 0; i < condition.Clauses.Count; i++)
                {
                    EventBindingConditionClause clause = condition.Clauses[i];
                    if (clause == null)
                    {
                        error = $"Clause {i + 1} is null.";
                        return false;
                    }

                    if (clause.SourceIndex < 0 || clause.SourceIndex >= values.Count || clause.SourceIndex >= metadata.Count)
                    {
                        error = $"Clause {i + 1} references invalid Source index {clause.SourceIndex}.";
                        return false;
                    }

                    Type valueType = metadata[clause.SourceIndex].ValueType;
                    object comparisonValue = null;

                    if (RequiresComparisonValue(clause.ComparisonOperator))
                    {
                        if (!clause.TryGetComparisonValue(
                                valueType,
                                out comparisonValue,
                                out error))
                        {
                            error = $"Clause {i + 1}: {error}";
                            return false;
                        }
                    }

                    if (!TryCompare(
                            values[clause.SourceIndex],
                            comparisonValue,
                            valueType,
                            clause.ComparisonOperator,
                            out bool clauseResult,
                            out error))
                    {
                        error = $"Clause {i + 1}: {error}";
                        return false;
                    }

                    bool effectiveClauseResult = clause.Negate ? !clauseResult : clauseResult;

                    if (i == 0)
                    {
                        andValue = effectiveClauseResult;
                        continue;
                    }

                    switch (clause.LogicalOperator)
                    {
                        case EventBindingLogicalOperator.And:
                            andValue = andValue && effectiveClauseResult;
                            break;

                        case EventBindingLogicalOperator.Xor:
                            xorValue ^= andValue;
                            andValue = effectiveClauseResult;
                            break;

                        case EventBindingLogicalOperator.Or:
                            xorValue ^= andValue;
                            orValue |= xorValue;
                            xorValue = false;
                            andValue = effectiveClauseResult;
                            break;

                        default:
                            error = $"Unsupported logical operator: {clause.LogicalOperator}.";
                            return false;
                    }
                }

                xorValue ^= andValue;
                orValue |= xorValue;
                result = orValue;
                return true;

            }
        }

        public static bool RequiresComparisonValue(EventBindingComparisonOperator comparisonOperator)
        {
            switch (comparisonOperator)
            {
                case EventBindingComparisonOperator.IsNull:
                case EventBindingComparisonOperator.IsNotNull:
                case EventBindingComparisonOperator.IsTrue:
                case EventBindingComparisonOperator.IsFalse:
                    return false;

                default:
                    return true;
            }
        }

        public static bool IsOperatorSupported(
            Type valueType,
            EventBindingComparisonOperator comparisonOperator)
        {
            if (valueType == null)
            {
                return false;
            }

            Type effectiveType = Nullable.GetUnderlyingType(valueType) ?? valueType;

            switch (comparisonOperator)
            {
                case EventBindingComparisonOperator.IsNull:
                case EventBindingComparisonOperator.IsNotNull:
                    return !valueType.IsValueType || Nullable.GetUnderlyingType(valueType) != null;

                case EventBindingComparisonOperator.IsTrue:
                    return effectiveType == typeof(bool) ||
                           FindUnaryBooleanOperator(effectiveType, "op_True") != null;

                case EventBindingComparisonOperator.IsFalse:
                    return effectiveType == typeof(bool) ||
                           FindUnaryBooleanOperator(effectiveType, "op_False") != null;

                case EventBindingComparisonOperator.Equal:
                case EventBindingComparisonOperator.NotEqual:
                    return SupportsEquality(effectiveType, comparisonOperator);

                case EventBindingComparisonOperator.GreaterThan:
                case EventBindingComparisonOperator.GreaterThanOrEqual:
                case EventBindingComparisonOperator.LessThan:
                case EventBindingComparisonOperator.LessThanOrEqual:
                    return SupportsRelationalComparison(effectiveType, comparisonOperator);

                default:
                    return false;
            }
        }

        private static bool TryCompare(
            object left,
            object right,
            Type valueType,
            EventBindingComparisonOperator comparisonOperator,
            out bool result,
            out string error)
        {
            result = false;
            error = string.Empty;

            bool leftIsNull = IsNullValue(left);
            bool rightIsNull = IsNullValue(right);

            switch (comparisonOperator)
            {
                case EventBindingComparisonOperator.IsNull:
                    result = leftIsNull;
                    return true;

                case EventBindingComparisonOperator.IsNotNull:
                    result = !leftIsNull;
                    return true;

                case EventBindingComparisonOperator.IsTrue:
                    if (leftIsNull)
                    {
                        result = false;
                        return true;
                    }

                    if (left is bool boolValue)
                    {
                        result = boolValue;
                        return true;
                    }

                    return TryInvokeUnaryBooleanOperator(
                        "op_True",
                        left,
                        valueType,
                        out result,
                        out error);

                case EventBindingComparisonOperator.IsFalse:
                    if (leftIsNull)
                    {
                        result = false;
                        return true;
                    }

                    if (left is bool falseValue)
                    {
                        result = !falseValue;
                        return true;
                    }

                    return TryInvokeUnaryBooleanOperator(
                        "op_False",
                        left,
                        valueType,
                        out result,
                        out error);

                case EventBindingComparisonOperator.Equal:
                    return TryEquality(left, right, valueType, false, out result, out error);

                case EventBindingComparisonOperator.NotEqual:
                    return TryEquality(left, right, valueType, true, out result, out error);
            }

            if (leftIsNull || rightIsNull)
            {
                result = false;
                return true;
            }

            Type operatorType = Nullable.GetUnderlyingType(valueType) ?? valueType;
            string operatorMethodName = GetOperatorMethodName(comparisonOperator);
            if (TryInvokeBinaryOperator(operatorMethodName, left, right, operatorType, out result, out error))
            {
                return true;
            }

            if (TryBuiltInRelationalComparison(
                    left,
                    right,
                    valueType,
                    comparisonOperator,
                    out result))
            {
                error = string.Empty;
                return true;
            }

            error = $"Type '{valueType.FullName}' does not define C# operator '{GetOperatorSymbol(comparisonOperator)}'.";
            return false;
        }

        private static bool TryEquality(
            object left,
            object right,
            Type valueType,
            bool invert,
            out bool result,
            out string error)
        {
            Type effectiveType = Nullable.GetUnderlyingType(valueType) ?? valueType;

            if (typeof(UnityEngine.Object).IsAssignableFrom(effectiveType))
            {
                UnityEngine.Object leftObject = left as UnityEngine.Object;
                UnityEngine.Object rightObject = right as UnityEngine.Object;
                result = leftObject == rightObject;
                if (invert)
                {
                    result = !result;
                }

                error = string.Empty;
                return true;
            }

            if (typeof(Delegate).IsAssignableFrom(effectiveType))
            {
                result = Equals(left, right);
                if (invert)
                {
                    result = !result;
                }

                error = string.Empty;
                return true;
            }

            string methodName = invert ? "op_Inequality" : "op_Equality";
            if (TryInvokeBinaryOperator(methodName, left, right, effectiveType, out result, out error))
            {
                return true;
            }

            bool leftIsNull = IsNullValue(left);
            bool rightIsNull = IsNullValue(right);
            if (leftIsNull || rightIsNull)
            {
                result = leftIsNull && rightIsNull;
                if (invert)
                {
                    result = !result;
                }

                error = string.Empty;
                return true;
            }

            if (TryBuiltInEquality(left, right, effectiveType, out result))
            {
                if (invert)
                {
                    result = !result;
                }

                error = string.Empty;
                return true;
            }

            if (!effectiveType.IsValueType)
            {
                result = ReferenceEquals(left, right);
                if (invert)
                {
                    result = !result;
                }

                error = string.Empty;
                return true;
            }

            error = $"Type '{effectiveType.FullName}' does not define C# operator '{(invert ? "!=" : "==")}'.";
            return false;
        }

        private static bool TryBuiltInEquality(
            object left,
            object right,
            Type valueType,
            out bool result)
        {
            result = false;

            if (valueType.IsEnum)
            {
                result = Equals(left, right);
                return true;
            }

            switch (Type.GetTypeCode(valueType))
            {
                case TypeCode.Boolean:
                    result = (bool)left == (bool)right;
                    return true;
                case TypeCode.Char:
                    result = (char)left == (char)right;
                    return true;
                case TypeCode.SByte:
                    result = (sbyte)left == (sbyte)right;
                    return true;
                case TypeCode.Byte:
                    result = (byte)left == (byte)right;
                    return true;
                case TypeCode.Int16:
                    result = (short)left == (short)right;
                    return true;
                case TypeCode.UInt16:
                    result = (ushort)left == (ushort)right;
                    return true;
                case TypeCode.Int32:
                    result = (int)left == (int)right;
                    return true;
                case TypeCode.UInt32:
                    result = (uint)left == (uint)right;
                    return true;
                case TypeCode.Int64:
                    result = (long)left == (long)right;
                    return true;
                case TypeCode.UInt64:
                    result = (ulong)left == (ulong)right;
                    return true;
                case TypeCode.Single:
                    result = (float)left == (float)right;
                    return true;
                case TypeCode.Double:
                    result = (double)left == (double)right;
                    return true;
                case TypeCode.Decimal:
                    result = (decimal)left == (decimal)right;
                    return true;
                case TypeCode.String:
                    result = (string)left == (string)right;
                    return true;
                default:
                    return false;
            }
        }

        private static bool TryBuiltInRelationalComparison(
            object left,
            object right,
            Type valueType,
            EventBindingComparisonOperator comparisonOperator,
            out bool result)
        {
            Type effectiveType = Nullable.GetUnderlyingType(valueType) ?? valueType;
            result = false;

            if (effectiveType.IsEnum)
            {
                return false;
            }

            switch (Type.GetTypeCode(effectiveType))
            {
                case TypeCode.Char:
                    return Compare((char)left, (char)right, comparisonOperator, out result);
                case TypeCode.SByte:
                    return Compare((sbyte)left, (sbyte)right, comparisonOperator, out result);
                case TypeCode.Byte:
                    return Compare((byte)left, (byte)right, comparisonOperator, out result);
                case TypeCode.Int16:
                    return Compare((short)left, (short)right, comparisonOperator, out result);
                case TypeCode.UInt16:
                    return Compare((ushort)left, (ushort)right, comparisonOperator, out result);
                case TypeCode.Int32:
                    return Compare((int)left, (int)right, comparisonOperator, out result);
                case TypeCode.UInt32:
                    return Compare((uint)left, (uint)right, comparisonOperator, out result);
                case TypeCode.Int64:
                    return Compare((long)left, (long)right, comparisonOperator, out result);
                case TypeCode.UInt64:
                    return Compare((ulong)left, (ulong)right, comparisonOperator, out result);
                case TypeCode.Single:
                    return CompareSingle((float)left, (float)right, comparisonOperator, out result);
                case TypeCode.Double:
                    return CompareDouble((double)left, (double)right, comparisonOperator, out result);
                case TypeCode.Decimal:
                    return Compare((decimal)left, (decimal)right, comparisonOperator, out result);
                default:
                    return false;
            }
        }

        private static bool CompareSingle(
            float left,
            float right,
            EventBindingComparisonOperator comparisonOperator,
            out bool result)
        {
            switch (comparisonOperator)
            {
                case EventBindingComparisonOperator.GreaterThan:
                    result = left > right;
                    return true;
                case EventBindingComparisonOperator.GreaterThanOrEqual:
                    result = left >= right;
                    return true;
                case EventBindingComparisonOperator.LessThan:
                    result = left < right;
                    return true;
                case EventBindingComparisonOperator.LessThanOrEqual:
                    result = left <= right;
                    return true;
                default:
                    result = false;
                    return false;
            }
        }

        private static bool CompareDouble(
            double left,
            double right,
            EventBindingComparisonOperator comparisonOperator,
            out bool result)
        {
            switch (comparisonOperator)
            {
                case EventBindingComparisonOperator.GreaterThan:
                    result = left > right;
                    return true;
                case EventBindingComparisonOperator.GreaterThanOrEqual:
                    result = left >= right;
                    return true;
                case EventBindingComparisonOperator.LessThan:
                    result = left < right;
                    return true;
                case EventBindingComparisonOperator.LessThanOrEqual:
                    result = left <= right;
                    return true;
                default:
                    result = false;
                    return false;
            }
        }

        private static bool Compare<T>(
            T left,
            T right,
            EventBindingComparisonOperator comparisonOperator,
            out bool result)
            where T : IComparable<T>
        {
            int comparison = left.CompareTo(right);

            switch (comparisonOperator)
            {
                case EventBindingComparisonOperator.GreaterThan:
                    result = comparison > 0;
                    return true;
                case EventBindingComparisonOperator.GreaterThanOrEqual:
                    result = comparison >= 0;
                    return true;
                case EventBindingComparisonOperator.LessThan:
                    result = comparison < 0;
                    return true;
                case EventBindingComparisonOperator.LessThanOrEqual:
                    result = comparison <= 0;
                    return true;
                default:
                    result = false;
                    return false;
            }
        }

        private static bool TryInvokeUnaryBooleanOperator(
            string methodName,
            object value,
            Type valueType,
            out bool result,
            out string error)
        {
            result = false;
            Type effectiveType = Nullable.GetUnderlyingType(valueType) ?? valueType;
            MethodInfo method = FindUnaryBooleanOperator(effectiveType, methodName);
            if (method == null)
            {
                error = $"Type '{effectiveType.FullName}' does not define C# operator '{methodName}'.";
                return false;
            }

            try
            {
                Func<object, bool> invoker = GetUnaryInvoker(effectiveType, methodName, method);
                if (invoker != null)
                {
                    result = invoker(value);
                    error = string.Empty;
                    return true;
                }

                if (unaryArguments == null)
                {
                    unaryArguments = new object[1];
                }

                unaryArguments[0] = value;
                result = (bool)method.Invoke(null, unaryArguments);
                unaryArguments[0] = null;
                error = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                if (unaryArguments != null)
                {
                    unaryArguments[0] = null;
                }

                error = $"Operator execution failed: {exception.GetBaseException().Message}";
                return false;
            }
        }

        private static MethodInfo FindUnaryBooleanOperator(Type valueType, string methodName)
        {
            if (valueType == null || string.IsNullOrEmpty(methodName))
            {
                return null;
            }

            string cacheKey = valueType.AssemblyQualifiedName + "|1|" + methodName;
            lock (OperatorMethodCacheLock)
            {
                if (OperatorMethodCache.TryGetValue(cacheKey, out MethodInfo cachedMethod))
                {
                    return cachedMethod;
                }
            }

            MethodInfo method = valueType.GetMethod(
                methodName,
                BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy,
                null,
                new[] { valueType },
                null);
            if (method != null && method.ReturnType != typeof(bool))
            {
                method = null;
            }

            lock (OperatorMethodCacheLock)
            {
                OperatorMethodCache[cacheKey] = method;
            }

            return method;
        }

        private static bool SupportsEquality(
            Type valueType,
            EventBindingComparisonOperator comparisonOperator)
        {
            if (typeof(UnityEngine.Object).IsAssignableFrom(valueType) ||
                valueType.IsEnum ||
                !valueType.IsValueType)
            {
                return true;
            }

            TypeCode typeCode = Type.GetTypeCode(valueType);
            if (typeCode == TypeCode.Boolean ||
                typeCode == TypeCode.Char ||
                typeCode == TypeCode.SByte ||
                typeCode == TypeCode.Byte ||
                typeCode == TypeCode.Int16 ||
                typeCode == TypeCode.UInt16 ||
                typeCode == TypeCode.Int32 ||
                typeCode == TypeCode.UInt32 ||
                typeCode == TypeCode.Int64 ||
                typeCode == TypeCode.UInt64 ||
                typeCode == TypeCode.Single ||
                typeCode == TypeCode.Double ||
                typeCode == TypeCode.Decimal ||
                typeCode == TypeCode.String)
            {
                return true;
            }

            string methodName = comparisonOperator == EventBindingComparisonOperator.NotEqual
                ? "op_Inequality"
                : "op_Equality";
            return FindBinaryOperator(valueType, methodName) != null;
        }

        private static bool SupportsRelationalComparison(
            Type valueType,
            EventBindingComparisonOperator comparisonOperator)
        {
            if (valueType.IsEnum)
            {
                return false;
            }

            TypeCode typeCode = Type.GetTypeCode(valueType);
            if (typeCode == TypeCode.Char ||
                typeCode == TypeCode.SByte ||
                typeCode == TypeCode.Byte ||
                typeCode == TypeCode.Int16 ||
                typeCode == TypeCode.UInt16 ||
                typeCode == TypeCode.Int32 ||
                typeCode == TypeCode.UInt32 ||
                typeCode == TypeCode.Int64 ||
                typeCode == TypeCode.UInt64 ||
                typeCode == TypeCode.Single ||
                typeCode == TypeCode.Double ||
                typeCode == TypeCode.Decimal)
            {
                return true;
            }

            return FindBinaryOperator(valueType, GetOperatorMethodName(comparisonOperator)) != null;
        }

        private static MethodInfo FindBinaryOperator(Type valueType, string methodName)
        {
            if (valueType == null || string.IsNullOrEmpty(methodName))
            {
                return null;
            }

            string cacheKey = valueType.AssemblyQualifiedName + "|2|" + methodName;
            lock (OperatorMethodCacheLock)
            {
                if (OperatorMethodCache.TryGetValue(cacheKey, out MethodInfo cachedMethod))
                {
                    return cachedMethod;
                }
            }

            MethodInfo method = valueType.GetMethod(
                methodName,
                BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy,
                null,
                new[] { valueType, valueType },
                null);
            if (method != null && method.ReturnType != typeof(bool))
            {
                method = null;
            }

            lock (OperatorMethodCacheLock)
            {
                OperatorMethodCache[cacheKey] = method;
            }

            return method;
        }

        private static bool TryInvokeBinaryOperator(
            string methodName,
            object left,
            object right,
            Type valueType,
            out bool result,
            out string error)
        {
            result = false;
            error = string.Empty;

            if (string.IsNullOrEmpty(methodName) || valueType == null)
            {
                return false;
            }

            MethodInfo method = FindBinaryOperator(valueType, methodName);
            if (method == null)
            {
                return false;
            }

            try
            {
                Func<object, object, bool> invoker = GetBinaryInvoker(
                    valueType,
                    methodName,
                    method);
                if (invoker != null)
                {
                    result = invoker(left, right);
                    return true;
                }

                if (binaryArguments == null)
                {
                    binaryArguments = new object[2];
                }

                binaryArguments[0] = left;
                binaryArguments[1] = right;
                result = (bool)method.Invoke(null, binaryArguments);
                binaryArguments[0] = null;
                binaryArguments[1] = null;
                return true;
            }
            catch (Exception exception)
            {
                if (binaryArguments != null)
                {
                    binaryArguments[0] = null;
                    binaryArguments[1] = null;
                }

                error = $"Operator execution failed: {exception.GetBaseException().Message}";
                return false;
            }
        }

        private static Func<object, bool> GetUnaryInvoker(
            Type valueType,
            string methodName,
            MethodInfo method)
        {
            string cacheKey = valueType.AssemblyQualifiedName + "|1|" + methodName;
            lock (OperatorMethodCacheLock)
            {
                if (UnaryInvokerCache.TryGetValue(cacheKey, out Func<object, bool> cached))
                {
                    return cached;
                }
            }

            Func<object, bool> invoker = null;
#if !ENABLE_IL2CPP
            try
            {
                ParameterExpression value = Expression.Parameter(typeof(object), "value");
                MethodCallExpression call = Expression.Call(
                    method,
                    Expression.Convert(value, valueType));
                invoker = Expression.Lambda<Func<object, bool>>(call, value).Compile();
            }
            catch
            {
                invoker = null;
            }
#endif
            lock (OperatorMethodCacheLock)
            {
                UnaryInvokerCache[cacheKey] = invoker;
            }

            return invoker;
        }

        private static Func<object, object, bool> GetBinaryInvoker(
            Type valueType,
            string methodName,
            MethodInfo method)
        {
            string cacheKey = valueType.AssemblyQualifiedName + "|2|" + methodName;
            lock (OperatorMethodCacheLock)
            {
                if (BinaryInvokerCache.TryGetValue(
                        cacheKey,
                        out Func<object, object, bool> cached))
                {
                    return cached;
                }
            }

            Func<object, object, bool> invoker = null;
#if !ENABLE_IL2CPP
            try
            {
                ParameterExpression left = Expression.Parameter(typeof(object), "left");
                ParameterExpression right = Expression.Parameter(typeof(object), "right");
                MethodCallExpression call = Expression.Call(
                    method,
                    Expression.Convert(left, valueType),
                    Expression.Convert(right, valueType));
                invoker = Expression.Lambda<Func<object, object, bool>>(
                    call,
                    left,
                    right).Compile();
            }
            catch
            {
                invoker = null;
            }
#endif
            lock (OperatorMethodCacheLock)
            {
                BinaryInvokerCache[cacheKey] = invoker;
            }

            return invoker;
        }

        private static string GetOperatorMethodName(EventBindingComparisonOperator comparisonOperator)
        {
            switch (comparisonOperator)
            {
                case EventBindingComparisonOperator.GreaterThan:
                    return "op_GreaterThan";
                case EventBindingComparisonOperator.GreaterThanOrEqual:
                    return "op_GreaterThanOrEqual";
                case EventBindingComparisonOperator.LessThan:
                    return "op_LessThan";
                case EventBindingComparisonOperator.LessThanOrEqual:
                    return "op_LessThanOrEqual";
                default:
                    return string.Empty;
            }
        }

        private static string GetOperatorSymbol(EventBindingComparisonOperator comparisonOperator)
        {
            switch (comparisonOperator)
            {
                case EventBindingComparisonOperator.Equal:
                    return "==";
                case EventBindingComparisonOperator.NotEqual:
                    return "!=";
                case EventBindingComparisonOperator.GreaterThan:
                    return ">";
                case EventBindingComparisonOperator.GreaterThanOrEqual:
                    return ">=";
                case EventBindingComparisonOperator.LessThan:
                    return "<";
                case EventBindingComparisonOperator.LessThanOrEqual:
                    return "<=";
                default:
                    return comparisonOperator.ToString();
            }
        }

        private static bool IsNullValue(object value)
        {
            if (value == null)
            {
                return true;
            }

            return value is UnityEngine.Object unityObject && unityObject == null;
        }
    }
}
