using System;
using UnityEngine;

namespace LegendaryTools
{
    public class LookAt : MonoBehaviour
    {
        public enum LookAxis
        {
            Up,
            Down,
            Left,
            Right,
            Forward,
            Back
        }

        [Flags]
        public enum LookConstraint
        {
            X = 1,
            Y = 2,
            Z = 4
        }

        public LookAxis Axis = LookAxis.Up;
        public LookConstraint Constraint;

        public bool ReverseFace;
        public Transform Target;

        private Quaternion targetRotation;
        private Transform Transform;
        public bool UpdateCycle;
        private Vector3 vector3Buffer;

        // return a direction based upon chosen axis
        public Vector3 GetAxis(LookAxis refAxis)
        {
            switch (refAxis)
            {
                case LookAxis.Down:
                    return Vector3.down;
                case LookAxis.Forward:
                    return Vector3.forward;
                case LookAxis.Back:
                    return Vector3.back;
                case LookAxis.Left:
                    return Vector3.left;
                case LookAxis.Right:
                    return Vector3.right;
            }

            // default is Vector3.up
            return Vector3.up;
        }

        private void Awake()
        {
            Transform = transform;
            Update();
        }

        private void Update()
        {
            if (UpdateCycle)
            {
                targetRotation =
                    Quaternion.LookRotation(
                        ReverseFace ? Transform.position - Target.position : Target.position - Transform.position,
                        GetAxis(Axis));

                vector3Buffer.Set(
                    FlagUtil.Has((int) Constraint, (int) LookConstraint.X)
                        ? Transform.rotation.eulerAngles.x
                        : targetRotation.eulerAngles.x,
                    FlagUtil.Has((int) Constraint, (int) LookConstraint.Y)
                        ? Transform.rotation.eulerAngles.y
                        : targetRotation.eulerAngles.y,
                    FlagUtil.Has((int) Constraint, (int) LookConstraint.Z)
                        ? Transform.rotation.eulerAngles.z
                        : targetRotation.eulerAngles.z);

                Transform.rotation = Quaternion.Euler(vector3Buffer);
            }
        }
    }
}