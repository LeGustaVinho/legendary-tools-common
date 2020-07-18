using UnityEngine;

namespace LegendaryTools
{
    public struct HSV
    {
        public float hue;
        public float saturation;
        public float value;

        public HSV(float hue, float saturation, float value)
        {
            this.hue = hue;
            this.saturation = saturation;
            this.value = value;
        }

        public HSV(Color color)
        {
            Color.RGBToHSV(color, out hue, out saturation, out value);
        }

        public static implicit operator Color(HSV hsv)
        {
            return Color.HSVToRGB(hsv.hue, hsv.saturation, hsv.value);
        }

        public static implicit operator HSV(Color color)
        {
            return new HSV(color);
        }

        public override bool Equals(object obj)
        {
            if (GetType() != obj.GetType())
            {
                return false;
            }

            HSV hsv = (HSV) obj;
            return hue == hsv.hue && saturation == hsv.saturation && value == hsv.value;
        }

        public override int GetHashCode()
        {
            return hue.GetHashCode() ^ saturation.GetHashCode() ^ value.GetHashCode();
        }

        public static bool operator ==(HSV x, HSV y)
        {
            return x.hue == y.hue && x.saturation == y.saturation && x.value == y.value;
        }

        public static bool operator !=(HSV x, HSV y)
        {
            return !(x == y);
        }

        public override string ToString()
        {
            return string.Format("({0}, {1}, {2})", hue, saturation, value);
        }
    }
}