#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace LegendaryTools.SOAP.Editor
{
    /// <summary>
    /// Utilities for copying values between SerializedProperty instances (supports arrays, lists and nested objects).
    /// </summary>
    public static class SerializedPropertyUtilities
    {
        /// <summary>
        /// Copies the value from src into dst. Works for most Unity-serializable property types.
        /// </summary>
        public static void CopyValue(SerializedProperty src, SerializedProperty dst)
        {
            if (src == null || dst == null)
                return;

            if (src.propertyType != dst.propertyType && src.propertyType != SerializedPropertyType.Generic)
                return;

            switch (src.propertyType)
            {
                case SerializedPropertyType.Integer: dst.intValue = src.intValue; break;
                case SerializedPropertyType.Boolean: dst.boolValue = src.boolValue; break;
                case SerializedPropertyType.Float: dst.floatValue = src.floatValue; break;
                case SerializedPropertyType.String: dst.stringValue = src.stringValue; break;
                case SerializedPropertyType.Color: dst.colorValue = src.colorValue; break;
                case SerializedPropertyType.ObjectReference: dst.objectReferenceValue = src.objectReferenceValue; break;
                case SerializedPropertyType.LayerMask: dst.intValue = src.intValue; break;
                case SerializedPropertyType.Enum: dst.enumValueIndex = src.enumValueIndex; break;
                case SerializedPropertyType.Vector2: dst.vector2Value = src.vector2Value; break;
                case SerializedPropertyType.Vector3: dst.vector3Value = src.vector3Value; break;
                case SerializedPropertyType.Vector4: dst.vector4Value = src.vector4Value; break;
                case SerializedPropertyType.Rect: dst.rectValue = src.rectValue; break;
                case SerializedPropertyType.Bounds: dst.boundsValue = src.boundsValue; break;
                case SerializedPropertyType.Quaternion: dst.quaternionValue = src.quaternionValue; break;
                case SerializedPropertyType.AnimationCurve: dst.animationCurveValue = src.animationCurveValue; break;
                case SerializedPropertyType.ExposedReference:
                    dst.exposedReferenceValue = src.exposedReferenceValue; break;
                case SerializedPropertyType.Vector2Int: dst.vector2IntValue = src.vector2IntValue; break;
                case SerializedPropertyType.Vector3Int: dst.vector3IntValue = src.vector3IntValue; break;
                case SerializedPropertyType.RectInt: dst.rectIntValue = src.rectIntValue; break;
                case SerializedPropertyType.BoundsInt: dst.boundsIntValue = src.boundsIntValue; break;
                case SerializedPropertyType.ManagedReference:
                    dst.managedReferenceValue = src.managedReferenceValue; break;

                case SerializedPropertyType.Generic:
                default:
                {
                    // Generic covers arrays, lists, and nested structs/classes.
                    SerializedProperty srcIter = src.Copy();
                    SerializedProperty dstIter = dst.Copy();
                    SerializedProperty endSrc = src.GetEndProperty();
                    SerializedProperty endDst = dst.GetEndProperty();

                    bool enterChildrenSrc = true;
                    bool enterChildrenDst = true;
                    while (srcIter.NextVisible(enterChildrenSrc) && dstIter.NextVisible(enterChildrenDst))
                    {
                        if (SerializedProperty.EqualContents(srcIter, endSrc)) break;
                        if (SerializedProperty.EqualContents(dstIter, endDst)) break;

                        CopyValue(srcIter, dstIter);
                        enterChildrenSrc = false;
                        enterChildrenDst = false;
                    }

                    break;
                }
            }
        }
    }
}
#endif