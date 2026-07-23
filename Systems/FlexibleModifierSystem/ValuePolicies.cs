using System;
using System.Collections.Generic;

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

        public static IAttributeValuePolicy<float> Float(Func<float, string> validator = null) =>
            new DelegateValuePolicy<float>(Operations, ApplySingle, validate: validator);

        public static IAttributeValuePolicy<double> Double(Func<double, string> validator = null) =>
            new DelegateValuePolicy<double>(Operations, ApplyDouble, validate: validator);

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

        private static float ApplySingle(float value, ModifierOperation operation, float operand)
        {
            switch (operation)
            {
                case ModifierOperation.Add: return value + operand;
                case ModifierOperation.Multiply: return value * operand;
                case ModifierOperation.Replace: return operand;
                case ModifierOperation.ClampMinimum: return Math.Max(value, operand);
                case ModifierOperation.ClampMaximum: return Math.Min(value, operand);
                case ModifierOperation.Minimum: return Math.Min(value, operand);
                case ModifierOperation.Maximum: return Math.Max(value, operand);
                default: throw new InvalidOperationException($"Unsupported Single operation {operation}.");
            }
        }

        private static double ApplyDouble(double value, ModifierOperation operation, double operand)
        {
            switch (operation)
            {
                case ModifierOperation.Add: return value + operand;
                case ModifierOperation.Multiply: return value * operand;
                case ModifierOperation.Replace: return operand;
                case ModifierOperation.ClampMinimum: return Math.Max(value, operand);
                case ModifierOperation.ClampMaximum: return Math.Min(value, operand);
                case ModifierOperation.Minimum: return Math.Min(value, operand);
                case ModifierOperation.Maximum: return Math.Max(value, operand);
                default: throw new InvalidOperationException($"Unsupported Double operation {operation}.");
            }
        }
    }
}
