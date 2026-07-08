using UnityEngine;

[ExecuteAlways]
[DisallowMultipleComponent]
public class CurvedDanmakuSurfaceLayer : MonoBehaviour
{
    [Header("层类型")]
    public CurvedCloudLayerKind layerKind = CurvedCloudLayerKind.MidInfo;

    [Header("曲面范围")]
    public bool useAutoPreset = true;
    [Tooltip("弧面半径，单位约等于真实米（前提：CurvedDanmakuCloudRig 已抵消 screen 的缩放）")]
    public float radius = 2.4f;
    [Range(10f, 180f)] public float horizontalAngleSpan = 90f;
    public float horizontalAngleOffset = 0f;
    public float verticalHeight = 1.2f;
    public float verticalOffset = 0f;
    [Tooltip("u 在该区间内会被推到左右两侧，避免挡在视频正中")]
    [Range(0f, 0.5f)] public float centerDeadZoneHalfWidth = 0.14f;

    [Header("运行时")]
    [Range(0f, 1f)] public float defaultAlpha = 0.95f;
    public int maxConcurrent = 6;
    public float clusterSpreadU = 0.12f;
    public float clusterSpreadV = 0.1f;

    [SerializeField] Transform editorVisualRoot;
    [SerializeField] LineRenderer editorOutline;

    public Color EditorColor => GetLayerColor();

    void OnEnable()
    {
        EnsureEditorVisual();
        UpdateEditorVisual();
        SetEditorVisualActive(!Application.isPlaying);
    }

    void OnValidate()
    {
        radius = Mathf.Max(0.2f, radius);
        verticalHeight = Mathf.Max(0.1f, verticalHeight);
        maxConcurrent = Mathf.Max(1, maxConcurrent);
        clusterSpreadU = Mathf.Max(0.01f, clusterSpreadU);
        clusterSpreadV = Mathf.Max(0.01f, clusterSpreadV);

        EnsureEditorVisual();
        UpdateEditorVisual();
        SetEditorVisualActive(!Application.isPlaying);
    }

    void Update()
    {
        if (Application.isPlaying)
        {
            SetEditorVisualActive(false);
            return;
        }

        SetEditorVisualActive(true);
        UpdateEditorVisual();
    }

    public Vector3 GetLocalPosition(float u, float v)
    {
        return GetLocalPosition(u, v, 0f);
    }

    public Vector3 GetLocalPosition(float u, float v, float radiusOffset)
    {
        float angle = (Mathf.Lerp(-horizontalAngleSpan * 0.5f, horizontalAngleSpan * 0.5f, ClampU(u)) + horizontalAngleOffset) * Mathf.Deg2Rad;
        float y = Mathf.Lerp(-verticalHeight * 0.5f, verticalHeight * 0.5f, Mathf.Clamp01(v)) + verticalOffset;
        float effectiveRadius = Mathf.Max(0.05f, radius + radiusOffset);
        return new Vector3(Mathf.Sin(angle) * effectiveRadius, y, -Mathf.Cos(angle) * effectiveRadius);
    }

    public float ClampU(float u)
    {
        u = Mathf.Clamp01(u);
        if (layerKind == CurvedCloudLayerKind.NearEmotion || centerDeadZoneHalfWidth <= 0f)
            return u;

        float deadMin = 0.5f - centerDeadZoneHalfWidth;
        float deadMax = 0.5f + centerDeadZoneHalfWidth;
        if (u < deadMin || u > deadMax)
            return u;

        return u < 0.5f ? deadMin : deadMax;
    }

    public void ApplyLayerKindPreset()
    {
        switch (layerKind)
        {
            case CurvedCloudLayerKind.NearEmotion:
                radius = 1.1f;
                horizontalAngleSpan = 100f;
                horizontalAngleOffset = 0f;
                verticalHeight = 0.35f;
                verticalOffset = -0.55f;
                centerDeadZoneHalfWidth = 0f;
                clusterSpreadU = 0.12f;
                clusterSpreadV = 0.06f;
                transform.localPosition = Vector3.zero;
                transform.localRotation = Quaternion.identity;
                break;

            case CurvedCloudLayerKind.MidInfo:
                radius = 1.9f;
                horizontalAngleSpan = 56f;
                horizontalAngleOffset = 0f;
                verticalHeight = 0.55f;
                verticalOffset = 0.05f;
                centerDeadZoneHalfWidth = 0.18f;
                clusterSpreadU = 0.03f;
                clusterSpreadV = 0.04f;
                transform.localPosition = Vector3.zero;
                transform.localRotation = Quaternion.identity;
                break;

            default:
                radius = 2.7f;
                horizontalAngleSpan = 64f;
                horizontalAngleOffset = 0f;
                verticalHeight = 0.5f;
                verticalOffset = 0.5f;
                centerDeadZoneHalfWidth = 0.15f;
                clusterSpreadU = 0.03f;
                clusterSpreadV = 0.04f;
                transform.localPosition = Vector3.zero;
                transform.localRotation = Quaternion.identity;
                break;
        }
    }

