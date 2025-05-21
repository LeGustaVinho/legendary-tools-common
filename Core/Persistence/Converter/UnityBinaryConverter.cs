using System.IO;
using UnityEngine;

namespace LegendaryTools.Persistence
{
    // Vector2 Converter
    public class Vector2BinaryConverter : IBinaryConverter<Vector2>
    {
        public void Write(BinaryWriter writer, Vector2 value)
        {
            writer.Write(value.x);
            writer.Write(value.y);
        }

        public Vector2 Read(BinaryReader reader)
        {
            float x = reader.ReadSingle();
            float y = reader.ReadSingle();
            return new Vector2(x, y);
        }

        void IBinaryConverter.Write(BinaryWriter writer, object value)
        {
            Write(writer, (Vector2)value);
        }

        object IBinaryConverter.Read(BinaryReader reader)
        {
            return Read(reader);
        }
    }

// Vector3 Converter
    public class Vector3BinaryConverter : IBinaryConverter<Vector3>
    {
        public void Write(BinaryWriter writer, Vector3 value)
        {
            writer.Write(value.x);
            writer.Write(value.y);
            writer.Write(value.z);
        }

        public Vector3 Read(BinaryReader reader)
        {
            float x = reader.ReadSingle();
            float y = reader.ReadSingle();
            float z = reader.ReadSingle();
            return new Vector3(x, y, z);
        }

        void IBinaryConverter.Write(BinaryWriter writer, object value)
        {
            Write(writer, (Vector3)value);
        }

        object IBinaryConverter.Read(BinaryReader reader)
        {
            return Read(reader);
        }
    }

// Vector4 Converter
    public class Vector4BinaryConverter : IBinaryConverter<Vector4>
    {
        public void Write(BinaryWriter writer, Vector4 value)
        {
            writer.Write(value.x);
            writer.Write(value.y);
            writer.Write(value.z);
            writer.Write(value.w);
        }

        public Vector4 Read(BinaryReader reader)
        {
            float x = reader.ReadSingle();
            float y = reader.ReadSingle();
            float z = reader.ReadSingle();
            float w = reader.ReadSingle();
            return new Vector4(x, y, z, w);
        }

        void IBinaryConverter.Write(BinaryWriter writer, object value)
        {
            Write(writer, (Vector4)value);
        }

        object IBinaryConverter.Read(BinaryReader reader)
        {
            return Read(reader);
        }
    }

// Quaternion Converter
    public class QuaternionBinaryConverter : IBinaryConverter<Quaternion>
    {
        public void Write(BinaryWriter writer, Quaternion value)
        {
            writer.Write(value.x);
            writer.Write(value.y);
            writer.Write(value.z);
            writer.Write(value.w);
        }

        public Quaternion Read(BinaryReader reader)
        {
            float x = reader.ReadSingle();
            float y = reader.ReadSingle();
            float z = reader.ReadSingle();
            float w = reader.ReadSingle();
            return new Quaternion(x, y, z, w);
        }

        void IBinaryConverter.Write(BinaryWriter writer, object value)
        {
            Write(writer, (Quaternion)value);
        }

        object IBinaryConverter.Read(BinaryReader reader)
        {
            return Read(reader);
        }
    }

// Color Converter
    public class ColorBinaryConverter : IBinaryConverter<Color>
    {
        public void Write(BinaryWriter writer, Color value)
        {
            writer.Write(value.r);
            writer.Write(value.g);
            writer.Write(value.b);
            writer.Write(value.a);
        }

        public Color Read(BinaryReader reader)
        {
            float r = reader.ReadSingle();
            float g = reader.ReadSingle();
            float b = reader.ReadSingle();
            float a = reader.ReadSingle();
            return new Color(r, g, b, a);
        }

        void IBinaryConverter.Write(BinaryWriter writer, object value)
        {
            Write(writer, (Color)value);
        }

        object IBinaryConverter.Read(BinaryReader reader)
        {
            return Read(reader);
        }
    }

// Color32 Converter
    public class Color32BinaryConverter : IBinaryConverter<Color32>
    {
        public void Write(BinaryWriter writer, Color32 value)
        {
            writer.Write(value.r);
            writer.Write(value.g);
            writer.Write(value.b);
            writer.Write(value.a);
        }

