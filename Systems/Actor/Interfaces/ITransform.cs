using UnityEngine;

namespace LegendaryTools.Actor
{
    public interface ITransform : IComponent
    {
        int ChildCount { get; }
        Vector3 EulerAngles { get; set; }
        Vector3 Forward { get; set; }
        bool HasChanged { get; set; }
        int HierarchyCapacity { get; }
        int HierarchyCount { get; }
        Vector3 LocalEulerAngles { get; set; }
        Vector3 LocalPosition { get; set; }
        Quaternion LocalRotation { get; set; }
        Vector3 LocalScale { get; set; }
        Matrix4x4 LocalToWorldMatrix { get; }
        Vector3 LossyScale { get; }
        Transform Parent { get; set; }
        Vector3 Position { get; set; }
        Vector3 Right { get; set; }
        Transform Root { get; }
        Quaternion Rotation { get; set; }
        Vector3 Up { get; set; }
        Matrix4x4 WorldToLocalMatrix { get; }

        void DetachChildren();
        Transform Find(string name);
        Transform GetChild(int index);
        int GetSiblingIndex();
        Vector3 InverseTransformDirection(Vector3 direction);
        Vector3 InverseTransformPoint(Vector3 position);
        Vector3 InverseTransformVector(Vector3 vector);
        bool IsChildOf(Transform parent);
        void LookAt(Transform target);
        void LookAt(Transform target, Vector3 worldUp);
        void Rotate(Vector3 eulers, Space relativeTo = Space.Self);
        void Rotate(float xAngle, float yAngle, float zAngle, Space relativeTo = Space.Self);
        void Rotate(Vector3 axis, float angle, Space relativeTo = Space.Self);
        void RotateAround(Vector3 point, Vector3 axis, float angle);
        void SetAsFirstSibling();
        void SetAsLastSibling();
        void SetParent(Transform parent);
        void SetParent(Transform parent, bool worldPositionStays);
        void SetPositionAndRotation(Vector3 position, Quaternion rotation);
        void SetSiblingIndex(int index);
        Vector3 TransformDirection(Vector3 direction);
        Vector3 TransformPoint(Vector3 position);
        Vector3 TransformVector(Vector3 vector);
        void Translate(Vector3 translation);
        void Translate(Vector3 translation, Space relativeTo = Space.Self);
    }
}