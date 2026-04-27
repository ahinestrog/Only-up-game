using System.Collections.Generic;
using UnityEngine;

public class ProceduralOnlyUpGenerator : MonoBehaviour
{
    public enum StartPointMode
    {
        ManualPosition = 0,
        AnchorTransform = 1
    }

    [Header("Prefabs")]
    [SerializeField] private List<GameObject> platformPrefabs = new List<GameObject>();
    [SerializeField] private GameObject fallbackPlatformPrefab;
    [SerializeField] private Vector3 fallbackPlatformScale = new Vector3(1.6f, 0.4f, 1.6f);

    [Header("Platform Sizing (normalize wildly different prefabs)")]
    [SerializeField] private bool normalizePlatformSize = true;
    [SerializeField] private float targetPlatformDiameter = 1.6f;
    [SerializeField] private float maxPlatformHeight = 1.5f;

    [Header("Top Cap (optional flat landing surface)")]
    [SerializeField] private bool addTopCap = false;
    [SerializeField] private GameObject topCapPrefab;
    [SerializeField] private Vector3 topCapScale = new Vector3(1.4f, 0.08f, 1.4f);
    [SerializeField] private float topCapYOffset = 0.02f;

    [Header("Start Point")]
    [SerializeField] private StartPointMode startPointMode = StartPointMode.AnchorTransform;
    [SerializeField] private Transform anchorTransform;
    [SerializeField] private bool useAnchorForwardDirection = true;
    [SerializeField] private float initialYOffset = 1.5f;
    [SerializeField] private Vector3 startPosition = Vector3.zero;

    [Header("Path")]
    [SerializeField] private int segmentCount = 80;
    [SerializeField] private float minVerticalStep = 0.35f;
    [SerializeField] private float maxVerticalStep = 0.7f;
    [SerializeField] private float extraForwardGapMin = 1.6f;
    [SerializeField] private float extraForwardGapMax = 2.4f;
    [SerializeField] private float maxSideStep = 0.7f;
    [SerializeField] private float maxTurnAnglePerStep = 10f;
    [SerializeField] private int maxPlacementAttemptsPerSegment = 32;
    [SerializeField] private float minXZSeparation = 0.3f;
    [SerializeField] private float minYSeparation = 0.45f;
    [SerializeField] private float footprintGapPadding = 0.25f;

    [Header("Player Reach (must match ThirdPersonController)")]
    [SerializeField] private float playerJumpHeight = 1.2f;
    [SerializeField] private float playerSprintSpeed = 5.335f;
    [SerializeField] private float playerGravityMagnitude = 15f;
    [Range(0.5f, 1f)]
    [SerializeField] private float reachSafetyMargin = 0.8f;

    [Header("First Segment Overrides")]
    [SerializeField] private bool useFirstSegmentOverrides = true;
    [SerializeField] private float firstForwardStepMin = 1.6f;
    [SerializeField] private float firstForwardStepMax = 2.2f;
    [SerializeField] private float firstVerticalStepMin = 0.3f;
    [SerializeField] private float firstVerticalStepMax = 0.7f;
    [SerializeField] private float firstMaxSideStep = 0.35f;

    [Header("Transform Variation")]
    [SerializeField] private bool alignYawWithPath = true;
    [SerializeField] private Vector2 randomYawRange = new Vector2(-20f, 20f);
    [SerializeField] private Vector2 randomUniformScaleRange = new Vector2(0.95f, 1.1f);

    [Header("Generation")]
    [SerializeField] private bool clearPreviousOnGenerate = true;
    [SerializeField] private bool generateOnStart = true;
    [SerializeField] private int randomSeed = 12345;

    [Header("Debug")]
    [SerializeField] private bool drawPathGizmos = true;
    [SerializeField] private Color gizmoReachableColor = new Color(0.2f, 1f, 0.6f, 0.85f);
    [SerializeField] private Color gizmoUnreachableColor = new Color(1f, 0.25f, 0.25f, 0.95f);

    private struct LandingPoint
    {
        public Vector3 position;
        public float footprintRadius;
    }