        public Color32 Read(BinaryReader reader)
        {
            byte r = reader.ReadByte();
            byte g = reader.ReadByte();
            byte b = reader.ReadByte();
            byte a = reader.ReadByte();
            return new Color32(r, g, b, a);
        }

        void IBinaryConverter.Write(BinaryWriter writer, object value)
        {
            Write(writer, (Color32)value);
        }

        object IBinaryConverter.Read(BinaryReader reader)
        {
            return Read(reader);
        }
    }

// Rect Converter
    public class RectBinaryConverter : IBinaryConverter<Rect>
    {
        public void Write(BinaryWriter writer, Rect value)
        {
            writer.Write(value.x);
            writer.Write(value.y);
            writer.Write(value.width);
            writer.Write(value.height);
        }

        public Rect Read(BinaryReader reader)
        {
            float x = reader.ReadSingle();
            float y = reader.ReadSingle();
            float width = reader.ReadSingle();
            float height = reader.ReadSingle();
            return new Rect(x, y, width, height);
        }

        void IBinaryConverter.Write(BinaryWriter writer, object value)
        {
            Write(writer, (Rect)value);
        }

        object IBinaryConverter.Read(BinaryReader reader)
        {
            return Read(reader);
        }
    }

// Bounds Converter
    public class BoundsBinaryConverter : IBinaryConverter<Bounds>
    {
        public void Write(BinaryWriter writer, Bounds value)
        {
            writer.Write(value.center.x);
            writer.Write(value.center.y);
            writer.Write(value.center.z);
            writer.Write(value.extents.x);
            writer.Write(value.extents.y);
            writer.Write(value.extents.z);
        }

        public Bounds Read(BinaryReader reader)
        {
            Vector3 center = new(
                reader.ReadSingle(),
                reader.ReadSingle(),
                reader.ReadSingle()
            );
            Vector3 extents = new(
                reader.ReadSingle(),
                reader.ReadSingle(),
                reader.ReadSingle()
            );
            return new Bounds(center, extents * 2);
        }

        void IBinaryConverter.Write(BinaryWriter writer, object value)
        {
            Write(writer, (Bounds)value);
        }

        object IBinaryConverter.Read(BinaryReader reader)
        {
            return Read(reader);
        }
    }

// LayerMask Converter
    public class LayerMaskBinaryConverter : IBinaryConverter<LayerMask>
    {
        public void Write(BinaryWriter writer, LayerMask value)
        {
            writer.Write(value.value);
        }

        public LayerMask Read(BinaryReader reader)
        {
            int intValue = reader.ReadInt32();
            LayerMask mask = new() { value = intValue };
            return mask;
        }

        void IBinaryConverter.Write(BinaryWriter writer, object value)
        {
            Write(writer, (LayerMask)value);
        }

        object IBinaryConverter.Read(BinaryReader reader)
        {
            return Read(reader);
        }
    }

// AnimationCurve Converter
    public class AnimationCurveBinaryConverter : IBinaryConverter<AnimationCurve>
    {
        public void Write(BinaryWriter writer, AnimationCurve value)
        {
            Keyframe[] keys = value.keys;
            writer.Write(keys.Length);
            foreach (Keyframe key in keys)
            {
                writer.Write(key.time);
                writer.Write(key.value);
                writer.Write(key.inTangent);
                writer.Write(key.outTangent);
                writer.Write((int)key.weightedMode);
                writer.Write(key.inWeight);
                writer.Write(key.outWeight);
            }

            writer.Write((int)value.preWrapMode);
            writer.Write((int)value.postWrapMode);
        }

