using TMPro;
using UnityEngine;

[RequireComponent(typeof(CanvasGroup))]
public class PopUpDanmakuInstance : MonoBehaviour
{
    CanvasGroup canvasGroup;
    Canvas canvas;
    RectTransform rectTransform;
    TextMeshProUGUI label;
    PopUpDanmakuAnchor ownerAnchor;
    PopUpDanmakuSettings settings;
    PopUpDanmakuRecord record;
    PopUpDanmakuZone zone;
    public PopUpDanmakuZone Zone => zone;

    float fadeInDuration;
    float dwellDuration;
    float fadeOutDuration;
    float elapsed;
    enum Phase { FadeIn, Dwell, FadeOut, Done }
    Phase phase = Phase.FadeIn;

    public void Initialize(PopUpDanmakuRecord sourceRecord, PopUpDanmakuSettings config, PopUpDanmakuZone targetZone, PopUpDanmakuAnchor anchor)
    {
        settings = config;
        record = sourceRecord;
        zone = targetZone;
        ownerAnchor = anchor;
        fadeInDuration = Mathf.Max(0.01f, settings.fadeInDuration);
        dwellDuration = settings.GetDwell(zone);
        fadeOutDuration = Mathf.Max(0.01f, settings.fadeOutDuration);

        canvasGroup = GetComponent<CanvasGroup>();
        canvas = GetComponent<Canvas>();
        rectTransform = GetComponent<RectTransform>();
        label = GetComponentInChildren<TextMeshProUGUI>();

        ApplyLayoutSettings();
        ApplyTextVisualSettings();

        canvasGroup.alpha = phase == Phase.FadeIn ? 0f : 1f;
        elapsed = 0f;
        if (phase != Phase.Dwell && phase != Phase.FadeOut)
            phase = Phase.FadeIn;
    }

    public void RefreshVisualSettings()
    {
        if (settings == null)
            return;

        ApplyLayoutSettings();
        ApplyTextVisualSettings();
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
        }
    }

    void ApplyTextVisualSettings()
    {
        if (label == null || settings == null)
            return;

        if (record != null)
            label.text = record.弹幕内容;

        label.fontSize = settings.GetFontSize(zone);
        label.color = settings.BuildTextColor(zone, record);
        label.outlineWidth = settings.outlineWidth;
        label.outlineColor = settings.outlineColor;
        label.ForceMeshUpdate();
    }

    void LateUpdate()
    {
        Camera cam = Camera.main;
        if (cam != null)
            transform.rotation = Quaternion.LookRotation(transform.position - cam.transform.position);
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
                    Finish();
                }
                break;
        }
    }

    void Finish()
    {
        if (ownerAnchor != null)
            ownerAnchor.Release(this);
        Destroy(gameObject);
    }
}
