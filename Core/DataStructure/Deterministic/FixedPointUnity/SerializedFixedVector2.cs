using System;
using UnityEngine;

namespace LegendaryTools.FixedPoint
{
    /// <summary>Unity asset storage: raw coordinates with declared decimal precision. Use Value in simulation.</summary>
    [Serializable]
    public struct SerializedFixedVector2
    {
        [SerializeField] private long _xRaw;
        [SerializeField] private long _yRaw;
        [SerializeField] private byte _precisionCode;
        public readonly int DecimalPlaces => FixedVectorSerialization.Precision(_precisionCode);
        public SerializedFixedVector2(long xRaw, long yRaw, int decimalPlaces)
        {
            if (decimalPlaces < 0 || decimalPlaces > 18) throw new ArgumentOutOfRangeException(nameof(decimalPlaces));
            _xRaw = xRaw; _yRaw = yRaw; _precisionCode = (byte)(decimalPlaces + 1);
        }
        public SerializedFixedVector2(in FixedVector2 value)
        { this = default; TrySetValue(value, out FixedStatus status); status.ThrowIfFailure(); }
        public FixedVector2 Value
        {
            readonly get { TryGetValue(out FixedVector2 value, out FixedStatus status); status.ThrowIfFailure(); return value; }
            set { TrySetValue(value, out FixedStatus status); status.ThrowIfFailure(); }
        }
        public readonly bool TryGetValue(out FixedVector2 value, out FixedStatus status)
        {
            value = FixedVector2.NaN;
            if (!FixedRuntime.IsInitialized) { status = FixedStatus.Uninitialized; return false; }
            status = FixedStatus.None;
            FixedVectorSerialization.TryRescale(_xRaw, DecimalPlaces, FixedRuntime.Settings.DecimalPlaces64, out long x, out FixedStatus sx); status |= sx;
            FixedVectorSerialization.TryRescale(_yRaw, DecimalPlaces, FixedRuntime.Settings.DecimalPlaces64, out long y, out FixedStatus sy); status |= sy;
            if (!status.IsSuccess()) return false;
            value = FixedVector2.FromRaw(x, y); return true;
        }
        public bool TrySetValue(in FixedVector2 value, out FixedStatus status)
        {
            if (!FixedRuntime.IsInitialized) { status = FixedStatus.Uninitialized; return false; }
            _xRaw = value.x.RawValue; _yRaw = value.y.RawValue;
            _precisionCode = (byte)(FixedRuntime.Settings.DecimalPlaces64 + 1); status = FixedStatus.None; return true;
        }
        public bool TrySetDecimalPlaces(int decimalPlaces, out FixedStatus status)
        {
            status = FixedStatus.None;
            FixedVectorSerialization.TryRescale(_xRaw, DecimalPlaces, decimalPlaces, out long x, out FixedStatus sx); status |= sx;
            FixedVectorSerialization.TryRescale(_yRaw, DecimalPlaces, decimalPlaces, out long y, out FixedStatus sy); status |= sy;
            if (!status.IsSuccess()) return false;
            _xRaw = x; _yRaw = y; _precisionCode = (byte)(decimalPlaces + 1); return true;
        }
    }
}
