using UnityEngine;

namespace LegendaryTools
{
    [ExecuteInEditMode]
    public class FollowTransform : MonoBehaviour
    {
        public enum FollowTransformLoopMode
        {
            Update,
            LateUpdate
        }

        public float CurrentAngle;
        public float CurrentDistance;
        public float DeltaAngleThreshold;
        public float DeltaPositionThreshold;

        public bool FollowPosition;

        public bool FollowRotation;

        private Vector3 lastTargetPosition;
        private Quaternion lastTargetRotation;
        public FollowTransformLoopMode LoopMode;
        public Vector3 PositionOffset;
        public Space PositionOffsetRelativeTo;
        public Vector3 RotationOffset;
        public Space RotationOffsetRelativeTo;
        public bool SmoothPosition;
        public float SmoothPositionFactor = 1;
        public bool SmoothRotation;
        public float SmoothRotationFactor = 1;

        public Transform Target;

        [HideInInspector] public Transform Transform;

        private void Awake()
        {
            Init();
            Follow();
        }

        private void Init()
        {
            Transform = transform;

            if (Target != null)
            {
                lastTargetPosition = Target.position + PositionOffset;
                lastTargetRotation = Target.rotation;
            }
        }

        public void Follow()
        {
            if (Target == null || Transform == null)
            {
                Init();
            }

            if (Target != null)
            {
                if (FollowPosition)
                {
                    CurrentDistance = Vector3.Distance(Transform.position, Target.position);
                    if (CurrentDistance >= DeltaPositionThreshold - PositionOffset.magnitude)
                    {
                        lastTargetPosition = Target.position;
                    }

                    if (SmoothPosition)
                    {
                        Transform.position = Vector3.Lerp(Transform.position, lastTargetPosition,
                            SmoothPositionFactor * Time.deltaTime);
                    }
                    else
                    {
                        Transform.position = lastTargetPosition;
                    }

                    Transform.Translate(PositionOffset, PositionOffsetRelativeTo);
                }

                if (FollowRotation)
                {
                    CurrentAngle = Quaternion.Angle(Transform.rotation, Target.rotation);
                    if (CurrentAngle >= DeltaAngleThreshold)
                    {
                        lastTargetRotation = Target.rotation;
                    }

                    if (SmoothRotation)
                    {
                        Transform.rotation = Quaternion.Lerp(Transform.rotation, lastTargetRotation,
                            SmoothRotationFactor * Time.deltaTime);
                    }
                    else
                    {
                        Transform.rotation = lastTargetRotation;
                    }

                    Transform.Rotate(RotationOffset, RotationOffsetRelativeTo);
                }
            }
        }

        private void Update()
        {
            if (LoopMode == FollowTransformLoopMode.Update)
            {
                Follow();
            }
        }

        private void LateUpdate()
        {
            if (LoopMode == FollowTransformLoopMode.LateUpdate)
            {
                Follow();
            }
        }

        private void Reset()
        {
            Init();
        }
    }
}