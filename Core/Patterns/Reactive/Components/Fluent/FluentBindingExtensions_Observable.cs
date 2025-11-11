namespace LegendaryTools.Reactive.Core
{
    /// <summary>
    /// Entry points for Observable-to-Observable fluent binding DSL.
    /// </summary>
    public static class FluentBindingExtensions_Observable
    {
        public static ObservableBindingBuilder<TLeft> Bind<TLeft>(this Observable<TLeft> left, Observable<TLeft> right)
            where TLeft : System.IEquatable<TLeft>, System.IComparable<TLeft>, System.IComparable, System.IConvertible
        {
            return new ObservableBindingBuilder<TLeft>(left, right);
        }

        public static ObservableBindingBuilder<TLeft, TRight> Bind<TLeft, TRight>(this Observable<TLeft> left,
            Observable<TRight> right)
            where TLeft : System.IEquatable<TLeft>, System.IComparable<TLeft>, System.IComparable, System.IConvertible
            where TRight : System.IEquatable<TRight>, System.IComparable<TRight>, System.IComparable,
            System.IConvertible
        {
            return new ObservableBindingBuilder<TLeft, TRight>(left, right);
        }
    }
}