    private struct PrefabInfo
    {
        public float footprintRadius;
        public float height;
        public float scaleMultiplier;
    }

    private readonly List<LandingPoint> _landingPoints = new List<LandingPoint>();
    private readonly List<GameObject> _spawned = new List<GameObject>();
    private readonly Dictionary<int, PrefabInfo> _prefabInfo = new Dictionary<int, PrefabInfo>();

    private float _initialJumpVelocity;

    private void Start()
    {
        if (generateOnStart)
        {
            Generate();
        }
    }

    [ContextMenu("Generate Path")]
    public void Generate()
    {
        if (segmentCount <= 0)
        {
            Debug.LogWarning("ProceduralOnlyUpGenerator: segmentCount must be > 0.", this);
            return;
        }

        if (clearPreviousOnGenerate)
        {
            ClearGenerated();
        }

        Random.InitState(randomSeed);
        _landingPoints.Clear();
        _prefabInfo.Clear();

        playerGravityMagnitude = Mathf.Max(0.1f, playerGravityMagnitude);
        playerJumpHeight = Mathf.Max(0.05f, playerJumpHeight);
        _initialJumpVelocity = Mathf.Sqrt(2f * playerGravityMagnitude * playerJumpHeight);

        for (int i = 0; i < platformPrefabs.Count; i++)
        {
            if (platformPrefabs[i] != null)
            {
                MeasurePrefab(platformPrefabs[i]);
            }
        }

        Vector3 cursor = ResolveStartPosition();
        Vector3 forward = ResolveInitialDirection();
        _landingPoints.Add(new LandingPoint { position = cursor, footprintRadius = 0.6f });

        for (int i = 0; i < segmentCount; i++)
        {
            bool isFirst = i == 0 && useFirstSegmentOverrides;

            GameObject prefab = GetRandomValidPrefab();
            PrefabInfo info = prefab != null
                ? MeasurePrefab(prefab)
                : new PrefabInfo { footprintRadius = Mathf.Max(fallbackPlatformScale.x, fallbackPlatformScale.z) * 0.5f, height = fallbackPlatformScale.y, scaleMultiplier = 1f };

            if (!TryFindNextLandingPoint(cursor, forward, isFirst, info.footprintRadius, out Vector3 nextLanding))
            {
                float fallbackFwd = Mathf.Max(0.6f, info.footprintRadius * 2f + footprintGapPadding);
                fallbackFwd = Mathf.Min(fallbackFwd, MaxHorizontalForDy(0f) * 0.85f);
                float fallbackUp = Mathf.Min(minVerticalStep, playerJumpHeight * reachSafetyMargin * 0.5f);
                nextLanding = cursor + forward * fallbackFwd + Vector3.up * fallbackUp;
            }

            GameObject placed = PlacePlatform(nextLanding, forward, prefab, info);
            _landingPoints.Add(new LandingPoint { position = nextLanding, footprintRadius = info.footprintRadius });
            if (placed != null)
            {
                _spawned.Add(placed);
            }

            if (addTopCap)
            {
                GameObject cap = CreateTopCap(nextLanding, forward);
                if (cap != null)
                {
                    _spawned.Add(cap);
                }
            }

            Vector3 horizontalDelta = Vector3.ProjectOnPlane(nextLanding - cursor, Vector3.up);
            if (horizontalDelta.sqrMagnitude > 0.0001f)
            {
                forward = horizontalDelta.normalized;
            }

            float turnAngle = Random.Range(-maxTurnAnglePerStep, maxTurnAnglePerStep);
            forward = (Quaternion.Euler(0f, turnAngle, 0f) * forward).normalized;

            cursor = nextLanding;
        }
    }

