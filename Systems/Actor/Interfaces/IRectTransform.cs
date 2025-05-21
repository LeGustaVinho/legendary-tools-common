using UnityEngine;

namespace LegendaryTools.Actor
{
    public interface IRectTransform : ITransform
    {
        Vector2 AnchoredPosition{ get; set; }
        Vector3 AnchoredPosition3D{ get; set; }
        Vector2 AnchorMax{ get; set; }
        Vector2 AnchorMin{ get; set; }
        Vector2 OffsetMax{ get; set; }
        Vector2 OffsetMin{ get; set; }
        Vector2 Pivot{ get; set; }
        Rect Rect{ get; }
        Vector2 SizeDelta{ get; set; }
        
        void ForceUpdateRectTransforms();
        void GetLocalCorners(Vector3[] fourCornersArray);
        void GetWorldCorners(Vector3[] fourCornersArray);
        void SetInsetAndSizeFromParentEdge(RectTransform.Edge edge, float inset, float size);
        void SetSizeWithCurrentAnchors(RectTransform.Axis axis, float size);
    }
}