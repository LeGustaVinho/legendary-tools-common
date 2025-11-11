using System;
using System.Collections.Generic;

namespace LegendaryTools.Reactive.Core
{
    /// <summary>
    /// Fluent builder for same-type binding: Observable&lt;T&gt; ↔ Observable&lt;T&gt;.
    /// </summary>
    public sealed class ObservableBindingBuilder<T>
        where T : IEquatable<T>, IComparable<T>, IComparable, IConvertible
    {
        private readonly Observable<T> _left;
        private readonly Observable<T> _right;

        private BindDirection _direction = BindDirection.TwoWay;
        private IEqualityComparer<T> _comparer = EqualityComparer<T>.Default;
        private Func<T, T> _toRight;
        private Func<T, T> _fromRight;

        private object _owner;
        private BindingOptions _options;

        public ObservableBindingBuilder(Observable<T> left, Observable<T> right)
        {
            _left = left ?? throw new ArgumentNullException(nameof(left));
            _right = right ?? throw new ArgumentNullException(nameof(right));
        }

        public ObservableBindingBuilder<T> TwoWay()
        {
            _direction = BindDirection.TwoWay;
            return this;
        }

        public ObservableBindingBuilder<T> ToOther()
        {
            _direction = BindDirection.ToUI; // semantic: left -> right
            return this;
        }

        public ObservableBindingBuilder<T> FromOther()
        {
            _direction = BindDirection.FromUI; // semantic: right -> left
            return this;
        }

        public ObservableBindingBuilder<T> Comparer(IEqualityComparer<T> comparer)
        {
            _comparer = comparer ?? EqualityComparer<T>.Default;
            return this;
        }

        public ObservableBindingBuilder<T> Converters(Func<T, T> toRight = null, Func<T, T> fromRight = null)
        {
            _toRight = toRight;
            _fromRight = fromRight;
            return this;
        }

        public ObservableBindingBuilder<T> Owner(object owner)
        {
            _owner = owner;
            return this;
        }

        public BindingHandle With(BindingOptions options = null)
        {
            _options ??= options ?? new BindingOptions();
            return _left.Bind(_right, _direction, _comparer, _owner, _options, _toRight, _fromRight);
        }
    }

    /// <summary>
    /// Fluent builder for cross-type binding: Observable&lt;TLeft&gt; ↔ Observable&lt;TRight&gt;.
    /// </summary>
    public sealed class ObservableBindingBuilder<TLeft, TRight>
        where TLeft : IEquatable<TLeft>, IComparable<TLeft>, IComparable, IConvertible
        where TRight : IEquatable<TRight>, IComparable<TRight>, IComparable, IConvertible
    {
        private readonly Observable<TLeft> _left;
        private readonly Observable<TRight> _right;

        private BindDirection _direction = BindDirection.TwoWay;
        private Func<TLeft, TRight> _toRight;
        private Func<TRight, TLeft> _fromRight;
        private IEqualityComparer<TLeft> _leftComparer = EqualityComparer<TLeft>.Default;
        private IEqualityComparer<TRight> _rightComparer = EqualityComparer<TRight>.Default;

        private object _owner;
        private BindingOptions _options;

        public ObservableBindingBuilder(Observable<TLeft> left, Observable<TRight> right)
        {
            _left = left ?? throw new ArgumentNullException(nameof(left));
            _right = right ?? throw new ArgumentNullException(nameof(right));
        }

        public ObservableBindingBuilder<TLeft, TRight> TwoWay()
        {
            _direction = BindDirection.TwoWay;
            return this;
        }

        public ObservableBindingBuilder<TLeft, TRight> ToOther()
        {
            _direction = BindDirection.ToUI;
            return this;
        }

        public ObservableBindingBuilder<TLeft, TRight> FromOther()
        {
            _direction = BindDirection.FromUI;
            return this;
        }

        public ObservableBindingBuilder<TLeft, TRight> Converters(Func<TLeft, TRight> toRight,
            Func<TRight, TLeft> fromRight)
        {
            _toRight = toRight;
            _fromRight = fromRight;
            return this;
        }

        public ObservableBindingBuilder<TLeft, TRight> LeftComparer(IEqualityComparer<TLeft> comparer)
        {
            _leftComparer = comparer ?? EqualityComparer<TLeft>.Default;
            return this;
        }

        public ObservableBindingBuilder<TLeft, TRight> RightComparer(IEqualityComparer<TRight> comparer)
        {
            _rightComparer = comparer ?? EqualityComparer<TRight>.Default;
            return this;
        }

        public ObservableBindingBuilder<TLeft, TRight> Owner(object owner)
        {
            _owner = owner;
            return this;
        }

        public BindingHandle With(BindingOptions options = null)
        {
            _options ??= options ?? new BindingOptions();
            return _left.Bind(_right, _direction, _toRight, _fromRight, _leftComparer, _rightComparer, _owner,
                _options);
        }
    }
}