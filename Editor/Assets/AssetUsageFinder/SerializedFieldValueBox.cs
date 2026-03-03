using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LegendaryTools.Editor
{
    [Serializable]
    public sealed class SerializedFieldValueBox
    {
        public Object ObjectValue;
        public bool BoolValue;
        public int IntValue;
        public long LongValue;
        public float FloatValue;
        public double DoubleValue;
        public string StringValue = string.Empty;

        public int EnumIndex;
        public string EnumName;

        public Vector2 Vector2Value;
        public Vector3 Vector3Value;
        public Vector4 Vector4Value;
        public Color ColorValue = Color.white;
        public Rect RectValue;
        public Bounds BoundsValue;
        public AnimationCurve CurveValue = AnimationCurve.Linear(0, 0, 1, 1);
        public Quaternion QuaternionValue = Quaternion.identity;
    }
}