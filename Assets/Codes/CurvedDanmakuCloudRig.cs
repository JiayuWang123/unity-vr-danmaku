using UnityEngine;

[ExecuteAlways]
[DisallowMultipleComponent]
public class CurvedDanmakuCloudRig : MonoBehaviour
{
    public CurvedDanmakuSurfaceLayer nearEmotionLayer;
    public CurvedDanmakuSurfaceLayer midInfoLayer;
    public CurvedDanmakuSurfaceLayer farInfoLayer;

    [Header("自动绑定（按子物体名称）")]
    public string nearLayerObjectName = "NearEmotionLayer";
    public string midLayerObjectName = "MidInfoLayer";
    public string farLayerObjectName = "FarInfoLayer";

    [Header("缩放修正")]
    [Tooltip("勾选后会自动抵消 screen（或任意父物体）的非等比缩放，" +
             "让下面各层的 Radius / Position 真正对应米数，避免曲面被拉伸、弹幕离得过近")]
    public bool autoNormalizeParentScale = true;

    void Awake()
    {
        NormalizeParentScale();
        ResolveLayers();
    }

    void OnEnable()
    {
        NormalizeParentScale();
    }

    void OnValidate()
    {
        NormalizeParentScale();
        ResolveLayers();
    }

    public void NormalizeParentScale()
    {
        if (!autoNormalizeParentScale)
            return;

        Transform parent = transform.parent;
        if (parent == null)
        {
            if (transform.localScale != Vector3.one)
                transform.localScale = Vector3.one;
            return;
        }

        Vector3 parentScale = parent.lossyScale;
        Vector3 targetScale = new Vector3(
            Mathf.Abs(parentScale.x) > 0.0001f ? 1f / parentScale.x : 1f,
            Mathf.Abs(parentScale.y) > 0.0001f ? 1f / parentScale.y : 1f,
            Mathf.Abs(parentScale.z) > 0.0001f ? 1f / parentScale.z : 1f);

        if ((transform.localScale - targetScale).sqrMagnitude > 0.0000001f)
            transform.localScale = targetScale;
    }

    public void ResolveLayers()
    {
        if (nearEmotionLayer == null)
            nearEmotionLayer = FindLayer(nearLayerObjectName, CurvedCloudLayerKind.NearEmotion);
        if (midInfoLayer == null)
            midInfoLayer = FindLayer(midLayerObjectName, CurvedCloudLayerKind.MidInfo);
        if (farInfoLayer == null)
            farInfoLayer = FindLayer(farLayerObjectName, CurvedCloudLayerKind.FarInfo);
    }

    CurvedDanmakuSurfaceLayer FindLayer(string objectName, CurvedCloudLayerKind kind)
    {
        Transform child = transform.Find(objectName);
        if (child == null)
            return null;

        CurvedDanmakuSurfaceLayer layer = child.GetComponent<CurvedDanmakuSurfaceLayer>();
        if (layer == null)
            layer = child.gameObject.AddComponent<CurvedDanmakuSurfaceLayer>();

        layer.layerKind = kind;
        if (layer.useAutoPreset && !Application.isPlaying)
            layer.ApplyLayerKindPreset();

        return layer;
    }

    public void ApplyAllLayerPresets(bool force = false)
    {
        ResolveLayers();
        ApplyPresetIfNeeded(nearEmotionLayer, force);
        ApplyPresetIfNeeded(midInfoLayer, force);
        ApplyPresetIfNeeded(farInfoLayer, force);
    }

    static void ApplyPresetIfNeeded(CurvedDanmakuSurfaceLayer layer, bool force)
    {
        if (layer == null)
            return;

        if (force || layer.useAutoPreset)
            layer.ApplyLayerKindPreset();
    }

    public CurvedDanmakuSurfaceLayer GetLayer(CurvedCloudLayerKind kind)
    {
        switch (kind)
        {
            case CurvedCloudLayerKind.NearEmotion:
                return nearEmotionLayer;
            case CurvedCloudLayerKind.FarInfo:
                return farInfoLayer;
            default:
                return midInfoLayer;
        }
    }

    public CurvedDanmakuSurfaceLayer GetLayerForSemantic(DanmakuSemanticLayer semanticLayer, DanmakuSemanticCategory category)
    {
        if (semanticLayer == DanmakuSemanticLayer.Emotion)
            return nearEmotionLayer;

        if (semanticLayer != DanmakuSemanticLayer.Info)
            return midInfoLayer;

        return category == DanmakuSemanticCategory.MatchHistory || category == DanmakuSemanticCategory.MemeJoke
            ? farInfoLayer != null ? farInfoLayer : midInfoLayer
            : midInfoLayer != null ? midInfoLayer : farInfoLayer;
    }
}
