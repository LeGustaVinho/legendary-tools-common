using System;
using System.Collections.Generic;
using DeterministicFixedPoint;

namespace LegendaryTools.ModifierSystem
{
    public interface IAttributeValuePolicy<T>
    {
        IReadOnlyCollection<ModifierOperation> SupportedOperations { get; }
        bool IsValid(T value, out string error);
        T Apply(T current, ModifierOperation operation, T operand);
        int Compare(T left, T right);
    }

    public sealed class DelegateValuePolicy<T> : IAttributeValuePolicy<T>
    {
        private readonly HashSet<ModifierOperation> _operations;
        private readonly IReadOnlyCollection<ModifierOperation> _operationsView;
        private readonly Func<T, ModifierOperation, T, T> _apply;
        private readonly Func<T, string> _validate;
        private readonly Comparison<T> _compare;

        public IReadOnlyCollection<ModifierOperation> SupportedOperations => _operationsView;

        public DelegateValuePolicy(
            IEnumerable<ModifierOperation> operations,
            Func<T, ModifierOperation, T, T> apply,
            Comparison<T> compare = null,
            Func<T, string> validate = null)
        {
            _operations = new HashSet<ModifierOperation>(operations ?? throw new ArgumentNullException(nameof(operations)));
            var orderedOperations = new List<ModifierOperation>(_operations);
            orderedOperations.Sort();
            _operationsView = Array.AsReadOnly(orderedOperations.ToArray());
            _apply = apply ?? throw new ArgumentNullException(nameof(apply));
            _compare = compare ?? Comparer<T>.Default.Compare;
            _validate = validate;
        }

        public bool IsValid(T value, out string error)
        {
            error = _validate?.Invoke(value);
            return string.IsNullOrEmpty(error);
        }

        public T Apply(T current, ModifierOperation operation, T operand)
        {
            if (!_operations.Contains(operation))
                throw new InvalidOperationException($"Operation {operation} is not supported for {typeof(T).Name}.");
            return _apply(current, operation, operand);
        }

        public int Compare(T left, T right) => _compare(left, right);
    }

    public static class NumericValuePolicies
    {
        private static readonly ModifierOperation[] Operations =
        {
            ModifierOperation.Add, ModifierOperation.Multiply, ModifierOperation.Replace,
            ModifierOperation.ClampMinimum, ModifierOperation.ClampMaximum,
            ModifierOperation.Minimum, ModifierOperation.Maximum
        };

        public static IAttributeValuePolicy<int> Int32(Func<int, string> validator = null) =>
            new DelegateValuePolicy<int>(Operations, ApplyInt32, validate: validator);

        public static IAttributeValuePolicy<long> Int64(Func<long, string> validator = null) =>
            new DelegateValuePolicy<long>(Operations, ApplyInt64, validate: validator);

        public static IAttributeValuePolicy<DetS32> FixedS32(Func<DetS32, string> validator = null) =>
            new DelegateValuePolicy<DetS32>(Operations, ApplyFixedS32, validate: validator);

        public static IAttributeValuePolicy<DetS64> FixedS64(Func<DetS64, string> validator = null) =>
            new DelegateValuePolicy<DetS64>(Operations, ApplyFixedS64, validate: validator);

        public static IAttributeValuePolicy<DetU32> FixedU32(Func<DetU32, string> validator = null) =>
            new DelegateValuePolicy<DetU32>(Operations, ApplyFixedU32, validate: validator);

        public static IAttributeValuePolicy<DetU64> FixedU64(Func<DetU64, string> validator = null) =>
            new DelegateValuePolicy<DetU64>(Operations, ApplyFixedU64, validate: validator);

        private static int ApplyInt32(int value, ModifierOperation operation, int operand)
        {
            switch (operation)
            {
                case ModifierOperation.Add: return checked(value + operand);
                case ModifierOperation.Multiply: return checked(value * operand);
                case ModifierOperation.Replace: return operand;
                case ModifierOperation.ClampMinimum: return Math.Max(value, operand);
                case ModifierOperation.ClampMaximum: return Math.Min(value, operand);
                case ModifierOperation.Minimum: return Math.Min(value, operand);
                case ModifierOperation.Maximum: return Math.Max(value, operand);
                default: throw new InvalidOperationException($"Unsupported Int32 operation {operation}.");
            }
        }

        private static long ApplyInt64(long value, ModifierOperation operation, long operand)
        {
            switch (operation)
            {
                case ModifierOperation.Add: return checked(value + operand);
                case ModifierOperation.Multiply: return checked(value * operand);
                case ModifierOperation.Replace: return operand;
                case ModifierOperation.ClampMinimum: return Math.Max(value, operand);
                case ModifierOperation.ClampMaximum: return Math.Min(value, operand);
                case ModifierOperation.Minimum: return Math.Min(value, operand);
                case ModifierOperation.Maximum: return Math.Max(value, operand);
                default: throw new InvalidOperationException($"Unsupported Int64 operation {operation}.");
            }
        }

        private static DetS32 ApplyFixedS32(DetS32 value, ModifierOperation operation, DetS32 operand)
        {
            switch (operation)
            {
                case ModifierOperation.Add: return value + operand;
                case ModifierOperation.Multiply: return value * operand;
                case ModifierOperation.Replace: return operand;
                case ModifierOperation.ClampMinimum:
                case ModifierOperation.Maximum: return value >= operand ? value : operand;
                case ModifierOperation.ClampMaximum:
                case ModifierOperation.Minimum: return value <= operand ? value : operand;
                default: throw new InvalidOperationException($"Unsupported DetS32 operation {operation}.");
            }
        }

        private static DetS64 ApplyFixedS64(DetS64 value, ModifierOperation operation, DetS64 operand)
        {
            switch (operation)
            {
                case ModifierOperation.Add: return value + operand;
                case ModifierOperation.Multiply: return value * operand;
                case ModifierOperation.Replace: return operand;
                case ModifierOperation.ClampMinimum:
                case ModifierOperation.Maximum: return value >= operand ? value : operand;
                case ModifierOperation.ClampMaximum:
                case ModifierOperation.Minimum: return value <= operand ? value : operand;
                default: throw new InvalidOperationException($"Unsupported DetS64 operation {operation}.");
            }
        }

        private static DetU32 ApplyFixedU32(DetU32 value, ModifierOperation operation, DetU32 operand)
        {
            switch (operation)
            {
                case ModifierOperation.Add: return value + operand;
                case ModifierOperation.Multiply: return value * operand;
                case ModifierOperation.Replace: return operand;
                case ModifierOperation.ClampMinimum:
                case ModifierOperation.Maximum: return value >= operand ? value : operand;
                case ModifierOperation.ClampMaximum:
                case ModifierOperation.Minimum: return value <= operand ? value : operand;
                default: throw new InvalidOperationException($"Unsupported DetU32 operation {operation}.");
            }
        }

        private static DetU64 ApplyFixedU64(DetU64 value, ModifierOperation operation, DetU64 operand)
        {
            switch (operation)
            {
                case ModifierOperation.Add: return value + operand;
                case ModifierOperation.Multiply: return value * operand;
                case ModifierOperation.Replace: return operand;
                case ModifierOperation.ClampMinimum:
                case ModifierOperation.Maximum: return value >= operand ? value : operand;
                case ModifierOperation.ClampMaximum:
                case ModifierOperation.Minimum: return value <= operand ? value : operand;
                default: throw new InvalidOperationException($"Unsupported DetU64 operation {operation}.");
            }
        }
    }
}
