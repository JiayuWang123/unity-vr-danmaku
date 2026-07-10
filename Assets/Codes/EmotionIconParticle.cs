using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 单个情绪符号粒子：从 NearEmotion 区域上方生成，垂直下落并淡出。
/// 由 EmotionIconParticleController 创建并通过对象池复用。
/// </summary>
[RequireComponent(typeof(CanvasGroup))]
public class EmotionIconParticle : MonoBehaviour
{
    Vector3 velocity;
    float lifetime;
    float elapsed;
    float fadeInEnd;
    float fadeOutStart;
    CanvasGroup cg;
    Transform cameraTransform;
    bool active;

    Image iconImage;

    public bool IsActive => active;

    void Awake()
    {
        cg = GetComponent<CanvasGroup>();
        cg.alpha = 0f;
    }

    public void Activate(
        Sprite sprite,
        Color color,
        float worldScale,
        Vector3 worldPosition,
        Vector3 fallVelocity,
        float particleLifetime,
        float fadeInFraction,
        float fadeOutFraction,
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

        EnsureIconImage(sortOrder);
        iconImage.sprite = sprite;
        iconImage.color = color;
        iconImage.enabled = sprite != null;
    }

    void EnsureIconImage(int sortOrder)
    {
        if (iconImage != null)
            return;

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
        rt.sizeDelta = new Vector2(64f, 64f);
        rt.pivot = new Vector2(0.5f, 0.5f);

        iconImage = GetComponent<Image>();
        if (iconImage == null)
            iconImage = gameObject.AddComponent<Image>();

        iconImage.raycastTarget = false;
        iconImage.preserveAspect = true;
    }

    void Update()
    {
        if (!active) return;

        elapsed += Time.deltaTime;
        float t = Mathf.Clamp01(elapsed / lifetime);

        float alpha;
        if (t < fadeInEnd)
            alpha = fadeInEnd > 0f ? t / fadeInEnd : 1f;
        else if (t < fadeOutStart)
            alpha = 1f;
        else
            alpha = fadeOutStart < 1f ? 1f - (t - fadeOutStart) / (1f - fadeOutStart) : 0f;

        if (cg != null) cg.alpha = alpha;

        transform.position += velocity * Time.deltaTime;

        if (elapsed >= lifetime)
            Recycle();
    }

    void LateUpdate()
    {
        if (!active || cameraTransform == null) return;

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
