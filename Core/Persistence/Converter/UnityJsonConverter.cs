using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace LegendaryTools.Persistence
{
    public class Vector3Converter : JsonConverter<Vector3>
    {
        public override void WriteJson(JsonWriter writer, Vector3 value, JsonSerializer serializer)
        {
            JObject jo = new()
            {
                { "x", value.x },
                { "y", value.y },
                { "z", value.z }
            };
            jo.WriteTo(writer);
        }

        public override Vector3 ReadJson(JsonReader reader, Type objectType, Vector3 existingValue,
            bool hasExistingValue, JsonSerializer serializer)
        {
            JObject jo = JObject.Load(reader);
            float x = (float)jo["x"];
            float y = (float)jo["y"];
            float z = (float)jo["z"];
            return new Vector3(x, y, z);
        }
    }

    public class Vector2Converter : JsonConverter<Vector2>
    {
        public override void WriteJson(JsonWriter writer, Vector2 value, JsonSerializer serializer)
        {
            JObject jo = new()
            {
                { "x", value.x },
                { "y", value.y }
            };
            jo.WriteTo(writer);
        }

        public override Vector2 ReadJson(JsonReader reader, Type objectType, Vector2 existingValue,
            bool hasExistingValue, JsonSerializer serializer)
        {
            JObject jo = JObject.Load(reader);
            float x = (float)jo["x"];
            float y = (float)jo["y"];
            return new Vector2(x, y);
        }
    }

    public class QuaternionConverter : JsonConverter<Quaternion>
    {
        public override void WriteJson(JsonWriter writer, Quaternion value, JsonSerializer serializer)
        {
            JObject jo = new()
            {
                { "x", value.x },
                { "y", value.y },
                { "z", value.z },
                { "w", value.w }
            };
            jo.WriteTo(writer);
        }

        public override Quaternion ReadJson(JsonReader reader, Type objectType, Quaternion existingValue,
            bool hasExistingValue, JsonSerializer serializer)
        {
            JObject jo = JObject.Load(reader);
            float x = (float)jo["x"];
            float y = (float)jo["y"];
            float z = (float)jo["z"];
            float w = (float)jo["w"];
            return new Quaternion(x, y, z, w);
        }
    }

    public class ColorConverter : JsonConverter<Color>
    {
        public override void WriteJson(JsonWriter writer, Color value, JsonSerializer serializer)
        {
            JObject jo = new()
            {
                { "r", value.r },
                { "g", value.g },
                { "b", value.b },
                { "a", value.a }
            };
            jo.WriteTo(writer);
        }

        public override Color ReadJson(JsonReader reader, Type objectType, Color existingValue, bool hasExistingValue,
            JsonSerializer serializer)
        {
            JObject jo = JObject.Load(reader);
            float r = (float)jo["r"];
            float g = (float)jo["g"];
            float b = (float)jo["b"];
            float a = (float)jo["a"];
            return new Color(r, g, b, a);
        }
    }

    public class Color32Converter : JsonConverter<Color32>
    {
        public override void WriteJson(JsonWriter writer, Color32 value, JsonSerializer serializer)
        {
            JObject jo = new()
            {
                { "r", value.r },
                { "g", value.g },
                { "b", value.b },
                { "a", value.a }
            };
            jo.WriteTo(writer);
        }

        public override Color32 ReadJson(JsonReader reader, Type objectType, Color32 existingValue,
            bool hasExistingValue, JsonSerializer serializer)
        {
            JObject jo = JObject.Load(reader);
            byte r = (byte)jo["r"];
            byte g = (byte)jo["g"];
            byte b = (byte)jo["b"];
            byte a = (byte)jo["a"];
            return new Color32(r, g, b, a);
        }
    }

    public class Vector3IntConverter : JsonConverter<Vector3Int>
    {
        public override void WriteJson(JsonWriter writer, Vector3Int value, JsonSerializer serializer)
        {
            JObject jo = new()
            {
                { "x", value.x },
                { "y", value.y },
                { "z", value.z }
            };
            jo.WriteTo(writer);
        }

        public override Vector3Int ReadJson(JsonReader reader, Type objectType, Vector3Int existingValue,
            bool hasExistingValue, JsonSerializer serializer)
        {
            JObject jo = JObject.Load(reader);
            int x = (int)jo["x"];
            int y = (int)jo["y"];
            int z = (int)jo["z"];
            return new Vector3Int(x, y, z);
        }
    }

    public class Vector2IntConverter : JsonConverter<Vector2Int>
    {
        public override void WriteJson(JsonWriter writer, Vector2Int value, JsonSerializer serializer)
        {
            JObject jo = new()
            {
                { "x", value.x },
                { "y", value.y }
            };
            jo.WriteTo(writer);
        }

        public override Vector2Int ReadJson(JsonReader reader, Type objectType, Vector2Int existingValue,
            bool hasExistingValue, JsonSerializer serializer)
        {
            JObject jo = JObject.Load(reader);
            int x = (int)jo["x"];
            int y = (int)jo["y"];
            return new Vector2Int(x, y);
        }
    }

    public class RectConverter : JsonConverter<Rect>
    {
        public override void WriteJson(JsonWriter writer, Rect value, JsonSerializer serializer)
        {
            JObject jo = new()
            {
                { "x", value.x },
                { "y", value.y },
                { "width", value.width },
                { "height", value.height }
            };
            jo.WriteTo(writer);
        }

        public override Rect ReadJson(JsonReader reader, Type objectType, Rect existingValue, bool hasExistingValue,
            JsonSerializer serializer)
        {
            JObject jo = JObject.Load(reader);
            float x = (float)jo["x"];
            float y = (float)jo["y"];
            float width = (float)jo["width"];
            float height = (float)jo["height"];
            return new Rect(x, y, width, height);
        }
    }

    public class Vector4Converter : JsonConverter<Vector4>
    {
        public override void WriteJson(JsonWriter writer, Vector4 value, JsonSerializer serializer)
        {
            JObject jo = new()
            {
                { "x", value.x },
                { "y", value.y },
                { "z", value.z },
                { "w", value.w }
            };
            jo.WriteTo(writer);
        }

        public override Vector4 ReadJson(JsonReader reader, Type objectType, Vector4 existingValue,
            bool hasExistingValue, JsonSerializer serializer)
        {
            JObject jo = JObject.Load(reader);
            float x = (float)jo["x"];
            float y = (float)jo["y"];
            float z = (float)jo["z"];
            float w = (float)jo["w"];
            return new Vector4(x, y, z, w);
        }
    }

    public class BoundsConverter : JsonConverter<Bounds>
    {
        public override void WriteJson(JsonWriter writer, Bounds value, JsonSerializer serializer)
        {
            JObject jo = new()
            {
                { "center", JToken.FromObject(value.center, serializer) },
                { "size", JToken.FromObject(value.size, serializer) }
            };
            jo.WriteTo(writer);
        }

        public override Bounds ReadJson(JsonReader reader, Type objectType, Bounds existingValue, bool hasExistingValue,
            JsonSerializer serializer)
        {
            JObject jo = JObject.Load(reader);
            Vector3 center = jo["center"].ToObject<Vector3>(serializer);
            Vector3 size = jo["size"].ToObject<Vector3>(serializer);
            return new Bounds(center, size);
        }
    }

    public class LayerMaskConverter : JsonConverter<LayerMask>
    {
        public override void WriteJson(JsonWriter writer, LayerMask value, JsonSerializer serializer)
        {
            writer.WriteValue(value.value);
        }

        public override LayerMask ReadJson(JsonReader reader, Type objectType, LayerMask existingValue,
            bool hasExistingValue, JsonSerializer serializer)
        {
            int intValue = reader.Value != null ? Convert.ToInt32(reader.Value) : 0;
            LayerMask layerMask = new();
            layerMask.value = intValue;
            return layerMask;
        }
    }

    public class AnimationCurveConverter : JsonConverter<AnimationCurve>
    {
        public override void WriteJson(JsonWriter writer, AnimationCurve value, JsonSerializer serializer)
        {
            JObject jo = new()
            {
                { "keys", JToken.FromObject(value.keys, serializer) },
                { "preWrapMode", value.preWrapMode.ToString() },
                { "postWrapMode", value.postWrapMode.ToString() }
            };
            jo.WriteTo(writer);
        }

        public override AnimationCurve ReadJson(JsonReader reader, Type objectType, AnimationCurve existingValue,
            bool hasExistingValue, JsonSerializer serializer)
        {
            JObject jo = JObject.Load(reader);
            Keyframe[] keys = jo["keys"].ToObject<Keyframe[]>(serializer);
            WrapMode preWrapMode = (WrapMode)Enum.Parse(typeof(WrapMode), (string)jo["preWrapMode"]);
            WrapMode postWrapMode = (WrapMode)Enum.Parse(typeof(WrapMode), (string)jo["postWrapMode"]);

            AnimationCurve curve = new(keys)
            {
                preWrapMode = preWrapMode,
                postWrapMode = postWrapMode
            };

            return curve;
        }
    }

    public class GradientConverter : JsonConverter<Gradient>
    {
        public override void WriteJson(JsonWriter writer, Gradient value, JsonSerializer serializer)
        {
            JObject jo = new()
            {
                { "colorKeys", JToken.FromObject(value.colorKeys, serializer) },
                { "alphaKeys", JToken.FromObject(value.alphaKeys, serializer) },
                { "mode", value.mode.ToString() }
            };
            jo.WriteTo(writer);
        }

        public override Gradient ReadJson(JsonReader reader, Type objectType, Gradient existingValue,
            bool hasExistingValue, JsonSerializer serializer)
        {
            JObject jo = JObject.Load(reader);
            GradientColorKey[] colorKeys = jo["colorKeys"].ToObject<GradientColorKey[]>(serializer);
            GradientAlphaKey[] alphaKeys = jo["alphaKeys"].ToObject<GradientAlphaKey[]>(serializer);
            GradientMode mode = (GradientMode)Enum.Parse(typeof(GradientMode), (string)jo["mode"]);

            Gradient gradient = new()
            {
                colorKeys = colorKeys,
                alphaKeys = alphaKeys,
                mode = mode
            };

            return gradient;
        }
    }

    public class RectOffsetConverter : JsonConverter<RectOffset>
    {
        public override void WriteJson(JsonWriter writer, RectOffset value, JsonSerializer serializer)
        {
            JObject jo = new()
            {
                { "left", value.left },
                { "right", value.right },
                { "top", value.top },
                { "bottom", value.bottom }
            };
            jo.WriteTo(writer);
        }

        public override RectOffset ReadJson(JsonReader reader, Type objectType, RectOffset existingValue,
            bool hasExistingValue, JsonSerializer serializer)
        {
            JObject jo = JObject.Load(reader);
            int left = (int)jo["left"];
            int right = (int)jo["right"];
            int top = (int)jo["top"];
            int bottom = (int)jo["bottom"];
            return new RectOffset(left, right, top, bottom);
        }
    }

    public class Matrix4x4Converter : JsonConverter<Matrix4x4>
    {
        public override void WriteJson(JsonWriter writer, Matrix4x4 value, JsonSerializer serializer)
        {
            float[] elements =
            {
                value.m00, value.m01, value.m02, value.m03,
                value.m10, value.m11, value.m12, value.m13,
                value.m20, value.m21, value.m22, value.m23,
                value.m30, value.m31, value.m32, value.m33
            };
            JArray ja = new(elements);
            ja.WriteTo(writer);
        }

        public override Matrix4x4 ReadJson(JsonReader reader, Type objectType, Matrix4x4 existingValue,
            bool hasExistingValue, JsonSerializer serializer)
        {
            JArray ja = JArray.Load(reader);
            float[] elements = ja.ToObject<float[]>();
            Matrix4x4 matrix = new()
            {
                m00 = elements[0], m01 = elements[1], m02 = elements[2], m03 = elements[3],
                m10 = elements[4], m11 = elements[5], m12 = elements[6], m13 = elements[7],
                m20 = elements[8], m21 = elements[9], m22 = elements[10], m23 = elements[11],
                m30 = elements[12], m31 = elements[13], m32 = elements[14], m33 = elements[15]
            };
            return matrix;
        }
    }

    public class RangeIntConverter : JsonConverter<RangeInt>
    {
        public override void WriteJson(JsonWriter writer, RangeInt value, JsonSerializer serializer)
        {
            JObject jo = new()
            {
                { "start", value.start },
                { "length", value.length }
            };
            jo.WriteTo(writer);
        }

        public override RangeInt ReadJson(JsonReader reader, Type objectType, RangeInt existingValue,
            bool hasExistingValue, JsonSerializer serializer)
        {
            JObject jo = JObject.Load(reader);
            int start = (int)jo["start"];
            int length = (int)jo["length"];
            return new RangeInt(start, length);
        }
    }

    public class RectIntConverter : JsonConverter<RectInt>
    {
        public override void WriteJson(JsonWriter writer, RectInt value, JsonSerializer serializer)
        {
            JObject jo = new()
            {
                { "x", value.x },
                { "y", value.y },
                { "width", value.width },
                { "height", value.height }
            };
            jo.WriteTo(writer);
        }

        public override RectInt ReadJson(JsonReader reader, Type objectType, RectInt existingValue,
            bool hasExistingValue, JsonSerializer serializer)
        {
            JObject jo = JObject.Load(reader);
            int x = (int)jo["x"];
            int y = (int)jo["y"];
            int width = (int)jo["width"];
            int height = (int)jo["height"];
            return new RectInt(x, y, width, height);
        }
    }

    public class BoundsIntConverter : JsonConverter<BoundsInt>
    {
        public override void WriteJson(JsonWriter writer, BoundsInt value, JsonSerializer serializer)
        {
            JObject jo = new()
            {
                { "position", JToken.FromObject(value.position, serializer) },
                { "size", JToken.FromObject(value.size, serializer) }
            };
            jo.WriteTo(writer);
        }

        public override BoundsInt ReadJson(JsonReader reader, Type objectType, BoundsInt existingValue,
            bool hasExistingValue, JsonSerializer serializer)
        {
            JObject jo = JObject.Load(reader);
            Vector3Int position = jo["position"].ToObject<Vector3Int>(serializer);
            Vector3Int size = jo["size"].ToObject<Vector3Int>(serializer);
            return new BoundsInt(position, size);
        }
    }

    public class Hash128Converter : JsonConverter<Hash128>
    {
        public override void WriteJson(JsonWriter writer, Hash128 value, JsonSerializer serializer)
        {
            writer.WriteValue(value.ToString());
        }

        public override Hash128 ReadJson(JsonReader reader, Type objectType, Hash128 existingValue,
            bool hasExistingValue, JsonSerializer serializer)
        {
            string hashString = (string)reader.Value;
            return Hash128.Parse(hashString);
        }
    }
}