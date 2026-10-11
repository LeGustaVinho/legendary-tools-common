using UnityEngine;

namespace LegendaryTools.FixedPoint
{
    /// <summary>Explicit presentation/input conversions. Unity floating-point vectors are not simulation state.</summary>
    public static class FixedVectorUnity
    {
        public static Vector2 ToUnityVector2(this FixedVector2 value) => new Vector2(value.x.ToSingle(), value.y.ToSingle());
        public static Vector3 ToUnityVector3(this FixedVector3 value) => new Vector3(value.x.ToSingle(), value.y.ToSingle(), value.z.ToSingle());
        public static FixedVector2 ToFixedVector2(this Vector2 value) => ToFixedVector2(value, out _);
        public static FixedVector2 ToFixedVector2(this Vector2 value, out FixedStatus status)
        {
            Fixed64 x = Fixed64.FromSingle(value.x, out FixedStatus sx), y = Fixed64.FromSingle(value.y, out FixedStatus sy);
            status = sx | sy; return new FixedVector2(x, y);
        }
        public static FixedVector3 ToFixedVector3(this Vector3 value) => ToFixedVector3(value, out _);
        public static FixedVector3 ToFixedVector3(this Vector3 value, out FixedStatus status)
        {
            Fixed64 x = Fixed64.FromSingle(value.x, out FixedStatus sx), y = Fixed64.FromSingle(value.y, out FixedStatus sy), z = Fixed64.FromSingle(value.z, out FixedStatus sz);
            status = sx | sy | sz; return new FixedVector3(x, y, z);
        }
    }
}
