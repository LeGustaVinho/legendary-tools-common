using UnityEngine;

namespace LegendaryTools.FixedPoint
{
    /// <summary>Explicit import/presentation boundary; conversion never normalizes implicitly.</summary>
    public static class FixedQuaternionUnity
    {
        public static Quaternion ToUnityQuaternion(this FixedQuaternion value) => new Quaternion(value.x.ToSingle(), value.y.ToSingle(), value.z.ToSingle(), value.w.ToSingle());
        public static FixedQuaternion ToFixedQuaternion(this Quaternion value) => ToFixedQuaternion(value, out _);
        public static FixedQuaternion ToFixedQuaternion(this Quaternion value, out FixedStatus status)
        {
            Fixed64 x = Fixed64.FromSingle(value.x, out var sx), y = Fixed64.FromSingle(value.y, out var sy), z = Fixed64.FromSingle(value.z, out var sz), w = Fixed64.FromSingle(value.w, out var sw);
            status = sx | sy | sz | sw; return new FixedQuaternion(x, y, z, w);
        }
    }
}