    [ContextMenu("Clear Generated")]
    public void ClearGenerated()
    {
        for (int i = _spawned.Count - 1; i >= 0; i--)
        {
            DestroyAny(_spawned[i]);
        }
        _spawned.Clear();
        _landingPoints.Clear();

        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Transform child = transform.GetChild(i);
            string n = child.name;
            if (n.Contains("(Clone)") || n.StartsWith("[GenPlatform]") || n.StartsWith("[GenCap]") || n.StartsWith("[Measure]"))
            {
                DestroyAny(child.gameObject);
            }
        }
    }

    private PrefabInfo MeasurePrefab(GameObject prefab)
    {
        int key = prefab.GetInstanceID();
        if (_prefabInfo.TryGetValue(key, out PrefabInfo cached)) return cached;

        Vector3 measureLoc = new Vector3(0f, -100000f, 0f);
        GameObject temp = Instantiate(prefab, measureLoc, Quaternion.identity, transform);
        temp.SetActive(true);
        temp.name = "[Measure] " + prefab.name;

        PrefabInfo info;
        if (!TryGetWorldBounds(temp, out Bounds raw) || raw.size.sqrMagnitude < 0.0001f)
        {
            info = new PrefabInfo { footprintRadius = 0.6f, height = 0.6f, scaleMultiplier = 1f };
        }
        else
        {
            float scaleMul = 1f;
            float maxXZ = Mathf.Max(raw.size.x, raw.size.z);
            if (normalizePlatformSize && maxXZ > 0.001f && targetPlatformDiameter > 0f)
            {
                scaleMul = targetPlatformDiameter / maxXZ;
            }
            if (maxPlatformHeight > 0f)
            {
                float scaledHeight = raw.size.y * scaleMul;
                if (scaledHeight > maxPlatformHeight)
                {
                    scaleMul *= maxPlatformHeight / scaledHeight;
                }
            }

            temp.transform.localScale = temp.transform.localScale * scaleMul;
            TryGetWorldBounds(temp, out Bounds scaled);
            float maxRandomScale = Mathf.Max(1f, Mathf.Max(Mathf.Abs(randomUniformScaleRange.x), Mathf.Abs(randomUniformScaleRange.y)));
            info = new PrefabInfo
            {
                footprintRadius = Mathf.Max(scaled.extents.x, scaled.extents.z) * maxRandomScale,
                height = scaled.size.y * maxRandomScale,
                scaleMultiplier = scaleMul
            };
        }

        _prefabInfo[key] = info;
        DestroyAny(temp);
        return info;
    }

    private bool TryFindNextLandingPoint(Vector3 from, Vector3 forward, bool isFirst, float candidateFootprint, out Vector3 result)
    {
        result = from;
        Vector3 side = new Vector3(-forward.z, 0f, forward.x);

        float vMin = isFirst ? firstVerticalStepMin : minVerticalStep;
        float vMax = isFirst ? firstVerticalStepMax : maxVerticalStep;
        float fwdMin = isFirst ? firstForwardStepMin : extraForwardGapMin;
        float fwdMax = isFirst ? firstForwardStepMax : extraForwardGapMax;
        float sideMax = isFirst ? firstMaxSideStep : maxSideStep;

        float verticalCap = playerJumpHeight * reachSafetyMargin;
        vMax = Mathf.Min(vMax, verticalCap);
        vMin = Mathf.Min(vMin, vMax * 0.5f);
        if (vMax <= 0.01f)
        {
            vMax = verticalCap;
            vMin = verticalCap * 0.4f;
        }

        LandingPoint last = _landingPoints[_landingPoints.Count - 1];
        float minHorizForFootprint = last.footprintRadius + candidateFootprint + footprintGapPadding;

        for (int attempt = 0; attempt < maxPlacementAttemptsPerSegment; attempt++)
        {
            float dy = Random.Range(vMin, vMax);
            float horizReach = MaxHorizontalForDy(dy);
            if (horizReach <= minHorizForFootprint)
            {
                dy = vMin;
                horizReach = MaxHorizontalForDy(dy);
                if (horizReach <= minHorizForFootprint) continue;
            }

            float fwd = Random.Range(fwdMin, fwdMax);
            float lateral = Random.Range(-sideMax, sideMax);

            float horizDist = Mathf.Sqrt(fwd * fwd + lateral * lateral);
            if (horizDist < minHorizForFootprint)
            {
                float k = minHorizForFootprint / Mathf.Max(0.0001f, horizDist);
                fwd *= k;
                lateral *= k;
                horizDist = minHorizForFootprint;
            }
            if (horizDist > horizReach)
            {
                float k = horizReach / horizDist;
                fwd *= k;
                lateral *= k;
            }

            Vector3 candidate = from + forward * fwd + side * lateral + Vector3.up * dy;

            if (!IsCandidateClear(candidate, candidateFootprint))
            {
                continue;
            }

            result = candidate;
            return true;
        }

        return false;
    }

    private bool IsCandidateClear(Vector3 candidate, float candidateFootprint)
    {
        for (int i = 0; i < _landingPoints.Count; i++)
        {
            LandingPoint lp = _landingPoints[i];
            float xz = Vector2.Distance(
                new Vector2(lp.position.x, lp.position.z),
                new Vector2(candidate.x, candidate.z));
            float dy = Mathf.Abs(lp.position.y - candidate.y);
            float requiredXZ = Mathf.Max(minXZSeparation, lp.footprintRadius + candidateFootprint + footprintGapPadding);
            if (xz < requiredXZ && dy < minYSeparation)
            {
                return false;
            }
        }
        return true;
    }

    private float MaxHorizontalForDy(float dy)
    {
        float v0 = _initialJumpVelocity;
        float g = playerGravityMagnitude;
        float disc = v0 * v0 - 2f * g * dy;
        if (disc < 0f) return 0f;
        float t = (v0 + Mathf.Sqrt(disc)) / g;
        return t * playerSprintSpeed * reachSafetyMargin;
    }

    private GameObject PlacePlatform(Vector3 landingPoint, Vector3 forward, GameObject prefab, PrefabInfo info)
    {
        float baseYaw = alignYawWithPath ? Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg : 0f;
        float yaw = baseYaw + Random.Range(randomYawRange.x, randomYawRange.y);
        Quaternion rotation = Quaternion.Euler(0f, yaw, 0f);

        float scaleMin = Mathf.Min(randomUniformScaleRange.x, randomUniformScaleRange.y);
        float scaleMax = Mathf.Max(randomUniformScaleRange.x, randomUniformScaleRange.y);
        float randomScale = Random.Range(scaleMin, scaleMax);

        if (prefab == null)
        {
            return CreateFallback(landingPoint, rotation);
        }

        GameObject inst = Instantiate(prefab, landingPoint, rotation, transform);
        inst.SetActive(true);
        inst.transform.localScale = inst.transform.localScale * info.scaleMultiplier * randomScale;
        inst.name = "[GenPlatform] " + prefab.name;

        if (TryGetWorldBounds(inst, out Bounds bounds))
        {
            Vector3 topCenter = new Vector3(bounds.center.x, bounds.max.y, bounds.center.z);
            inst.transform.position += new Vector3(
                landingPoint.x - topCenter.x,
                landingPoint.y - topCenter.y,
                landingPoint.z - topCenter.z);
        }

        EnsureCollider(inst);
        return inst;
    }

    private GameObject CreateFallback(Vector3 landingPoint, Quaternion rotation)
    {
        GameObject pad;
        if (fallbackPlatformPrefab != null)
        {
            pad = Instantiate(fallbackPlatformPrefab, landingPoint, rotation, transform);
        }
        else
        {
            pad = GameObject.CreatePrimitive(PrimitiveType.Cube);
            pad.transform.SetParent(transform, true);
            pad.transform.SetPositionAndRotation(landingPoint, rotation);
        }

        pad.transform.localScale = fallbackPlatformScale;
        pad.transform.position += Vector3.down * (fallbackPlatformScale.y * 0.5f);
        pad.name = "[GenPlatform] Fallback";
        return pad;
    }

    private GameObject CreateTopCap(Vector3 landingPoint, Vector3 forward)
    {
        float baseYaw = alignYawWithPath ? Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg : 0f;
        Quaternion rotation = Quaternion.Euler(0f, baseYaw, 0f);
        Vector3 pos = landingPoint + Vector3.up * topCapYOffset;

        GameObject cap;
        if (topCapPrefab != null)
        {
            cap = Instantiate(topCapPrefab, pos, rotation, transform);
        }
        else
        {
            cap = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cap.transform.SetParent(transform, true);
            cap.transform.SetPositionAndRotation(pos, rotation);
        }

        cap.transform.localScale = topCapScale;
        cap.transform.position += Vector3.up * (topCapScale.y * 0.5f);
        cap.name = "[GenCap]";
        return cap;
    }

    private void EnsureCollider(GameObject go)
    {
        if (go.GetComponentInChildren<Collider>() != null) return;
        if (!TryGetWorldBounds(go, out Bounds bounds)) return;

        BoxCollider bc = go.AddComponent<BoxCollider>();
        Vector3 lossy = go.transform.lossyScale;
        bc.center = go.transform.InverseTransformPoint(bounds.center);
        bc.size = new Vector3(
            bounds.size.x / Mathf.Max(0.0001f, Mathf.Abs(lossy.x)),
            bounds.size.y / Mathf.Max(0.0001f, Mathf.Abs(lossy.y)),
            bounds.size.z / Mathf.Max(0.0001f, Mathf.Abs(lossy.z)));
    }

    private GameObject GetRandomValidPrefab()
    {
        if (platformPrefabs == null || platformPrefabs.Count == 0) return null;
        int start = Random.Range(0, platformPrefabs.Count);
        for (int i = 0; i < platformPrefabs.Count; i++)
        {
            int idx = (start + i) % platformPrefabs.Count;
            if (platformPrefabs[idx] != null) return platformPrefabs[idx];
        }
        return null;
    }

    private Vector3 ResolveStartPosition()
    {
        if (startPointMode == StartPointMode.AnchorTransform && anchorTransform != null)
        {
            return GetTopCenter(anchorTransform) + Vector3.up * initialYOffset;
        }
        return startPosition;
    }

    private Vector3 ResolveInitialDirection()
    {
        if (useAnchorForwardDirection && anchorTransform != null)
        {
            Vector3 fwd = Vector3.ProjectOnPlane(anchorTransform.forward, Vector3.up);
            if (fwd.sqrMagnitude > 0.0001f) return fwd.normalized;
        }
        return Vector3.forward;
    }

    private Vector3 GetTopCenter(Transform t)
    {
        Renderer[] rs = t.GetComponentsInChildren<Renderer>(true);
        if (rs.Length == 0) return t.position;
        Bounds b = rs[0].bounds;
        for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
        return new Vector3(b.center.x, b.max.y, b.center.z);
    }

    private bool TryGetWorldBounds(GameObject go, out Bounds bounds)
    {
        Renderer[] rs = go.GetComponentsInChildren<Renderer>(true);
        if (rs.Length > 0)
        {
            bounds = rs[0].bounds;
            for (int i = 1; i < rs.Length; i++) bounds.Encapsulate(rs[i].bounds);
            return true;
        }

        Collider[] cs = go.GetComponentsInChildren<Collider>(true);
        if (cs.Length > 0)
        {
            bounds = cs[0].bounds;
            for (int i = 1; i < cs.Length; i++) bounds.Encapsulate(cs[i].bounds);
            return true;
        }

        bounds = default;
        return false;
    }

    private static void DestroyAny(GameObject obj)
    {
        if (obj == null) return;
        if (Application.isPlaying) Destroy(obj);
        else DestroyImmediate(obj);
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawPathGizmos || _landingPoints == null || _landingPoints.Count < 2) return;

        float verticalCap = playerJumpHeight * reachSafetyMargin;
        for (int i = 1; i < _landingPoints.Count; i++)
        {
            Vector3 a = _landingPoints[i - 1].position;
            Vector3 b = _landingPoints[i].position;
            float dy = b.y - a.y;
            float dx = Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
            float reach = MaxHorizontalForDy(dy);
            bool reachable = dy <= verticalCap + 0.001f && dx <= reach + 0.001f;
            Gizmos.color = reachable ? gizmoReachableColor : gizmoUnreachableColor;
            Gizmos.DrawLine(a, b);
            Gizmos.DrawWireSphere(b, 0.18f);
        }
    }
}
