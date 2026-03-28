using UnityEngine;

public static class CameraUtil
{
    /// <summary>
    /// Frames the full hierarchy bounds of the target into the camera view.
    /// The camera is placed on an orbit around the target and always looks at the focus point.
    /// </summary>
    /// <param name="camera">Target camera.</param>
    /// <param name="target">Target GameObject.</param>
    /// <param name="padding">Extra margin around the bounds. Use values >= 1.0.</param>
    /// <param name="orbitYaw">Horizontal orbit angle in degrees.</param>
    /// <param name="orbitPitch">Vertical orbit angle in degrees.</param>
    /// <param name="focusOffsetLocal">Optional local-space offset applied to the bounds center.</param>
    /// <param name="orbitRelativeToTarget">Whether the orbit direction should follow the target orientation.</param>
    /// <returns>True if bounds were found and applied; otherwise false.</returns>
    public static bool FrameGameObject(
        Camera camera,
        GameObject target,
        float padding = 1.1f,
        float orbitYaw = 0.0f,
        float orbitPitch = 15.0f,
        Vector3 focusOffsetLocal = default,
        bool orbitRelativeToTarget = true)
    {
        if (camera == null || target == null)
        {
            return false;
        }

        if (!TryGetHierarchyBounds(target, out Bounds bounds))
        {
            return false;
        }

        Vector3 focusPoint = bounds.center + target.transform.TransformDirection(focusOffsetLocal);
        Quaternion cameraRotation = GetOrbitRotation(target.transform, orbitYaw, orbitPitch, orbitRelativeToTarget);

        return FrameBounds(camera, bounds, focusPoint, cameraRotation, padding);
    }

    /// <summary>
    /// Frames the given bounds into the camera view using the provided focus point and rotation.
    /// </summary>
    /// <param name="camera">Target camera.</param>
    /// <param name="bounds">Bounds to frame.</param>
    /// <param name="focusPoint">Point that the camera will look at.</param>
    /// <param name="cameraRotation">Desired camera rotation.</param>
    /// <param name="padding">Extra margin around the bounds. Use values >= 1.0.</param>
    /// <returns>True if successful; otherwise false.</returns>
    public static bool FrameBounds(
        Camera camera,
        Bounds bounds,
        Vector3 focusPoint,
        Quaternion cameraRotation,
        float padding = 1.1f)
    {
        if (camera == null)
        {
            return false;
        }

        padding = Mathf.Max(1.0f, padding);

        Vector3 paddedExtents = bounds.extents * padding;
        Vector3[] paddedCorners = GetBoundsCorners(bounds.center, paddedExtents);

        if (camera.orthographic)
        {
            FrameOrthographicCamera(camera, bounds, paddedCorners, focusPoint, cameraRotation);
        }
        else
        {
            FramePerspectiveCamera(camera, bounds, paddedCorners, focusPoint, cameraRotation);
        }

        return true;
    }

    /// <summary>
    /// Tries to calculate a combined bounds using Renderers first, then Colliders as fallback.
    /// </summary>
    private static bool TryGetHierarchyBounds(GameObject target, out Bounds bounds)
    {
        bounds = new Bounds(target.transform.position, Vector3.zero);
        bool hasBounds = false;

        Renderer[] renderers = target.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            if (!hasBounds)
            {
                bounds = renderers[i].bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(renderers[i].bounds);
            }
        }

        if (hasBounds)
        {
            return true;
        }

