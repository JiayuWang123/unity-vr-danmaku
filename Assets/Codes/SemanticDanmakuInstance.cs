using TMPro;
using UnityEngine;

[RequireComponent(typeof(CanvasGroup))]
public class SemanticDanmakuInstance : MonoBehaviour
{
    CanvasGroup canvasGroup;
    Canvas canvas;
    RectTransform rectTransform;
    TextMeshProUGUI label;
    SemanticDanmakuSettings settings;
    SemanticDanmakuRecord record;
    CurvedCloudLayerKind layerKind;
    CurvedDanmakuSurfaceLayer surfaceLayer;
    float clusterU;
    float clusterV;
    float clusterRadiusOffset;

    float fadeInDuration;
    float dwellDuration;
    float fadeOutDuration;
    float elapsed;
    enum Phase { FadeIn, Dwell, FadeOut, Done }
    Phase phase = Phase.FadeIn;

    public CurvedCloudLayerKind LayerKind => layerKind;
    public DanmakuSemanticLayer SemanticLayer => record != null ? record.semanticLayer : DanmakuSemanticLayer.Info;

    public void Initialize(
        SemanticDanmakuRecord sourceRecord,
        SemanticDanmakuSettings config,
        CurvedDanmakuSurfaceLayer surface,
        CurvedCloudLayerKind cloudLayerKind,
        float u,
        float v,
        float radiusOffset = 0f)
    {
        settings = config;
        record = sourceRecord;
        surfaceLayer = surface;
        layerKind = cloudLayerKind;
        clusterRadiusOffset = radiusOffset;
        if (surface != null)
        {
            float spreadU = Mathf.Max(surface.clusterSpreadU, 0.06f);
            float spreadV = Mathf.Max(surface.clusterSpreadV, 0.05f);
            clusterU = surface.ClampU(Mathf.Clamp01(u + Random.Range(-spreadU, spreadU)));
            clusterV = Mathf.Clamp01(v + Random.Range(-spreadV, spreadV));
        }
        else
        {
            clusterU = u;
            clusterV = v;
        }

        fadeInDuration = Mathf.Max(0.01f, settings.fadeInDuration);
        dwellDuration = settings.GetDwell(layerKind);
        fadeOutDuration = Mathf.Max(0.01f, settings.fadeOutDuration);

        canvasGroup = GetComponent<CanvasGroup>();
        canvas = GetComponent<Canvas>();
        rectTransform = GetComponent<RectTransform>();
        label = GetComponentInChildren<TextMeshProUGUI>();

        ApplyLayoutSettings();
        ApplyTextVisualSettings();
        UpdateWorldPose();

        canvasGroup.alpha = 0f;
        elapsed = 0f;
        phase = Phase.FadeIn;
    }

    void ApplyLayoutSettings()
    {
        if (rectTransform != null && settings != null)
        {
            rectTransform.sizeDelta = settings.labelSize;
            rectTransform.localScale = Vector3.one * settings.worldLabelScale;
        }

        if (canvas != null && settings != null)
        {
            canvas.overrideSorting = true;
            canvas.sortingOrder = settings.canvasSortingOrder;
            Camera viewCamera = DanmakuCameraUtility.ResolveViewCamera();
            if (viewCamera != null)
                canvas.worldCamera = viewCamera;
        }
    }

    void ApplyTextVisualSettings()
    {
        if (label == null || settings == null)
            return;

        if (record != null)
            label.text = record.text;

        label.fontSize = settings.GetFontSize(layerKind);
        label.color = settings.BuildTextColor(record, layerKind);
        label.ForceMeshUpdate(true, true);
        label.fontStyle = FontStyles.Bold;
    }

    void UpdateWorldPose()
    {
        if (surfaceLayer == null)
            return;

        transform.localPosition = surfaceLayer.GetLocalPosition(clusterU, clusterV, clusterRadiusOffset);

        Camera viewCamera = DanmakuCameraUtility.ResolveViewCamera();
        Vector3 worldPosition = transform.position;
        transform.rotation = surfaceLayer.GetBillboardRotation(worldPosition, viewCamera);
    }

    void LateUpdate()
    {
        UpdateWorldPose();
    }

    void Update()
    {
        elapsed += Time.deltaTime;

        switch (phase)
        {
            case Phase.FadeIn:
                canvasGroup.alpha = Mathf.Clamp01(elapsed / fadeInDuration);
                if (elapsed >= fadeInDuration)
                {
                    phase = Phase.Dwell;
                    elapsed = 0f;
                    canvasGroup.alpha = 1f;
                }
                break;

            case Phase.Dwell:
                if (elapsed >= dwellDuration)
                {
                    phase = Phase.FadeOut;
                    elapsed = 0f;
                }
                break;

            case Phase.FadeOut:
                canvasGroup.alpha = 1f - Mathf.Clamp01(elapsed / fadeOutDuration);
                if (elapsed >= fadeOutDuration)
                {
                    phase = Phase.Done;
                    Destroy(gameObject);
                }
                break;
        }
    }
}
