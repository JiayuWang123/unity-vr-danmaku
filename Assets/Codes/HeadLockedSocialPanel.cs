using System.Collections.Generic;
using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
public class HeadLockedSocialPanel : MonoBehaviour
{
    [Header("跟随")]
    public Transform cameraTransform;
    public Vector3 localOffset = new Vector3(0f, -0.35f, 0.75f);
    public bool expanded;

    [Header("显示")]
    public int maxVisibleItems = 4;
    public float itemLifetimeSeconds = 8f;
    [Range(0f, 1f)] public float collapsedAlpha = 0.35f;
    [Range(0f, 1f)] public float expandedAlpha = 0.92f;
    public TMP_FontAsset fontAsset;

    CanvasGroup canvasGroup;
    TextMeshProUGUI statusLabel;
    readonly Queue<SocialItem> queue = new Queue<SocialItem>();
    readonly List<SocialItem> visibleItems = new List<SocialItem>();

    struct SocialItem
    {
        public string text;
        public float expireTime;
    }

    void Awake()
    {
        if (cameraTransform == null && Camera.main != null)
            cameraTransform = Camera.main.transform;

        EnsureUi();
        RefreshVisual();
    }

    void LateUpdate()
    {
        if (cameraTransform == null)
            return;

        transform.SetPositionAndRotation(
            cameraTransform.TransformPoint(localOffset),
            Quaternion.LookRotation(transform.position - cameraTransform.position, Vector3.up));

        CleanupExpired();
        RefreshVisual();
    }

    public void Enqueue(SemanticDanmakuRecord record)
    {
        if (record == null || string.IsNullOrWhiteSpace(record.text))
            return;

        queue.Enqueue(new SocialItem
        {
            text = record.text,
            expireTime = Time.time + itemLifetimeSeconds
        });

        while (queue.Count > maxVisibleItems * 3)
            queue.Dequeue();
    }

    public void ToggleExpanded()
    {
        expanded = !expanded;
        RefreshVisual();
    }

    void CleanupExpired()
    {
        float now = Time.time;
        while (queue.Count > 0)
        {
            SocialItem peek = queue.Peek();
            if (peek.expireTime > now)
                break;
            queue.Dequeue();
        }
    }

    void RefreshVisual()
    {
        if (statusLabel == null || canvasGroup == null)
            return;

        visibleItems.Clear();
        foreach (SocialItem item in queue)
        {
            visibleItems.Add(item);
            if (visibleItems.Count >= maxVisibleItems)
                break;
        }

        if (!expanded)
        {
            canvasGroup.alpha = collapsedAlpha;
            statusLabel.text = queue.Count > 0 ? $"社交弹幕 ({queue.Count})" : "社交弹幕";
            return;
        }

        canvasGroup.alpha = expandedAlpha;
        if (visibleItems.Count == 0)
        {
            statusLabel.text = "暂无社交弹幕";
            return;
        }

        var lines = new List<string>(visibleItems.Count);
        for (int i = 0; i < visibleItems.Count; i++)
            lines.Add(visibleItems[i].text);

        statusLabel.text = string.Join("\n", lines);
    }

    void EnsureUi()
    {
        if (canvasGroup != null)
            return;

        Canvas canvas = gameObject.GetComponent<Canvas>();
        if (canvas == null)
            canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;

        RectTransform rect = gameObject.GetComponent<RectTransform>();
        if (rect == null)
            rect = gameObject.AddComponent<RectTransform>();
        rect.sizeDelta = new Vector2(520f, 180f);
        rect.localScale = Vector3.one * 0.002f;

        canvasGroup = gameObject.GetComponent<CanvasGroup>();
        if (canvasGroup == null)
            canvasGroup = gameObject.AddComponent<CanvasGroup>();

        gameObject.AddComponent<UnityEngine.UI.GraphicRaycaster>();

        GameObject textGo = new GameObject("StatusLabel");
        textGo.transform.SetParent(transform, false);
        RectTransform textRect = textGo.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(12f, 12f);
        textRect.offsetMax = new Vector2(-12f, -12f);

        statusLabel = textGo.AddComponent<TextMeshProUGUI>();
        statusLabel.fontSize = 24f;
        statusLabel.alignment = TextAlignmentOptions.TopLeft;
        statusLabel.color = Color.white;
        if (fontAsset != null)
            statusLabel.font = fontAsset;
    }
}