        Collider[] colliders = target.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            if (!hasBounds)
            {
                bounds = colliders[i].bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(colliders[i].bounds);
            }
        }

        return hasBounds;
    }

    /// <summary>
    /// Frames bounds for a perspective camera.
    /// </summary>
    private static void FramePerspectiveCamera(
        Camera camera,
        Bounds originalBounds,
        Vector3[] paddedCorners,
        Vector3 focusPoint,
        Quaternion cameraRotation)
    {
        Quaternion inverseCameraRotation = Quaternion.Inverse(cameraRotation);

        float tanVertical = Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float tanHorizontal = tanVertical * camera.aspect;

        float requiredDistance = 0.0f;
        const float minDepth = 0.01f;

        for (int i = 0; i < paddedCorners.Length; i++)
        {
            Vector3 local = inverseCameraRotation * (paddedCorners[i] - focusPoint);

            requiredDistance = Mathf.Max(requiredDistance, Mathf.Abs(local.x) / tanHorizontal - local.z);
            requiredDistance = Mathf.Max(requiredDistance, Mathf.Abs(local.y) / tanVertical - local.z);
            requiredDistance = Mathf.Max(requiredDistance, -local.z + minDepth);
        }

        Vector3 forward = cameraRotation * Vector3.forward;
        Vector3 position = focusPoint - forward * requiredDistance;

        camera.transform.SetPositionAndRotation(position, cameraRotation);

        AdjustClipPlanes(camera, originalBounds);
    }

    /// <summary>
    /// Frames bounds for an orthographic camera.
    /// </summary>
    private static void FrameOrthographicCamera(
        Camera camera,
        Bounds originalBounds,
        Vector3[] paddedCorners,
        Vector3 focusPoint,
        Quaternion cameraRotation)
    {
        Quaternion inverseCameraRotation = Quaternion.Inverse(cameraRotation);

        float requiredOrthoSize = 0.0f;
        float minLocalZ = float.PositiveInfinity;
        float safeAspect = Mathf.Max(0.0001f, camera.aspect);

        for (int i = 0; i < paddedCorners.Length; i++)
        {
            Vector3 local = inverseCameraRotation * (paddedCorners[i] - focusPoint);

            requiredOrthoSize = Mathf.Max(requiredOrthoSize, Mathf.Abs(local.y));
            requiredOrthoSize = Mathf.Max(requiredOrthoSize, Mathf.Abs(local.x) / safeAspect);

            minLocalZ = Mathf.Min(minLocalZ, local.z);
        }

        camera.orthographicSize = requiredOrthoSize;

        float distance = Mathf.Max(0.01f, -minLocalZ + 0.01f);
        Vector3 forward = cameraRotation * Vector3.forward;
        Vector3 position = focusPoint - forward * distance;

        camera.transform.SetPositionAndRotation(position, cameraRotation);

        AdjustClipPlanes(camera, originalBounds);
    }

    /// <summary>
    /// Adjusts near and far clip planes so the full bounds stay inside the frustum depth.
    /// </summary>
    private static void AdjustClipPlanes(Camera camera, Bounds bounds)
    {
        Vector3[] corners = GetBoundsCorners(bounds.center, bounds.extents);

        float minZ = float.PositiveInfinity;
        float maxZ = float.NegativeInfinity;

        for (int i = 0; i < corners.Length; i++)
        {
            float z = camera.transform.InverseTransformPoint(corners[i]).z;
            minZ = Mathf.Min(minZ, z);
            maxZ = Mathf.Max(maxZ, z);
        }

        float depthPadding = Mathf.Max(0.01f, (maxZ - minZ) * 0.05f);

        camera.nearClipPlane = Mathf.Max(0.01f, minZ - depthPadding);
        camera.farClipPlane = Mathf.Max(camera.nearClipPlane + 0.1f, maxZ + depthPadding);
    }

    /// <summary>
    /// Creates the camera rotation from orbit angles.
    /// The resulting camera always looks toward the target.
    /// </summary>
    private static Quaternion GetOrbitRotation(
        Transform targetTransform,
        float orbitYaw,
        float orbitPitch,
        bool orbitRelativeToTarget)
    {
        Vector3 localForward = Quaternion.Euler(orbitPitch, orbitYaw, 0.0f) * Vector3.forward;
        Vector3 worldForward = orbitRelativeToTarget
            ? targetTransform.TransformDirection(localForward)
            : localForward;

        if (worldForward.sqrMagnitude < 0.000001f)
        {
            worldForward = Vector3.forward;
        }

        worldForward.Normalize();

        Vector3 worldUp = GetSafeUp(worldForward, Vector3.up);
        return Quaternion.LookRotation(worldForward, worldUp);
    }

    /// <summary>
    /// Returns a stable up vector for LookRotation.
    /// </summary>
    private static Vector3 GetSafeUp(Vector3 forward, Vector3 preferredUp)
    {
        Vector3 normalizedPreferredUp = preferredUp.normalized;

        if (Mathf.Abs(Vector3.Dot(forward, normalizedPreferredUp)) < 0.999f)
        {
            return normalizedPreferredUp;
        }

        Vector3 fallbackUp = Vector3.forward;
        if (Mathf.Abs(Vector3.Dot(forward, fallbackUp)) > 0.999f)
        {
            fallbackUp = Vector3.right;
        }

        Vector3 right = Vector3.Cross(fallbackUp, forward).normalized;
        return Vector3.Cross(forward, right).normalized;
    }

    /// <summary>
    /// Returns the 8 corners of a bounds defined by center and extents.
    /// </summary>
    private static Vector3[] GetBoundsCorners(Vector3 center, Vector3 extents)
    {
        return new Vector3[]
        {
            center + new Vector3(-extents.x, -extents.y, -extents.z),
            center + new Vector3(-extents.x, -extents.y,  extents.z),
            center + new Vector3(-extents.x,  extents.y, -extents.z),
            center + new Vector3(-extents.x,  extents.y,  extents.z),
            center + new Vector3( extents.x, -extents.y, -extents.z),
            center + new Vector3( extents.x, -extents.y,  extents.z),
            center + new Vector3( extents.x,  extents.y, -extents.z),
            center + new Vector3( extents.x,  extents.y,  extents.z),
        };
    }
}