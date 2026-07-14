using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

/// <summary>
/// 单独控制视频屏幕（VideoSurface）的位置/旋转/缩放。
/// screen 根物体保持不动，作为弹幕/信息条的坐标锚点；只有 VideoSurface 会随布局变化。
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-200)]
[AddComponentMenu("Stadium/Screen Layout Controller")]
public class ScreenLayoutController : MonoBehaviour
{
    [Header("视频表面")]
    [Tooltip("留空则自动查找或创建名为 VideoSurface 的子物体（仅移动它，弹幕不会跟着动）")]
    public Transform videoSurface;

    [Header("布局（相对 screen 的 Local 坐标，只作用于 VideoSurface）")]
    [Tooltip("勾选后，每次进入 Play 都使用下面的 Local 位置/旋转/缩放")]
    public bool useCustomLayout = false;

    public Vector3 customLocalPosition = Vector3.zero;
    public Vector3 customLocalEulerAngles = Vector3.zero;
    [Tooltip("只放大/缩小视频平面，不影响弹幕。1=默认大小；例如 1.2 大约大 20%")]
    public Vector3 customLocalScale = Vector3.one;

    public Transform VideoSurfaceTransform => videoSurface;

#if UNITY_EDITOR
    void OnValidate()
    {
        if (!useCustomLayout || !isActiveAndEnabled)
            return;

        EnsureVideoSurface();
        ApplyLayout();
    }
#endif

    void Awake()
    {
        EnsureVideoSurface();
        if (useCustomLayout)
            ApplyLayout();
    }

    public void EnsureVideoSurface()
    {
        if (videoSurface != null)
            return;

        Transform existing = transform.Find("VideoSurface");
        if (existing != null)
        {
            videoSurface = existing;
            return;
        }

        var go = new GameObject("VideoSurface");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;

        MoveOrCopyMeshComponents(go);
        videoSurface = go.transform;
    }

    void MoveOrCopyMeshComponents(GameObject target)
    {
        var srcFilter = GetComponent<MeshFilter>();
        var srcRenderer = GetComponent<MeshRenderer>();
        var srcCollider = GetComponent<MeshCollider>();

        if (srcFilter != null)
        {
            var dstFilter = target.GetComponent<MeshFilter>();
            if (dstFilter == null)
                dstFilter = target.AddComponent<MeshFilter>();
            dstFilter.sharedMesh = srcFilter.sharedMesh;
        }

        if (srcRenderer != null)
        {
            var dstRenderer = target.GetComponent<MeshRenderer>();
            if (dstRenderer == null)
                dstRenderer = target.AddComponent<MeshRenderer>();
            CopyRendererSettings(srcRenderer, dstRenderer);
            srcRenderer.enabled = false;
        }

        if (srcCollider != null)
        {
            var dstCollider = target.GetComponent<MeshCollider>();
            if (dstCollider == null)
                dstCollider = target.AddComponent<MeshCollider>();
            dstCollider.sharedMesh = srcCollider.sharedMesh;
            dstCollider.convex = srcCollider.convex;
            srcCollider.enabled = false;
        }
    }

    static void CopyRendererSettings(MeshRenderer src, MeshRenderer dst)
    {
        dst.sharedMaterials = src.sharedMaterials;
        dst.shadowCastingMode = src.shadowCastingMode;
        dst.receiveShadows = src.receiveShadows;
        dst.lightProbeUsage = src.lightProbeUsage;
        dst.reflectionProbeUsage = src.reflectionProbeUsage;
        dst.motionVectorGenerationMode = src.motionVectorGenerationMode;
        dst.allowOcclusionWhenDynamic = src.allowOcclusionWhenDynamic;
        dst.renderingLayerMask = src.renderingLayerMask;
        dst.sortingLayerID = src.sortingLayerID;
        dst.sortingOrder = src.sortingOrder;
    }

    public void ApplyLayout()
    {
        EnsureVideoSurface();
        if (videoSurface == null)
            return;

        videoSurface.localPosition = customLocalPosition;
        videoSurface.localEulerAngles = customLocalEulerAngles;
        videoSurface.localScale = customLocalScale;
    }

    [ContextMenu("Capture VideoSurface Transform To Custom Layout")]
    void CaptureCurrentTransform()
    {
        EnsureVideoSurface();
        if (videoSurface == null)
            return;

        customLocalPosition = videoSurface.localPosition;
        customLocalEulerAngles = videoSurface.localEulerAngles;
        customLocalScale = videoSurface.localScale;
        useCustomLayout = true;
    }

    [ContextMenu("Reset VideoSurface To Identity")]
    void ResetVideoSurfaceToIdentity()
    {
        EnsureVideoSurface();
        if (videoSurface == null)
            return;

        videoSurface.localPosition = Vector3.zero;
        videoSurface.localEulerAngles = Vector3.zero;
        videoSurface.localScale = Vector3.one;
        customLocalPosition = Vector3.zero;
        customLocalEulerAngles = Vector3.zero;
        customLocalScale = Vector3.one;
    }

#if UNITY_EDITOR
    [ContextMenu("Permanently Move Mesh To VideoSurface (Save Scene After)")]
    void PermanentlyMoveMeshToVideoSurface()
    {
        EnsureVideoSurface();
        if (videoSurface == null)
            return;

        MoveComponentIfPresent<MeshFilter>(videoSurface.gameObject);
        MoveComponentIfPresent<MeshRenderer>(videoSurface.gameObject);
        MoveComponentIfPresent<MeshCollider>(videoSurface.gameObject);
        EditorUtility.SetDirty(gameObject);
        EditorUtility.SetDirty(videoSurface.gameObject);
        if (gameObject.scene.IsValid())
            EditorSceneManager.MarkSceneDirty(gameObject.scene);

        Debug.Log("[ScreenLayout] 已将 Mesh 组件永久移到 VideoSurface。请保存场景。");
    }

    void MoveComponentIfPresent<T>(GameObject target) where T : Component
    {
        var src = GetComponent<T>();
        if (src == null || target.GetComponent<T>() != null)
            return;

        UnityEditorInternal.ComponentUtility.CopyComponent(src);
        UnityEditorInternal.ComponentUtility.PasteComponentAsNew(target);
        Undo.DestroyObjectImmediate(src);
    }
#endif
}