        public AnimationCurve Read(BinaryReader reader)
        {
            int length = reader.ReadInt32();
            Keyframe[] keys = new Keyframe[length];
            for (int i = 0; i < length; i++)
            {
                float time = reader.ReadSingle();
                float value = reader.ReadSingle();
                float inTangent = reader.ReadSingle();
                float outTangent = reader.ReadSingle();
                WeightedMode weightedMode = (WeightedMode)reader.ReadInt32();
                float inWeight = reader.ReadSingle();
                float outWeight = reader.ReadSingle();
                Keyframe key = new(time, value, inTangent, outTangent, inWeight, outWeight)
                {
                    weightedMode = weightedMode
                };
                keys[i] = key;
            }

            WrapMode preWrapMode = (WrapMode)reader.ReadInt32();
            WrapMode postWrapMode = (WrapMode)reader.ReadInt32();
            AnimationCurve curve = new(keys)
            {
                preWrapMode = preWrapMode,
                postWrapMode = postWrapMode
            };
            return curve;
        }

        void IBinaryConverter.Write(BinaryWriter writer, object value)
        {
            Write(writer, (AnimationCurve)value);
        }

        object IBinaryConverter.Read(BinaryReader reader)
        {
            return Read(reader);
        }
    }

// Gradient Converter
    public class GradientBinaryConverter : IBinaryConverter<Gradient>
    {
        public void Write(BinaryWriter writer, Gradient value)
        {
            writer.Write((int)value.mode);

            GradientColorKey[] colorKeys = value.colorKeys;
            writer.Write(colorKeys.Length);
            foreach (GradientColorKey ck in colorKeys)
            {
                writer.Write(ck.time);
                writer.Write(ck.color.r);
                writer.Write(ck.color.g);
                writer.Write(ck.color.b);
                writer.Write(ck.color.a);
            }

            GradientAlphaKey[] alphaKeys = value.alphaKeys;
            writer.Write(alphaKeys.Length);
            foreach (GradientAlphaKey ak in alphaKeys)
            {
                writer.Write(ak.time);
                writer.Write(ak.alpha);
            }
        }

        public Gradient Read(BinaryReader reader)
        {
            Gradient gradient = new()
            {
                mode = (GradientMode)reader.ReadInt32()
            };

            int colorKeyCount = reader.ReadInt32();
            GradientColorKey[] colorKeys = new GradientColorKey[colorKeyCount];
            for (int i = 0; i < colorKeyCount; i++)
            {
                float time = reader.ReadSingle();
                float r = reader.ReadSingle();
                float g = reader.ReadSingle();
                float b = reader.ReadSingle();
                float a = reader.ReadSingle();
                Color color = new(r, g, b, a);
                colorKeys[i] = new GradientColorKey(color, time);
            }

            int alphaKeyCount = reader.ReadInt32();
            GradientAlphaKey[] alphaKeys = new GradientAlphaKey[alphaKeyCount];
            for (int i = 0; i < alphaKeyCount; i++)
            {
                float time = reader.ReadSingle();
                float alpha = reader.ReadSingle();
                alphaKeys[i] = new GradientAlphaKey(alpha, time);
            }

            gradient.SetKeys(colorKeys, alphaKeys);

            return gradient;
        }

        void IBinaryConverter.Write(BinaryWriter writer, object value)
        {
            Write(writer, (Gradient)value);
        }

        object IBinaryConverter.Read(BinaryReader reader)
        {
            return Read(reader);
        }
    }

// RectOffset Converter
    public class RectOffsetBinaryConverter : IBinaryConverter<RectOffset>
    {
        public void Write(BinaryWriter writer, RectOffset value)
        {
            writer.Write(value.left);
            writer.Write(value.right);
            writer.Write(value.top);
            writer.Write(value.bottom);
        }

        public RectOffset Read(BinaryReader reader)
        {
            int left = reader.ReadInt32();
            int right = reader.ReadInt32();
            int top = reader.ReadInt32();
            int bottom = reader.ReadInt32();
            return new RectOffset(left, right, top, bottom);
        }

        void IBinaryConverter.Write(BinaryWriter writer, object value)
        {
            Write(writer, (RectOffset)value);
        }

        object IBinaryConverter.Read(BinaryReader reader)
        {
            return Read(reader);
        }
    }

// Matrix4x4 Converter
    public class Matrix4x4BinaryConverter : IBinaryConverter<Matrix4x4>
    {
        public void Write(BinaryWriter writer, Matrix4x4 value)
        {
            for (int i = 0; i < 16; i++) writer.Write(value[i]);
        }

