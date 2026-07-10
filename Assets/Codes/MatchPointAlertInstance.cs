using System;
using UnityEngine;

/// <summary>
/// 单条赛点提醒横条的生命周期：弹出（缩放+淡入）→ 停留 → 淡出 → 通知控制器 → 销毁。
/// 现在是一条一条排队弹出的，不存在同时多条，所以不需要格子/槲位的概念。
/// </summary>
[RequireComponent(typeof(CanvasGroup))]
public class MatchPointAlertInstance : MonoBehaviour
{
    enum Phase { PopIn, Dwell, FadeOut, Done }

    Phase phase;
    CanvasGroup canvasGroup;
    float elapsed;
    float fadeInDuration;
    float dwellDuration;
    float fadeOutDuration;
    float maxAlpha;
    float baseWorldScale;
    Action<MatchPointAlertInstance> onFinished;

    const float PopInScaleMul = 0.75f;

    public void Initialize(
        float fadeIn,
        float dwell,
        float fadeOut,
        float worldScale,
        float maxAlphaValue,
        Action<MatchPointAlertInstance> onDone)
    {
        canvasGroup = GetComponent<CanvasGroup>();
        fadeInDuration = Mathf.Max(0.01f, fadeIn);
        dwellDuration = Mathf.Max(0f, dwell);
        fadeOutDuration = Mathf.Max(0.01f, fadeOut);
        baseWorldScale = worldScale;
        maxAlpha = Mathf.Clamp01(maxAlphaValue);
        onFinished = onDone;

        transform.localScale = Vector3.one * (baseWorldScale * PopInScaleMul);
        if (canvasGroup) canvasGroup.alpha = 0f;

        elapsed = 0f;
        phase = Phase.PopIn;
    }

    void Update()
    {
        elapsed += Time.deltaTime;

        switch (phase)
        {
            case Phase.PopIn:
            {
                float t = Mathf.Clamp01(elapsed / fadeInDuration);
                float eased = 1f - (1f - t) * (1f - t);
                if (canvasGroup) canvasGroup.alpha = eased * maxAlpha;
                transform.localScale = Vector3.one * Mathf.LerpUnclamped(baseWorldScale * PopInScaleMul, baseWorldScale * 1.04f, eased);

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

    void Finish()
    {
        onFinished?.Invoke(this);
        UiSpriteCleanupUtil.DestroyGeneratedSprites(gameObject);
        Destroy(gameObject);
    }
}
