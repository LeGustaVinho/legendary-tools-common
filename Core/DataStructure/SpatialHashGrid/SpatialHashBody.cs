using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace LegendaryTools
{
    [DisallowMultipleComponent]
    public class SpatialHashBody : UnityBehaviour, ISpatialHashGridItem
    {
        [SerializeField] private SpatialHashGridVolume gridVolume;
        [SerializeField] private bool registerOnEnable = true;
        [SerializeField] private bool findSceneVolumeIfUnassigned;
        [SerializeField] private bool isDynamic = true;
        [SerializeField] private int stableId;
        [SerializeField] private uint userFlags;
        [SerializeField] private SpatialHashBoundsSource boundsSource = SpatialHashBoundsSource.Automatic;
        [SerializeField] private Vector3 customCenter;
        [SerializeField] private Vector3 customSize = Vector3.one;

        public SpatialHashGridVolume GridVolume => gridVolume;
        public bool RegisterOnEnable => registerOnEnable;
        public bool IsDynamic => isDynamic;
        public int StableId => stableId;
        public int Layer => gameObject.layer;
        public uint UserFlags => userFlags;

        public bool TryGetWorldBounds(SpatialHashGridDimension dimension, out Bounds bounds)
        {
            if (TryResolveBounds(boundsSource, out bounds))
            {
                return true;
            }

            if (TryResolveBounds(SpatialHashBoundsSource.Automatic, out bounds))
            {
                return true;
            }

            bounds = CreateCustomBounds();
            return HasRelevantSize(bounds, dimension);
        }

        public void ForceRefresh()
        {
            ResolveGridVolume()?.RefreshBody(this);
        }

        public void SetGridVolume(SpatialHashGridVolume volume, bool refreshRegistration = true)
        {
            if (gridVolume == volume)
            {
                return;
            }

            if (isActiveAndEnabled)
            {
                gridVolume?.UnregisterBody(this);
            }

            gridVolume = volume;

            if (refreshRegistration && isActiveAndEnabled && registerOnEnable)
            {
                ResolveGridVolume()?.RegisterBody(this);
            }
        }

        private void OnEnable()
        {
            if (!registerOnEnable)
            {
                return;
            }

            ResolveGridVolume()?.RegisterBody(this);
        }

        private void OnDisable()
        {
            gridVolume?.UnregisterBody(this);
        }

        private void OnValidate()
        {
            customSize.x = Mathf.Max(0.0001f, customSize.x);
            customSize.y = Mathf.Max(0.0001f, customSize.y);
            customSize.z = Mathf.Max(0.0001f, customSize.z);

#if UNITY_EDITOR
            if (stableId == 0)
            {
                stableId = GenerateStableId();
                EditorUtility.SetDirty(this);
            }
#endif
        }

        private SpatialHashGridVolume ResolveGridVolume()
        {
            if (gridVolume != null)
            {
                return gridVolume;
            }

            gridVolume = GetComponentInParent<SpatialHashGridVolume>();
            if (gridVolume != null)
            {
                return gridVolume;
            }

            if (!findSceneVolumeIfUnassigned)
            {
                return null;
            }

            gridVolume = FindFirstObjectByType<SpatialHashGridVolume>();
            if (gridVolume != null)
            {
                Debug.LogWarning($"{nameof(SpatialHashBody)} on {name} auto-registered to scene volume {gridVolume.name}. Assign a volume explicitly to avoid ambiguous registration.", this);
            }

            return gridVolume;
        }

        private bool TryResolveBounds(SpatialHashBoundsSource source, out Bounds bounds)
        {
            switch (source)
            {
                case SpatialHashBoundsSource.Automatic:
                    if (TryResolveBounds(SpatialHashBoundsSource.Collider3D, out bounds) ||
                        TryResolveBounds(SpatialHashBoundsSource.Collider2D, out bounds) ||
                        TryResolveBounds(SpatialHashBoundsSource.Renderer, out bounds))
                    {
                        return true;
                    }

                    bounds = CreateCustomBounds();
                    return HasNonZeroVolume(bounds);

                case SpatialHashBoundsSource.Renderer:
                    Renderer rendererComponent = GetComponent<Renderer>();
                    if (rendererComponent != null)
                    {
                        bounds = rendererComponent.bounds;
                        return true;
                    }

                    break;

                case SpatialHashBoundsSource.Collider3D:
                    Collider collider3D = GetComponent<Collider>();
                    if (collider3D != null)
                    {
                        bounds = collider3D.bounds;
                        return true;
                    }

                    break;

                case SpatialHashBoundsSource.Collider2D:
                    Collider2D collider2D = GetComponent<Collider2D>();
                    if (collider2D != null)
                    {
                        bounds = collider2D.bounds;
                        return true;
                    }

                    break;

                case SpatialHashBoundsSource.Custom:
                    bounds = CreateCustomBounds();
                    return HasNonZeroVolume(bounds);
            }

            bounds = default;
            return false;
        }

        private Bounds CreateCustomBounds()
        {
            return new Bounds(transform.TransformPoint(customCenter), Vector3.Scale(customSize, Abs(transform.lossyScale)));
        }

        private static Vector3 Abs(Vector3 value)
        {
            return new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
        }

        private static bool HasNonZeroVolume(Bounds bounds)
        {
            return bounds.size.sqrMagnitude > 0f;
        }

        private static bool HasRelevantSize(Bounds bounds, SpatialHashGridDimension dimension)
        {
            Vector3 size = bounds.size;
            return dimension switch
            {
                SpatialHashGridDimension.XY2D => size.x > 0f && size.y > 0f,
                SpatialHashGridDimension.XZ2D => size.x > 0f && size.z > 0f,
                _ => size.x > 0f && size.y > 0f && size.z > 0f
            };
        }

#if UNITY_EDITOR
        private int GenerateStableId()
        {
            GlobalObjectId globalId = GlobalObjectId.GetGlobalObjectIdSlow(this);
            string source = globalId.ToString();

            unchecked
            {
                int hash = 17;
                for (int i = 0; i < source.Length; i++)
                {
                    hash = (hash * 31) + source[i];
                }

                return hash == 0 ? 1 : hash;
            }
        }
#endif
    }
}
