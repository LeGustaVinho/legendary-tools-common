using UnityEngine;

namespace LegendaryTools.AttributeSystem.HexGrid
{
    public struct Layout
    {
        public enum WorldPlane
        {
            XY,
            XZ
        }

        public enum OrientationType
        {
            Pointy,
            Flat,
        }

        public readonly WorldPlane Plane;
        public readonly Orientation Orientation;
        public readonly Vector3 Size;
        public readonly Vector3 Origin;

        public static readonly Orientation Pointy = new Orientation(Mathf.Sqrt(3.0f), Mathf.Sqrt(3.0f) / 2.0f, 0.0f,
            3.0f / 2.0f, Mathf.Sqrt(3.0f) / 3.0f, -1.0f / 3.0f, 0.0f, 2.0f / 3.0f, 0.5f);

        public static readonly Orientation Flat = new Orientation(3.0f / 2.0f, 0.0f, Mathf.Sqrt(3.0f) / 2.0f,
            Mathf.Sqrt(3.0f), 2.0f / 3.0f, 0.0f, -1.0f / 3.0f, Mathf.Sqrt(3.0f) / 3.0f, 0.0f);

        public Layout(WorldPlane plane, OrientationType orientationType, Vector3 size, Vector3 origin)
        {
            Plane = plane;
            Orientation = orientationType == OrientationType.Flat ? Flat : Pointy;
            Size = size;
            Origin = Plane == WorldPlane.XZ ? new Vector3(origin.x, origin.z, 0) : origin;
        }

        public Vector2 HexToPixel(Hex h)
        {
            float x = Origin.x + (Orientation.F0 * h.Q + Orientation.F1 * h.R) * Size.x;
            float y = Origin.y + (Orientation.F2 * h.Q + Orientation.F3 * h.R) * Size.y;

            return new Vector2(x, y);
        }
        
        public Vector3 HexToPosition(Hex h, float elevation)
        {
            Vector2 pixelCoords = HexToPixel(h);
            switch (Plane)
            {
                case WorldPlane.XY: return new Vector3(pixelCoords.x, pixelCoords.y, elevation);
                case WorldPlane.XZ: return new Vector3(pixelCoords.x, elevation, pixelCoords.y);
            }

            return Vector3.zero;
        }

        public Hex PixelToHex(Vector3 pixelPosition)
        {
            if (Plane == WorldPlane.XZ)
            {
                pixelPosition.y = pixelPosition.z;
                pixelPosition.z = 0;
            }
            
            float x = (pixelPosition.x - Origin.x) / Size.x;
            float y = (pixelPosition.y - Origin.y) / Size.y;

            Vector3 pt = new Vector3(x, y, 0);

            float q = Orientation.B0 * pt.x + Orientation.B1 * pt.y;
            float r = Orientation.B2 * pt.x + Orientation.B3 * pt.y;

            return new FractionalHex(q, r, -q - r).Round();
        }

        public Vector2 HexCornerOffset(int corner)
        {
            float angle = 2.0f * Mathf.PI * (Orientation.StartAngle - corner) / 6.0f;

            float x = Size.x * Mathf.Cos(angle);
            float y = Size.y * Mathf.Sin(angle);

            return new Vector2(x, y);
        }

        public Vector3[] PolygonCorners(Hex h)
        {
            Vector3[] corners = new Vector3[6];
            Vector2 center = HexToPixel(h);

            for (int i = 0; i < 6; i++)
            {
                Vector2 offset = HexCornerOffset(i);
                float x = center.x + offset.x;
                float y = center.y + offset.y;

                corners[i] = coordForPlane(Plane, x, y);
            }

            return corners;
        }

        private Vector3 coordForPlane(WorldPlane plane, float x, float y)
        {
            switch (plane)
            {
                case WorldPlane.XY: return new Vector3(x, y, 0);
                case WorldPlane.XZ: return new Vector3(x, 0, y);
            }

            return Vector3.zero;
        }
    }
}