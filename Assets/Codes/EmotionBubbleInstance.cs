using System;
using UnityEngine;

/// <summary>
/// 单条情绪弹幕的生命周期：淡入 → （边上升边）暂留 → 淡出上升 → 销毁。
/// 整个过程中持续匀速上升，模拟气泡从下方升起的感觉。
/// </summary>
[RequireComponent(typeof(CanvasGroup))]
public class EmotionBubbleInstance : MonoBehaviour
{
    enum Phase { FadeIn, Dwell, FadeOut, Done }

    Phase phase;
    CanvasGroup canvasGroup;
    float elapsed;
    float fadeInDuration;
    float dwellDuration;
    float fadeOutDuration;
    float maxAlpha;
    float riseSpeed;
    float baseWorldScale;
    Action<EmotionBubbleInstance> onFinished;

    const float PopInScaleMul = 0.7f;

    public float HalfWidth { get; private set; }
    public float HalfHeight { get; private set; }

    public void Initialize(
        float fadeIn,
        float dwell,
        float fadeOut,
        float worldScale,
        float maxAlphaValue,
        float riseSpeedValue,
        float halfWidth,
        float halfHeight,
        Action<EmotionBubbleInstance> onDone)
    {
        canvasGroup = GetComponent<CanvasGroup>();
        fadeInDuration = Mathf.Max(0.01f, fadeIn);
        dwellDuration = Mathf.Max(0f, dwell);
        fadeOutDuration = Mathf.Max(0.01f, fadeOut);
        baseWorldScale = worldScale;
        maxAlpha = Mathf.Clamp01(maxAlphaValue);
        riseSpeed = riseSpeedValue;
        HalfWidth = halfWidth;
        HalfHeight = halfHeight;
        onFinished = onDone;

        transform.localScale = Vector3.one * (baseWorldScale * PopInScaleMul);
        if (canvasGroup) canvasGroup.alpha = 0f;

        elapsed = 0f;
        phase = Phase.FadeIn;
    }

    void Update()
    {
        float dt = Time.deltaTime;
        elapsed += dt;

        // 整个生命周期持续上升
        Vector3 pos = transform.localPosition;
        pos.y += riseSpeed * dt;
        transform.localPosition = pos;

        switch (phase)
        {
            case Phase.FadeIn:
            {
                float t = Mathf.Clamp01(elapsed / fadeInDuration);
                float eased = 1f - (1f - t) * (1f - t);
                if (canvasGroup) canvasGroup.alpha = eased * maxAlpha;
                transform.localScale = Vector3.one * Mathf.Lerp(baseWorldScale * PopInScaleMul, baseWorldScale, eased);

                if (t >= 1f)
                {
                    transform.localScale = Vector3.one * baseWorldScale;
                    if (canvasGroup) canvasGroup.alpha = maxAlpha;
                    phase = Phase.Dwell;
                    elapsed = 0f;
                }
                break;
            }
            case Phase.Dwell:
                if (elapsed >= dwellDuration)
                {
                    phase = Phase.FadeOut;
                    elapsed = 0f;
                }
                break;
            case Phase.FadeOut:
                if (canvasGroup)
                    canvasGroup.alpha = (1f - Mathf.Clamp01(elapsed / fadeOutDuration)) * maxAlpha;
                if (elapsed >= fadeOutDuration)
                {
                    phase = Phase.Done;
                    Finish();
                }
                break;
        }
    }

    void LateUpdate()
    {
        Camera cam = DanmakuCameraUtility.ResolveViewCamera();
        if (cam != null)
            transform.rotation = Quaternion.LookRotation(transform.position - cam.transform.position);
    }

    void Finish()
    {
        onFinished?.Invoke(this);
        UiSpriteCleanupUtil.DestroyGeneratedSprites(gameObject);
        Destroy(gameObject);
    }
}
