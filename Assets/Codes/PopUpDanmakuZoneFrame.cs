using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways]
[DisallowMultipleComponent]
public class PopUpDanmakuZoneFrame : MonoBehaviour
{
    public PopUpDanmakuZone zone = PopUpDanmakuZone.Near;

    [Tooltip("区域框宽高（本地单位，相对 screen）")]
    public Vector2 frameSize = new Vector2(2.7f, 1.2f);

    [Tooltip("近/中区：左右两个锚点；远区：左/中/右三个锚点")]
    public bool useCenterAnchor;

    [Tooltip("每个锚点最多同时显示几条弹幕")]
    public int maxConcurrentPerAnchor = 2;

    [SerializeField] Transform editorVisualRoot;
    [SerializeField] LineRenderer editorOutline;

    readonly List<PopUpDanmakuAnchor> anchors = new List<PopUpDanmakuAnchor>();

    public IReadOnlyList<PopUpDanmakuAnchor> Anchors => anchors;

    void Awake()
    {
        if (Application.isPlaying)
            HideEditorVisualImmediate();
    }

    void OnEnable()
    {
        EnsureEditorVisual();
        EnsureAnchors();
        UpdateEditorVisual();
        SetEditorVisualActive(!Application.isPlaying);
    }

    void OnValidate()
    {
        frameSize.x = Mathf.Max(0.2f, frameSize.x);
        frameSize.y = Mathf.Max(0.2f, frameSize.y);
        maxConcurrentPerAnchor = Mathf.Max(1, maxConcurrentPerAnchor);

        if (zone == PopUpDanmakuZone.Far)
            useCenterAnchor = true;

        EnsureEditorVisual();
        EnsureAnchors();
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

    public PopUpDanmakuAnchor[] GetAnchorArray()
    {
        EnsureAnchors();
        return anchors.ToArray();
    }

    public void EnsureAnchors()
    {
        anchors.Clear();

        if (useCenterAnchor || zone == PopUpDanmakuZone.Far)
            anchors.Add(GetOrCreateAnchor("AnchorCenter", 0));

        anchors.Add(GetOrCreateAnchor("AnchorLeft", useCenterAnchor || zone == PopUpDanmakuZone.Far ? 1 : 0));
        anchors.Add(GetOrCreateAnchor("AnchorRight", useCenterAnchor || zone == PopUpDanmakuZone.Far ? 2 : 1));

        foreach (PopUpDanmakuAnchor anchor in anchors)
        {
            anchor.zone = zone;
            anchor.maxConcurrent = maxConcurrentPerAnchor;
        }
    }

    [ContextMenu("Snap Anchors To Frame")]
    public void SnapAnchorsToFrame()
    {
        SyncAnchorLocalPositions();
        UpdateEditorVisual();
    }

    public void SyncAnchorLocalPositions()
    {
        EnsureAnchors();

        float halfW = frameSize.x * 0.5f;
        float halfH = frameSize.y * 0.5f;

        foreach (PopUpDanmakuAnchor anchor in anchors)
        {
            if (anchor == null)
                continue;

            switch (anchor.slotIndex)
            {
                case 0 when useCenterAnchor || zone == PopUpDanmakuZone.Far:
                    anchor.transform.localPosition = new Vector3(0f, 0f, 0f);
                    break;
                case 0:
                    anchor.transform.localPosition = new Vector3(-halfW, 0f, 0f);
                    break;
                case 1:
                    anchor.transform.localPosition = new Vector3(
                        useCenterAnchor || zone == PopUpDanmakuZone.Far ? -halfW : halfW,
                        0f,
                        0f);
                    break;
                default:
                    anchor.transform.localPosition = new Vector3(halfW, 0f, 0f);
                    break;
            }

            anchor.transform.localRotation = Quaternion.identity;
            anchor.transform.localScale = Vector3.one;
        }
    }

    PopUpDanmakuAnchor GetOrCreateAnchor(string name, int slotIndex)
    {
        Transform existing = transform.Find(name);
        GameObject go = existing != null ? existing.gameObject : new GameObject(name);
        if (existing == null)
            go.transform.SetParent(transform, false);

        PopUpDanmakuAnchor anchor = go.GetComponent<PopUpDanmakuAnchor>();
        if (anchor == null)
            anchor = go.AddComponent<PopUpDanmakuAnchor>();

        anchor.zone = zone;
        anchor.slotIndex = slotIndex;
        anchor.maxConcurrent = maxConcurrentPerAnchor;
        return anchor;
    }

    void EnsureEditorVisual()
    {
        if (editorVisualRoot == null)
        {
            Transform existing = transform.Find("EditorZoneGuide");
            editorVisualRoot = existing != null ? existing : new GameObject("EditorZoneGuide").transform;
            editorVisualRoot.SetParent(transform, false);
        }

        if (editorOutline == null)
        {
            editorOutline = editorVisualRoot.GetComponent<LineRenderer>();
            if (editorOutline == null)
                editorOutline = editorVisualRoot.gameObject.AddComponent<LineRenderer>();
        }

        editorOutline.useWorldSpace = false;
        editorOutline.loop = true;
        editorOutline.widthMultiplier = 0.015f;
        editorOutline.numCornerVertices = 4;
        editorOutline.numCapVertices = 4;
        editorOutline.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        editorOutline.receiveShadows = false;
        editorOutline.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        editorOutline.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

        if (editorOutline.sharedMaterial == null)
        {
            Material lineMat = new Material(Shader.Find("Sprites/Default"));
            lineMat.color = GetZoneColor();
            editorOutline.sharedMaterial = lineMat;
        }
    }

    public void UpdateEditorVisual()
    {
        if (editorOutline == null)
            return;

        float halfW = frameSize.x * 0.5f;
        float halfH = frameSize.y * 0.5f;
        editorOutline.positionCount = 4;
        editorOutline.SetPositions(new[]
        {
            new Vector3(-halfW, halfH, 0f),
            new Vector3(halfW, halfH, 0f),
            new Vector3(halfW, -halfH, 0f),
            new Vector3(-halfW, -halfH, 0f)
        });

        if (editorOutline.sharedMaterial != null)
            editorOutline.sharedMaterial.color = GetZoneColor();
    }

    void SetEditorVisualActive(bool active)
    {
        if (editorVisualRoot != null)
            editorVisualRoot.gameObject.SetActive(active);
    }

    void HideEditorVisualImmediate()
    {
        if (editorOutline != null)
            editorOutline.enabled = false;

        SetEditorVisualActive(false);
    }

    public Color GetZoneColor()
    {
        switch (zone)
        {
            case PopUpDanmakuZone.Near:
                return new Color(1f, 0.82f, 0.2f, 0.95f);
            case PopUpDanmakuZone.Mid:
                return new Color(0.2f, 0.85f, 1f, 0.95f);
            default:
                return new Color(0.78f, 0.55f, 1f, 0.95f);
        }
    }

    public string GetZoneLabel()
    {
        switch (zone)
        {
            case PopUpDanmakuZone.Near: return "近景 Near";
            case PopUpDanmakuZone.Mid: return "中景 Mid";
            default: return "远景 Far";
        }
    }

    void OnDrawGizmos()
    {
        if (Application.isPlaying)
            return;

        Gizmos.color = GetZoneColor() * new Color(1f, 1f, 1f, 0.35f);
        Matrix4x4 oldMatrix = Gizmos.matrix;
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawCube(Vector3.zero, new Vector3(frameSize.x, frameSize.y, 0.01f));
        Gizmos.color = GetZoneColor();
        Gizmos.DrawWireCube(Vector3.zero, new Vector3(frameSize.x, frameSize.y, 0.01f));
        Gizmos.matrix = oldMatrix;
    }

    public static PopUpDanmakuZoneFrame CreateFrame(Transform parent, string name, PopUpDanmakuZone zone, Vector3 localPosition, Vector2 size, bool centerAnchor)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;

        PopUpDanmakuZoneFrame frame = go.AddComponent<PopUpDanmakuZoneFrame>();
        frame.zone = zone;
        frame.frameSize = size;
        frame.useCenterAnchor = centerAnchor;
        frame.EnsureAnchors();
        frame.SyncAnchorLocalPositions();
        frame.UpdateEditorVisual();
        return frame;
    }
}
