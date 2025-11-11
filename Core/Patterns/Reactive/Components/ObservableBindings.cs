using System;
using System.Collections.Generic;
using UnityEngine; // MonoBehaviour, Object
using LegendaryTools.Reactive; // BindDirection, BindingHandle, BindingOptions, BindingAnchor, BindingDisposer

namespace LegendaryTools.Reactive.Core
{
    /// <summary>
    /// Extensions to bind Observable&lt;T&gt; to another Observable.
    /// Supports same-type and cross-type bindings with converters, TwoWay/ToOther/FromOther, and debugger metadata.
    /// </summary>
    public static class ObservableBindings
    {
        // -----------------------------
        // Same-type binding
        // -----------------------------
        public static BindingHandle Bind<T>(
            this Observable<T> left,
            Observable<T> right,
            BindDirection direction = BindDirection.TwoWay,
            IEqualityComparer<T> comparer = null,
            object owner = null,
            BindingOptions options = null,
            Func<T, T> toRight = null,
            Func<T, T> fromRight = null)
            where T : IEquatable<T>, IComparable<T>, IComparable, IConvertible
        {
            if (left == null) throw new ArgumentNullException(nameof(left));
            if (right == null) throw new ArgumentNullException(nameof(right));

            comparer ??= EqualityComparer<T>.Default;
            options ??= new BindingOptions();

            BindingAnchor anchor = null;
            MonoBehaviour ownerMB = owner as MonoBehaviour;
            if (ownerMB != null)
                anchor = ownerMB.GetComponent<BindingAnchor>() ?? ownerMB.gameObject.AddComponent<BindingAnchor>();

            bool isUpdatingLeft = false;
            bool isUpdatingRight = false;

            void PushLeftToRight(T v)
            {
                if (anchor != null && !anchor.ShouldProcessNow()) return;
                try
                {
                    isUpdatingRight = true;
                    T mapped = toRight != null ? toRight(v) : v;
                    if (!comparer.Equals(right.Value, mapped))
                        right.Value = mapped;
                }
                finally
                {
                    isUpdatingRight = false;
                }
            }

            void PushRightToLeft(T v)
            {
                if (anchor != null && !anchor.ShouldProcessNow()) return;
                try
                {
                    isUpdatingLeft = true;
                    T mapped = fromRight != null ? fromRight(v) : v;
                    if (!comparer.Equals(left.Value, mapped))
                        left.Value = mapped;
                }
                finally
                {
                    isUpdatingLeft = false;
                }
            }

            void LeftChanged(IObservable<T> _, T oldV, T newV)
            {
                if (direction == BindDirection.FromUI) return; // only right -> left
                if (isUpdatingLeft || isUpdatingRight) return;
                PushLeftToRight(newV);
            }

            void RightChanged(IObservable<T> _, T oldV, T newV)
            {
                if (direction == BindDirection.ToUI) return; // only left -> right
                if (isUpdatingRight || isUpdatingLeft) return;
                PushRightToLeft(newV);
            }

            void Subscribe()
            {
                if (direction != BindDirection.FromUI) PushLeftToRight(left.Value);
                else PushRightToLeft(right.Value);

                left.OnChanged += LeftChanged;
                right.OnChanged += RightChanged;
            }

            void Unsubscribe()
            {
                left.OnChanged -= LeftChanged;
                right.OnChanged -= RightChanged;
            }

            void Resync()
            {
                if (direction != BindDirection.FromUI) PushLeftToRight(left.Value);
                else PushRightToLeft(right.Value);
            }

            BindingInfo info = new()
            {
                Kind = $"Observable↔Observable<{typeof(T).Name}>",
                Direction = direction.ToString(),
                Description = $"Observable<{typeof(T).Name}> ↔ Observable<{typeof(T).Name}>",
                // Use a Unity object as Target to satisfy the debugger UI expectations.
                Target = ownerMB != null ? (UnityEngine.Object)ownerMB
                    : anchor != null ? (UnityEngine.Object)anchor : null,
                Owner = ownerMB,
                Anchor = anchor,
                Options = options,
                GetState = () =>
                    $"left={left.Value?.ToString() ?? "<null>"}, right={right.Value?.ToString() ?? "<null>"}",
                Tags = new[] { "Observable", "TwoWay" }
            };

            BindingHandle handle = new(Subscribe, Unsubscribe, Resync, anchor, options, info);

            if (ownerMB != null)
            {
                BindingDisposer disposer = ownerMB.GetComponent<BindingDisposer>() ??
                                           ownerMB.gameObject.AddComponent<BindingDisposer>();
                disposer.Add(handle);
            }

            return handle;
        }