        public Matrix4x4 Read(BinaryReader reader)
        {
            Matrix4x4 matrix = new();
            for (int i = 0; i < 16; i++) matrix[i] = reader.ReadSingle();
            return matrix;
        }

        void IBinaryConverter.Write(BinaryWriter writer, object value)
        {
            Write(writer, (Matrix4x4)value);
        }

        object IBinaryConverter.Read(BinaryReader reader)
        {
            return Read(reader);
        }
    }

// GUIStyle Converter
    public class GUIStyleBinaryConverter : IBinaryConverter<GUIStyle>
    {
        public void Write(BinaryWriter writer, GUIStyle value)
        {
            // Note: GUIStyle is complex and may not be fully serializable.
            // Here, we serialize only the name. Extend as needed.
            writer.Write(value.name);
        }

        public GUIStyle Read(BinaryReader reader)
        {
            GUIStyle style = new()
            {
                name = reader.ReadString()
            };
            return style;
        }

        void IBinaryConverter.Write(BinaryWriter writer, object value)
        {
            Write(writer, (GUIStyle)value);
        }

        object IBinaryConverter.Read(BinaryReader reader)
        {
            return Read(reader);
        }
    }

// RangeInt Converter
    public class RangeIntBinaryConverter : IBinaryConverter<RangeInt>
    {
        public void Write(BinaryWriter writer, RangeInt value)
        {
            writer.Write(value.start);
            writer.Write(value.length);
        }

        public RangeInt Read(BinaryReader reader)
        {
            int start = reader.ReadInt32();
            int length = reader.ReadInt32();
            return new RangeInt(start, length);
        }

        void IBinaryConverter.Write(BinaryWriter writer, object value)
        {
            Write(writer, (RangeInt)value);
        }

        object IBinaryConverter.Read(BinaryReader reader)
        {
            return Read(reader);
        }
    }

// RectInt Converter
    public class RectIntBinaryConverter : IBinaryConverter<RectInt>
    {
        public void Write(BinaryWriter writer, RectInt value)
        {
            writer.Write(value.x);
            writer.Write(value.y);
            writer.Write(value.width);
            writer.Write(value.height);
        }

        public RectInt Read(BinaryReader reader)
        {
            int x = reader.ReadInt32();
            int y = reader.ReadInt32();
            int width = reader.ReadInt32();
            int height = reader.ReadInt32();
            return new RectInt(x, y, width, height);
        }

        void IBinaryConverter.Write(BinaryWriter writer, object value)
        {
            Write(writer, (RectInt)value);
        }

        object IBinaryConverter.Read(BinaryReader reader)
        {
            return Read(reader);
        }
    }

// BoundsInt Converter
    public class BoundsIntBinaryConverter : IBinaryConverter<BoundsInt>
    {
        public void Write(BinaryWriter writer, BoundsInt value)
        {
            writer.Write(value.position.x);
            writer.Write(value.position.y);
            writer.Write(value.position.z);
            writer.Write(value.size.x);
            writer.Write(value.size.y);
            writer.Write(value.size.z);
        }

        public BoundsInt Read(BinaryReader reader)
        {
            Vector3Int position = new(
                reader.ReadInt32(),
                reader.ReadInt32(),
                reader.ReadInt32()
            );
            Vector3Int size = new(
                reader.ReadInt32(),
                reader.ReadInt32(),
                reader.ReadInt32()
            );
            return new BoundsInt(position, size);
        }

        void IBinaryConverter.Write(BinaryWriter writer, object value)
        {
            Write(writer, (BoundsInt)value);
        }

        object IBinaryConverter.Read(BinaryReader reader)
        {
            return Read(reader);
        }
    }

// Hash128 Converter
    public class Hash128BinaryConverter : IBinaryConverter<Hash128>
    {
        public void Write(BinaryWriter writer, Hash128 value)
        {
            writer.Write(value.ToString());
        }

        public Hash128 Read(BinaryReader reader)
        {
            string hashString = reader.ReadString();
            return Hash128.Parse(hashString);
        }

        void IBinaryConverter.Write(BinaryWriter writer, object value)
        {
            Write(writer, (Hash128)value);
        }

        object IBinaryConverter.Read(BinaryReader reader)
        {
            return Read(reader);
        }
    }
}