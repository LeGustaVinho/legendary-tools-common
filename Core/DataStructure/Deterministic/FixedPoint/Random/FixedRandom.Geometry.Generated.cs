using System;
namespace LegendaryTools.FixedPoint
{
    public partial struct FixedRandom
    {
        public FixedPointVector2 InsideUnitCircle() => InsideUnitCircle(out _);
        public FixedPointVector2 InsideUnitCircle(out FixedStatus status) => InsideCircle(Fixed64.One, out status);
        public bool TryInsideUnitCircle(out FixedPointVector2 value, out FixedStatus status) => TryInsideCircle(default, Fixed64.One, out value, out status);
        public FixedPointVector2 InsideUnitCircleChecked() { var v = InsideUnitCircle(out FixedStatus s); s.ThrowIfFailure(); return v; }
        public FixedPointVector2 InsideCircle(Fixed64 radius) => InsideCircle(radius, out _);
        public FixedPointVector2 InsideCircle(Fixed64 radius, out FixedStatus status) => InsideCircle(default, radius, out status);
        public bool TryInsideCircle(Fixed64 radius, out FixedPointVector2 value, out FixedStatus status) => TryInsideCircle(default, radius, out value, out status);
        public FixedPointVector2 InsideCircle(FixedPointVector2 center, Fixed64 radius) => InsideCircle(center, radius, out _);
        public FixedPointVector2 InsideCircle(FixedPointVector2 center, Fixed64 radius, out FixedStatus status)
        { TryInsideCircle(center, radius, out FixedPointVector2 value, out status); return value; }
        public bool TryInsideCircle(FixedPointVector2 center, Fixed64 radius, out FixedPointVector2 value, out FixedStatus status)
        {
            bool ok = FixedRandomGeometry.Inside(ref this, center.X.Bits, center.Y.Bits, 0UL, radius.Bits, false, FixedRuntime.State, out FixedPointVector3 point, out status);
            value = new FixedPointVector2(point.X, point.Y); return ok;
        }
        public FixedPointVector2 InsideCircleChecked(FixedPointVector2 center, Fixed64 radius)
        { var v = InsideCircle(center, radius, out FixedStatus s); s.ThrowIfFailure(); return v; }
        public FixedPointVector2 OnUnitCircle() => OnUnitCircle(out _);
        public FixedPointVector2 OnUnitCircle(out FixedStatus status) => OnCircle(Fixed64.One, out status);
        public bool TryOnUnitCircle(out FixedPointVector2 value, out FixedStatus status) => TryOnCircle(default, Fixed64.One, out value, out status);
        public FixedPointVector2 OnUnitCircleChecked() { var v = OnUnitCircle(out FixedStatus s); s.ThrowIfFailure(); return v; }
        public FixedPointVector2 OnCircle(Fixed64 radius) => OnCircle(radius, out _);
        public FixedPointVector2 OnCircle(Fixed64 radius, out FixedStatus status) => OnCircle(default, radius, out status);
        public bool TryOnCircle(Fixed64 radius, out FixedPointVector2 value, out FixedStatus status) => TryOnCircle(default, radius, out value, out status);
        public FixedPointVector2 OnCircle(FixedPointVector2 center, Fixed64 radius) => OnCircle(center, radius, out _);
        public FixedPointVector2 OnCircle(FixedPointVector2 center, Fixed64 radius, out FixedStatus status)
        { TryOnCircle(center, radius, out FixedPointVector2 value, out status); return value; }
        public bool TryOnCircle(FixedPointVector2 center, Fixed64 radius, out FixedPointVector2 value, out FixedStatus status)
        {
            bool ok = FixedRandomGeometry.On(ref this, center.X.Bits, center.Y.Bits, 0UL, radius.Bits, false, FixedRuntime.State, out FixedPointVector3 point, out status);
            value = new FixedPointVector2(point.X, point.Y); return ok;
        }
        public FixedPointVector2 OnCircleChecked(FixedPointVector2 center, Fixed64 radius)
        { var v = OnCircle(center, radius, out FixedStatus s); s.ThrowIfFailure(); return v; }
        public FixedPointVector3 InsideUnitSphere() => InsideUnitSphere(out _);
        public FixedPointVector3 InsideUnitSphere(out FixedStatus status) => InsideSphere(Fixed64.One, out status);
        public bool TryInsideUnitSphere(out FixedPointVector3 value, out FixedStatus status) => TryInsideSphere(default, Fixed64.One, out value, out status);
        public FixedPointVector3 InsideUnitSphereChecked() { var v = InsideUnitSphere(out FixedStatus s); s.ThrowIfFailure(); return v; }
        public FixedPointVector3 InsideSphere(Fixed64 radius) => InsideSphere(radius, out _);
        public FixedPointVector3 InsideSphere(Fixed64 radius, out FixedStatus status) => InsideSphere(default, radius, out status);
        public bool TryInsideSphere(Fixed64 radius, out FixedPointVector3 value, out FixedStatus status) => TryInsideSphere(default, radius, out value, out status);
        public FixedPointVector3 InsideSphere(FixedPointVector3 center, Fixed64 radius) => InsideSphere(center, radius, out _);
        public FixedPointVector3 InsideSphere(FixedPointVector3 center, Fixed64 radius, out FixedStatus status)
        { TryInsideSphere(center, radius, out FixedPointVector3 value, out status); return value; }
        public bool TryInsideSphere(FixedPointVector3 center, Fixed64 radius, out FixedPointVector3 value, out FixedStatus status)
        {
            bool ok = FixedRandomGeometry.Inside(ref this, center.X.Bits, center.Y.Bits, center.Z.Bits, radius.Bits, true, FixedRuntime.State, out FixedPointVector3 point, out status);
            value = point; return ok;
        }
        public FixedPointVector3 InsideSphereChecked(FixedPointVector3 center, Fixed64 radius)
        { var v = InsideSphere(center, radius, out FixedStatus s); s.ThrowIfFailure(); return v; }
        public FixedPointVector3 OnUnitSphere() => OnUnitSphere(out _);
        public FixedPointVector3 OnUnitSphere(out FixedStatus status) => OnSphere(Fixed64.One, out status);
        public bool TryOnUnitSphere(out FixedPointVector3 value, out FixedStatus status) => TryOnSphere(default, Fixed64.One, out value, out status);
        public FixedPointVector3 OnUnitSphereChecked() { var v = OnUnitSphere(out FixedStatus s); s.ThrowIfFailure(); return v; }
        public FixedPointVector3 OnSphere(Fixed64 radius) => OnSphere(radius, out _);
        public FixedPointVector3 OnSphere(Fixed64 radius, out FixedStatus status) => OnSphere(default, radius, out status);
        public bool TryOnSphere(Fixed64 radius, out FixedPointVector3 value, out FixedStatus status) => TryOnSphere(default, radius, out value, out status);
        public FixedPointVector3 OnSphere(FixedPointVector3 center, Fixed64 radius) => OnSphere(center, radius, out _);
        public FixedPointVector3 OnSphere(FixedPointVector3 center, Fixed64 radius, out FixedStatus status)
        { TryOnSphere(center, radius, out FixedPointVector3 value, out status); return value; }
        public bool TryOnSphere(FixedPointVector3 center, Fixed64 radius, out FixedPointVector3 value, out FixedStatus status)
        {
            bool ok = FixedRandomGeometry.On(ref this, center.X.Bits, center.Y.Bits, center.Z.Bits, radius.Bits, true, FixedRuntime.State, out FixedPointVector3 point, out status);
            value = point; return ok;
        }
        public FixedPointVector3 OnSphereChecked(FixedPointVector3 center, Fixed64 radius)
        { var v = OnSphere(center, radius, out FixedStatus s); s.ThrowIfFailure(); return v; }
        public FixedPointVector2 InsideRectangle(FixedPointVector2 min, FixedPointVector2 max) => InsideRectangle(min, max, out _);
        public FixedPointVector2 InsideRectangle(FixedPointVector2 min, FixedPointVector2 max, out FixedStatus status)
        { TryInsideRectangle(min, max, out FixedPointVector2 value, out status); return value; }
        public bool TryInsideRectangle(FixedPointVector2 min, FixedPointVector2 max, out FixedPointVector2 value, out FixedStatus status)
        {
            bool ok = FixedRandomGeometry.Box(ref this, new FixedPointVector3(min.X, min.Y, Fixed64.Zero), new FixedPointVector3(max.X, max.Y, Fixed64.Zero), false, FixedRuntime.State, out FixedPointVector3 point, out status);
            value = new FixedPointVector2(point.X, point.Y); return ok;
        }
        public FixedPointVector2 InsideRectangleChecked(FixedPointVector2 min, FixedPointVector2 max)
        { var v = InsideRectangle(min, max, out FixedStatus s); s.ThrowIfFailure(); return v; }
        public FixedPointVector3 InsideBox(FixedPointVector3 min, FixedPointVector3 max) => InsideBox(min, max, out _);
        public FixedPointVector3 InsideBox(FixedPointVector3 min, FixedPointVector3 max, out FixedStatus status)
        { TryInsideBox(min, max, out FixedPointVector3 value, out status); return value; }
        public bool TryInsideBox(FixedPointVector3 min, FixedPointVector3 max, out FixedPointVector3 value, out FixedStatus status)
        {
            bool ok = FixedRandomGeometry.Box(ref this, min, max, true, FixedRuntime.State, out FixedPointVector3 point, out status);
            value = point; return ok;
        }
        public FixedPointVector3 InsideBoxChecked(FixedPointVector3 min, FixedPointVector3 max)
        { var v = InsideBox(min, max, out FixedStatus s); s.ThrowIfFailure(); return v; }
        public FixedPointVector2 InsideTriangle(FixedPointVector2 a, FixedPointVector2 b, FixedPointVector2 c) => InsideTriangle(a, b, c, out _);
        public FixedPointVector2 InsideTriangle(FixedPointVector2 a, FixedPointVector2 b, FixedPointVector2 c, out FixedStatus status)
        { TryInsideTriangle(a, b, c, out FixedPointVector2 value, out status); return value; }
        public bool TryInsideTriangle(FixedPointVector2 a, FixedPointVector2 b, FixedPointVector2 c, out FixedPointVector2 value, out FixedStatus status)
        {
            bool ok = FixedRandomGeometry.Triangle(ref this, new FixedPointVector3(a.X, a.Y, Fixed64.Zero), new FixedPointVector3(b.X, b.Y, Fixed64.Zero), new FixedPointVector3(c.X, c.Y, Fixed64.Zero), FixedRuntime.State, out FixedPointVector3 point, out status);
            value = new FixedPointVector2(point.X, point.Y); return ok;
        }
        public FixedPointVector2 InsideTriangleChecked(FixedPointVector2 a, FixedPointVector2 b, FixedPointVector2 c)
        { var v = InsideTriangle(a, b, c, out FixedStatus s); s.ThrowIfFailure(); return v; }
        public FixedPointVector3 InsideTriangle(FixedPointVector3 a, FixedPointVector3 b, FixedPointVector3 c) => InsideTriangle(a, b, c, out _);
        public FixedPointVector3 InsideTriangle(FixedPointVector3 a, FixedPointVector3 b, FixedPointVector3 c, out FixedStatus status)
        { TryInsideTriangle(a, b, c, out FixedPointVector3 value, out status); return value; }
        public bool TryInsideTriangle(FixedPointVector3 a, FixedPointVector3 b, FixedPointVector3 c, out FixedPointVector3 value, out FixedStatus status)
        {
            bool ok = FixedRandomGeometry.Triangle(ref this, a, b, c, FixedRuntime.State, out FixedPointVector3 point, out status);
            value = point; return ok;
        }
        public FixedPointVector3 InsideTriangleChecked(FixedPointVector3 a, FixedPointVector3 b, FixedPointVector3 c)
        { var v = InsideTriangle(a, b, c, out FixedStatus s); s.ThrowIfFailure(); return v; }
        public Fixed64 NextAngleRadians() => NextAngleRadians(out _);
        public Fixed64 NextAngleRadians(out FixedStatus status) => Range(Fixed64.Zero, Fixed64.Tau, out status);
        public bool TryNextAngleRadians(out Fixed64 value, out FixedStatus status) => TryRange(Fixed64.Zero, Fixed64.Tau, out value, out status);
        public Fixed64 NextAngleRadiansChecked() { var v = NextAngleRadians(out FixedStatus s); s.ThrowIfFailure(); return v; }
    }
}