    public Vector3 GetWorldPosition(float u, float v)
    {
        return GetWorldPosition(u, v, 0f);
    }

    public Vector3 GetWorldPosition(float u, float v, float radiusOffset)
    {
        return transform.TransformPoint(GetLocalPosition(u, v, radiusOffset));
    }

    public Vector3 GetJitteredWorldPosition(float u, float v)
    {
        float ju = Mathf.Clamp01(u + Random.Range(-clusterSpreadU, clusterSpreadU));
        float jv = Mathf.Clamp01(v + Random.Range(-clusterSpreadV, clusterSpreadV));
        return GetWorldPosition(ju, jv);
    }

    public Quaternion GetBillboardRotation(Vector3 worldPosition, Camera camera)
    {
        if (camera == null)
            return transform.rotation;

        Vector3 toCamera = worldPosition - camera.transform.position;
        if (toCamera.sqrMagnitude < 0.0001f)
            return transform.rotation;

        return Quaternion.LookRotation(toCamera.normalized, Vector3.up);
    }

    public void EnsureEditorVisual()
    {
        if (editorVisualRoot == null)
        {
            Transform existing = transform.Find("EditorSurfaceGuide");
            editorVisualRoot = existing != null ? existing : new GameObject("EditorSurfaceGuide").transform;
            editorVisualRoot.SetParent(transform, false);
        }

        if (editorOutline == null)
        {
            editorOutline = editorVisualRoot.GetComponent<LineRenderer>();
            if (editorOutline == null)
                editorOutline = editorVisualRoot.gameObject.AddComponent<LineRenderer>();
        }

        editorOutline.useWorldSpace = false;
        editorOutline.loop = false;
        editorOutline.widthMultiplier = 0.012f;
        editorOutline.numCornerVertices = 4;
        editorOutline.numCapVertices = 4;
        editorOutline.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        editorOutline.receiveShadows = false;

        if (editorOutline.sharedMaterial == null)
        {
            Material lineMat = new Material(Shader.Find("Sprites/Default"));
            lineMat.color = GetLayerColor();
            editorOutline.sharedMaterial = lineMat;
        }
    }

    public void UpdateEditorVisual()
    {
        if (editorOutline == null)
            return;

        const int horizontalSegments = 12;
        const int verticalSegments = 4;
        int pointCount = (horizontalSegments + 1) * 2 + (verticalSegments + 1) * 2;
        editorOutline.positionCount = pointCount;

        int index = 0;
        for (int i = 0; i <= horizontalSegments; i++)
        {
            float u = i / (float)horizontalSegments;
            editorOutline.SetPosition(index++, GetLocalPosition(u, 0f));
        }

        for (int i = 0; i <= horizontalSegments; i++)
        {
            float u = i / (float)horizontalSegments;
            editorOutline.SetPosition(index++, GetLocalPosition(u, 1f));
        }

        for (int i = 0; i <= verticalSegments; i++)
        {
            float v = i / (float)verticalSegments;
            editorOutline.SetPosition(index++, GetLocalPosition(0f, v));
        }

        for (int i = 0; i <= verticalSegments; i++)
        {
            float v = i / (float)verticalSegments;
            editorOutline.SetPosition(index++, GetLocalPosition(1f, v));
        }

        if (editorOutline.sharedMaterial != null)
            editorOutline.sharedMaterial.color = GetLayerColor();
    }

    void SetEditorVisualActive(bool active)
    {
        if (editorVisualRoot != null)
            editorVisualRoot.gameObject.SetActive(active);
    }

    Color GetLayerColor()
    {
        switch (layerKind)
        {
            case CurvedCloudLayerKind.NearEmotion:
                return new Color(1f, 0.78f, 0.2f, 0.95f);
            case CurvedCloudLayerKind.FarInfo:
                return new Color(0.75f, 0.55f, 1f, 0.95f);
            default:
                return new Color(0.25f, 0.85f, 1f, 0.95f);
        }
    }

    void OnDrawGizmos()
    {
        if (Application.isPlaying)
            return;

        Gizmos.color = GetLayerColor() * new Color(1f, 1f, 1f, 0.25f);
        Matrix4x4 old = Gizmos.matrix;
        Gizmos.matrix = transform.localToWorldMatrix;

        const int segments = 10;
        Vector3 prevBottom = GetLocalPosition(0f, 0f);
        Vector3 prevTop = GetLocalPosition(0f, 1f);
        for (int i = 1; i <= segments; i++)
        {
            float u = i / (float)segments;
            Vector3 bottom = GetLocalPosition(u, 0f);
            Vector3 top = GetLocalPosition(u, 1f);
            Gizmos.DrawLine(prevBottom, bottom);
            Gizmos.DrawLine(prevTop, top);
            Gizmos.DrawLine(bottom, top);
            prevBottom = bottom;
            prevTop = top;
        }

        Gizmos.matrix = old;
    }
}
