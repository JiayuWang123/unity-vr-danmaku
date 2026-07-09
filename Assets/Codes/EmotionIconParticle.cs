using TMPro;
using UnityEngine;

/// <summary>
/// 单个情绪符号粒子：从 NearEmotion 区域上方生成，垂直下落并淡出。
/// 由 EmotionIconParticleController 创建并通过对象池复用。
/// </summary>
[RequireComponent(typeof(CanvasGroup))]
public class EmotionIconParticle : MonoBehaviour
{
    // ── 运行时状态 ──
    Vector3 velocity;
    float lifetime;
    float elapsed;
    float fadeInEnd;     // 淡入结束时间点（0~1 归一化）
    float fadeOutStart;  // 淡出开始时间点
    CanvasGroup cg;
    Transform cameraTransform;
    bool active;

    TextMeshProUGUI label;

    // ── 池接口 ──
    public bool IsActive => active;

    void Awake()
    {
        cg = GetComponent<CanvasGroup>();
        cg.alpha = 0f;
    }

    /// <summary>
    /// 激活粒子。在对象池中复用时调用。
    /// </summary>
    public void Activate(
        string icon,
        Color color,
        float worldScale,
        Vector3 worldPosition,
        Vector3 fallVelocity,
        float particleLifetime,
        float fadeInFraction,
        float fadeOutFraction,
        TMP_FontAsset font,
        int sortOrder,
        Transform cam)
    {
        gameObject.SetActive(true);
        transform.position = worldPosition;
        transform.rotation = Quaternion.identity;
        transform.localScale = Vector3.one * worldScale;

        velocity = fallVelocity;
        lifetime = Mathf.Max(0.1f, particleLifetime);
        elapsed = 0f;
        fadeInEnd = fadeInFraction;
        fadeOutStart = 1f - fadeOutFraction;
        cameraTransform = cam;
        active = true;

        if (cg == null) cg = GetComponent<CanvasGroup>();
        cg.alpha = 0f;

        EnsureLabel(font, sortOrder);
        label.text = icon;
        label.color = color;
    }

    void EnsureLabel(TMP_FontAsset font, int sortOrder)
    {
        if (label != null)
        {
            if (font != null) label.font = font;
            return;
        }

        var canvas = GetComponent<Canvas>();
        if (canvas == null)
        {
            canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.overrideSorting = true;
        }
        canvas.sortingOrder = sortOrder;

        var rt = GetComponent<RectTransform>();
        if (rt == null) rt = gameObject.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(48f, 48f);
        rt.pivot = new Vector2(0.5f, 0.5f);

        gameObject.AddComponent<UnityEngine.UI.CanvasScaler>();

        var textGo = new GameObject("Icon");
        textGo.transform.SetParent(transform, false);
        var textRt = textGo.AddComponent<RectTransform>();
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.offsetMin = Vector2.zero;
        textRt.offsetMax = Vector2.zero;

        label = textGo.AddComponent<TextMeshProUGUI>();
        label.fontSize = 28f;
        label.alignment = TextAlignmentOptions.Center;
        label.overflowMode = TextOverflowModes.Overflow;
        if (font != null) label.font = font;
    }

    void Update()
    {
        if (!active) return;

        elapsed += Time.deltaTime;
        float t = Mathf.Clamp01(elapsed / lifetime);

        // 透明度：淡入 → 保持 → 淡出
        float alpha;
        if (t < fadeInEnd)
            alpha = fadeInEnd > 0f ? t / fadeInEnd : 1f;
        else if (t < fadeOutStart)
            alpha = 1f;
        else
            alpha = fadeOutStart < 1f ? 1f - (t - fadeOutStart) / (1f - fadeOutStart) : 0f;

        if (cg != null) cg.alpha = alpha;

        // 位移
        transform.position += velocity * Time.deltaTime;

        // 生命结束
        if (elapsed >= lifetime)
            Recycle();
    }

    void LateUpdate()
    {
        if (!active || cameraTransform == null) return;

        // Billboard：朝向相机，只绕 Y 轴旋转（保持竖直）
        Vector3 toCamera = cameraTransform.position - transform.position;
        toCamera.y = 0f;
        if (toCamera.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.LookRotation(-toCamera.normalized, Vector3.up);
    }

    public void Recycle()
    {
        active = false;
        gameObject.SetActive(false);
        if (cg != null) cg.alpha = 0f;
    }
}
