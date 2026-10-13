using UnityEngine;

namespace LegendaryTools.FixedPoint
{
    /// <summary>Explicit presentation/import boundary. Floating-point Unity geometry is not deterministic simulation state.</summary>
    public static class FixedBoundsUnity
    {
        public static Bounds ToUnityBounds(this FixedBounds3 value)
        { var result = new Bounds(); result.SetMinMax(value.min.ToUnityVector3(), value.max.ToUnityVector3()); return result; }
        public static FixedBounds3 ToFixedBounds3(this Bounds value) => ToFixedBounds3(value, out _);
        public static FixedBounds3 ToFixedBounds3(this Bounds value, out FixedStatus status)
        {
            var min = value.min.ToFixedVector3(out var a); var max = value.max.ToFixedVector3(out var b);
            status = a | b;
            if (!status.IsSuccess()) return FixedBounds3.Invalid;
            var result = FixedBounds3.FromMinMax(min, max, out var c); status |= c; return result;
        }
        public static Rect ToUnityRect(this FixedBounds2 value) => Rect.MinMaxRect(value.min.x.ToSingle(), value.min.y.ToSingle(), value.max.x.ToSingle(), value.max.y.ToSingle());
        public static FixedBounds2 ToFixedBounds2(this Rect value) => ToFixedBounds2(value, out _);
        public static FixedBounds2 ToFixedBounds2(this Rect value, out FixedStatus status)
        {
            var min = value.min.ToFixedVector2(out var a); var max = value.max.ToFixedVector2(out var b);
            status = a | b;
            if (!status.IsSuccess()) return FixedBounds2.Invalid;
            var result = FixedBounds2.FromMinMax(min, max, out var c); status |= c; return result;
        }
        public static FixedRay3 ToFixedRay3(this Ray value, out FixedStatus status)
        { var origin = value.origin.ToFixedVector3(out var a); var direction = value.direction.ToFixedVector3(out var b); status = a | b; return new FixedRay3(origin, direction); }
        public static FixedRay3 ToFixedRay3(this Ray value) => ToFixedRay3(value, out _);
        public static FixedRay2 ToFixedRay2(this Ray2D value, out FixedStatus status)
        { var origin = value.origin.ToFixedVector2(out var a); var direction = value.direction.ToFixedVector2(out var b); status = a | b; return new FixedRay2(origin, direction); }
        public static FixedRay2 ToFixedRay2(this Ray2D value) => ToFixedRay2(value, out _);
    }
}
