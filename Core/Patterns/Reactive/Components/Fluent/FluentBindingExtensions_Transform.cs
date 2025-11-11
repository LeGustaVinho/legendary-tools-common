using UnityEngine;

namespace LegendaryTools.Reactive.Unity
{
    /// <summary>
    /// Entry points for Transform fluent binding DSL.
    /// </summary>
    public static class FluentBindingExtensions_Transform
    {
        public static TransformBindingBuilder<Vector3> Bind(this Observable<Vector3> observable, Transform target)
        {
            return new TransformBindingBuilder<Vector3>(observable, target);
        }

        public static TransformBindingBuilder<Quaternion> Bind(this Observable<Quaternion> observable, Transform target)
        {
            return new TransformBindingBuilder<Quaternion>(observable, target);
        }

        public static TransformBindingBuilder<float> Bind(this Observable<float> observable, Transform target)
        {
            return new TransformBindingBuilder<float>(observable, target);
        }

        public static TransformActiveBindingBuilder Bind(this Observable<bool> observable, Transform target)
        {
            return new TransformActiveBindingBuilder(observable, target);
        }
    }
}