        // -----------------------------
        // Cross-type binding
        // -----------------------------
        public static BindingHandle Bind<TLeft, TRight>(
            this Observable<TLeft> left,
            Observable<TRight> right,
            BindDirection direction,
            Func<TLeft, TRight> toRight,
            Func<TRight, TLeft> fromRight,
            IEqualityComparer<TLeft> leftComparer = null,
            IEqualityComparer<TRight> rightComparer = null,
            object owner = null,
            BindingOptions options = null)
            where TLeft : IEquatable<TLeft>, IComparable<TLeft>, IComparable, IConvertible
            where TRight : IEquatable<TRight>, IComparable<TRight>, IComparable, IConvertible
        {
            if (left == null) throw new ArgumentNullException(nameof(left));
            if (right == null) throw new ArgumentNullException(nameof(right));

            if (direction == BindDirection.TwoWay && (toRight == null || fromRight == null))
                throw new ArgumentException(
                    "TwoWay cross-type binding requires both converters (toRight and fromRight).");

            leftComparer ??= EqualityComparer<TLeft>.Default;
            rightComparer ??= EqualityComparer<TRight>.Default;
            options ??= new BindingOptions();

            BindingAnchor anchor = null;
            MonoBehaviour ownerMB = owner as MonoBehaviour;
            if (ownerMB != null)
                anchor = ownerMB.GetComponent<BindingAnchor>() ?? ownerMB.gameObject.AddComponent<BindingAnchor>();

            bool isUpdatingLeft = false;
            bool isUpdatingRight = false;

            void PushLeftToRight(TLeft v)
            {
                if (anchor != null && !anchor.ShouldProcessNow()) return;
                if (toRight == null) return;
                try
                {
                    isUpdatingRight = true;
                    TRight mapped = toRight(v);
                    if (!rightComparer.Equals(right.Value, mapped))
                        right.Value = mapped;
                }
                finally
                {
                    isUpdatingRight = false;
                }
            }

            void PushRightToLeft(TRight v)
            {
                if (anchor != null && !anchor.ShouldProcessNow()) return;
                if (fromRight == null) return;
                try
                {
                    isUpdatingLeft = true;
                    TLeft mapped = fromRight(v);
                    if (!leftComparer.Equals(left.Value, mapped))
                        left.Value = mapped;
                }
                finally
                {
                    isUpdatingLeft = false;
                }
            }

            void LeftChanged(IObservable<TLeft> _, TLeft oldV, TLeft newV)
            {
                if (direction == BindDirection.FromUI) return; // only right->left
                if (isUpdatingLeft || isUpdatingRight) return;
                PushLeftToRight(newV);
            }

            void RightChanged(IObservable<TRight> _, TRight oldV, TRight newV)
            {
                if (direction == BindDirection.ToUI) return; // only left->right
                if (isUpdatingRight || isUpdatingLeft) return;
                PushRightToLeft(newV);
            }

            void Subscribe()
            {
                if (direction != BindDirection.FromUI) PushLeftToRight(left.Value);
                else PushRightToLeft(right.Value);

                left.OnChanged += LeftChanged;
                right.OnChanged += RightChanged;
            }

            void Unsubscribe()
            {
                left.OnChanged -= LeftChanged;
                right.OnChanged -= RightChanged;
            }

            void Resync()
            {
                if (direction != BindDirection.FromUI) PushLeftToRight(left.Value);
                else PushRightToLeft(right.Value);
            }

            BindingInfo info = new()
            {
                Kind = $"Observable<{typeof(TLeft).Name}>↔Observable<{typeof(TRight).Name}>",
                Direction = direction.ToString(),
                Description = $"Cross-type binding with converters",
                Target = ownerMB != null ? (UnityEngine.Object)ownerMB
                    : anchor != null ? (UnityEngine.Object)anchor : null,
                Owner = ownerMB,
                Anchor = anchor,
                Options = options,
                GetState = () =>
                    $"left={left.Value?.ToString() ?? "<null>"} -> right={right.Value?.ToString() ?? "<null>"}",
                Tags = new[] { "Observable", "CrossType" }
            };

            BindingHandle handle = new(Subscribe, Unsubscribe, Resync, anchor, options, info);

            if (ownerMB != null)
            {
                BindingDisposer disposer = ownerMB.GetComponent<BindingDisposer>() ??
                                           ownerMB.gameObject.AddComponent<BindingDisposer>();
                disposer.Add(handle);
            }

            return handle;
        }
    }
}