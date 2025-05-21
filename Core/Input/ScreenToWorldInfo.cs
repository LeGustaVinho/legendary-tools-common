using System;
using UnityEngine;

namespace LegendaryTools
{
    public class ScreenToWorldInfo : MonoBehaviour
    {
        public enum EventTriggerType
        {
            PointerMove,
            PointerUp,
            PointerDown
        }

        public enum Mode
        {
            Physics3D,
            Physics2D
        }

        public Camera Camera;
        public bool CanInput = true;
        public LayerMask CullingMask;
        public Mode RayCastMode;
        public bool ShowDebug = true;
        
        public EventTriggerType EventTrigger;
        public KeyCode TriggerKey = KeyCode.Mouse0;
        
        [HideInInspector] public RaycastHit HitInfo;
        [HideInInspector] public RaycastHit2D HitInfo2D;
        public bool HasSomething;
        public Transform Transform => RayCastMode == Mode.Physics3D ? HitInfo.transform : HitInfo2D.transform;
        public float Distance => RayCastMode == Mode.Physics3D ? HitInfo.distance : HitInfo2D.distance;
        public Vector3 Position => RayCastMode == Mode.Physics3D ? HitInfo.point : (Vector3) HitInfo2D.point;
        public Vector3 Normal => RayCastMode == Mode.Physics3D ? HitInfo.normal : (Vector3) HitInfo2D.normal;

        public event Action<RaycastHit> On3DHit;
        public event Action<RaycastHit2D> On2DHit;
        
        private void Update()
        {
            if (CanInput)
            {
                switch (EventTrigger)
                {
                    case EventTriggerType.PointerMove:
#if UNITY_ANDROID || UNITY_IPHONE
                        foreach (Touch touch in UnityEngine.Input.touches)
                            LaunchRay(touch.position);
#endif

#if UNITY_EDITOR || UNITY_STANDALONE || UNITY_WEBPLAYER
                        LaunchRay(UnityEngine.Input.mousePosition);
#endif
                        break;
                    case EventTriggerType.PointerUp:

#if UNITY_ANDROID || UNITY_IPHONE
                        foreach (Touch touch in UnityEngine.Input.touches)
                        {
                            if(touch.phase == TouchPhase.Began)
                                LaunchRay(touch.position);
                        }
#endif

#if UNITY_EDITOR || UNITY_STANDALONE || UNITY_WEBPLAYER
                        if (UnityEngine.Input.GetKeyUp(TriggerKey))
                        {
                            LaunchRay(UnityEngine.Input.mousePosition);
                        }
#endif
                        break;
                    case EventTriggerType.PointerDown:
#if UNITY_ANDROID || UNITY_IPHONE
                        foreach (Touch touch in UnityEngine.Input.touches)
                        {
                            if (touch.phase == TouchPhase.Ended)
                                LaunchRay(touch.position);
                        }
#endif

#if UNITY_EDITOR || UNITY_STANDALONE || UNITY_WEBPLAYER
                        if (UnityEngine.Input.GetKeyDown(TriggerKey))
                        {
                            LaunchRay(UnityEngine.Input.mousePosition);
                        }
#endif
                        break;
                }
            }
        }

        public void LaunchRay(Vector3 position)
        {
            Ray ray = Camera.ScreenPointToRay(position);

            if (ShowDebug)
            {
                Debug.DrawRay(ray.origin, ray.direction * 100, Color.blue);
            }

            if (RayCastMode == Mode.Physics3D)
            {
                if (Physics.Raycast(ray, out HitInfo, Mathf.Infinity, CullingMask.value))
                {
                    if (ShowDebug)
                    {
                        Debug.DrawLine(transform.position, HitInfo.point, Color.red, 1);
                    }

                    HasSomething = true;

                    if (On3DHit != null)
                    {
                        On3DHit.Invoke(HitInfo);
                    }
                }
                else
                {
                    HasSomething = false;
                }
            }
            else
            {
                HitInfo2D = Physics2D.Raycast(ray.origin, ray.direction, Mathf.Infinity, CullingMask);

                if (HitInfo2D.collider != null)
                {
                    if (ShowDebug)
                    {
                        Debug.DrawLine(transform.position, HitInfo.point, Color.red, 1);
                    }

                    HasSomething = true;

                    if (On2DHit != null)
                    {
                        On2DHit.Invoke(HitInfo2D);
                    }
                }
                else
                {
                    HasSomething = false;
                }
            }
        }
    }
}