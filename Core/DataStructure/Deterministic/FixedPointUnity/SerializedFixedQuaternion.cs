using System;
using UnityEngine;

namespace LegendaryTools.FixedPoint
{
    /// <summary>Four raw components with declared precision. Default storage is zero at three decimal places.</summary>
    [Serializable]
    public struct SerializedFixedQuaternion
    {
        [SerializeField] private long _xRaw, _yRaw, _zRaw, _wRaw;
        [SerializeField] private byte _precisionCode;
        public readonly int DecimalPlaces => FixedVectorSerialization.Precision(_precisionCode);
        public SerializedFixedQuaternion(long xRaw, long yRaw, long zRaw, long wRaw, int decimalPlaces)
        {
            if (decimalPlaces < 0 || decimalPlaces > 18) throw new ArgumentOutOfRangeException(nameof(decimalPlaces));
            _xRaw = xRaw; _yRaw = yRaw; _zRaw = zRaw; _wRaw = wRaw; _precisionCode = (byte)(decimalPlaces + 1);
        }
        public SerializedFixedQuaternion(in FixedQuaternion value)
        { this = default; TrySetValue(value, out var status); status.ThrowIfFailure(); }
        public FixedQuaternion Value
        {
            readonly get { TryGetValue(out var value, out var status); status.ThrowIfFailure(); return value; }
            set { TrySetValue(value, out var status); status.ThrowIfFailure(); }
        }
        public readonly bool TryGetValue(out FixedQuaternion value, out FixedStatus status)
        {
            value = FixedQuaternion.NaN;
            if (!FixedRuntime.IsInitialized) { status = FixedStatus.Uninitialized; return false; }
            bool ok = TryRescale(FixedRuntime.Settings.DecimalPlaces64, out var x, out var y, out var z, out var w, out status);
            if (ok) value = FixedQuaternion.FromRaw(x, y, z, w); return ok;
        }
        public bool TrySetValue(in FixedQuaternion value, out FixedStatus status)
        {
            if (!FixedRuntime.IsInitialized) { status = FixedStatus.Uninitialized; return false; }
            _xRaw = value.x.RawValue; _yRaw = value.y.RawValue; _zRaw = value.z.RawValue; _wRaw = value.w.RawValue;
            _precisionCode = (byte)(FixedRuntime.Settings.DecimalPlaces64 + 1); status = FixedStatus.None; return true;
        }
        private readonly bool TryRescale(int decimalPlaces, out long x, out long y, out long z, out long w, out FixedStatus status)
        {
            FixedVectorSerialization.TryRescale(_xRaw, DecimalPlaces, decimalPlaces, out x, out var sx);
            FixedVectorSerialization.TryRescale(_yRaw, DecimalPlaces, decimalPlaces, out y, out var sy);
            FixedVectorSerialization.TryRescale(_zRaw, DecimalPlaces, decimalPlaces, out z, out var sz);
            FixedVectorSerialization.TryRescale(_wRaw, DecimalPlaces, decimalPlaces, out w, out var sw);
            status = sx | sy | sz | sw; return status.IsSuccess();
        }
        public bool TrySetDecimalPlaces(int decimalPlaces, out FixedStatus status)
        {
            if (!TryRescale(decimalPlaces, out var x, out var y, out var z, out var w, out status)) return false;
            _xRaw = x; _yRaw = y; _zRaw = z; _wRaw = w; _precisionCode = (byte)(decimalPlaces + 1); return true;
        }
    }
}
