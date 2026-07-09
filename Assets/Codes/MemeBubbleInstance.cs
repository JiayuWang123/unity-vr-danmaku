using System;
using UnityEngine;

[RequireComponent(typeof(CanvasGroup))]
public class MemeBubbleInstance : MonoBehaviour
{
    enum Phase { PopIn, Dwell, FadeOut, Done }

    Phase phase;
    CanvasGroup canvasGroup;
    float elapsed;
    float dwellDuration;
    float fadeOutDuration;
    float baseWorldScale;
    Action<MemeBubbleInstance> onFinished;

    const float PopInDuration = 0.32f;
    const float OverScaleThreshold = 0.65f;
    const float OverScaleMul = 1.12f;
    const float StartScaleMul = 0.6f;

    public bool IsLeft { get; private set; }
    public float LayoutHalfHeight { get; private set; }

    public void Initialize(float dwell, float fadeOut, float worldScale, bool isLeft, float layoutHalfHeight, Action<MemeBubbleInstance> onDone)
    {
        IsLeft = isLeft;
        LayoutHalfHeight = layoutHalfHeight;
        canvasGroup = GetComponent<CanvasGroup>();
        dwellDuration = dwell;
        fadeOutDuration = Mathf.Max(0.01f, fadeOut);
        baseWorldScale = worldScale;
        onFinished = onDone;

        transform.localScale = Vector3.one * (baseWorldScale * StartScaleMul);
        if (canvasGroup) canvasGroup.alpha = 0f;

        elapsed = 0f;
        phase = Phase.PopIn;
    }

    void Update()
    {
        elapsed += Time.deltaTime;
        float targetScale = baseWorldScale;

        switch (phase)
        {
            case Phase.PopIn:
            {
                float t = Mathf.Clamp01(elapsed / PopInDuration);
                float scaleMul;
                if (t < OverScaleThreshold)
                {
                    float s = t / OverScaleThreshold;
                    scaleMul = Mathf.Lerp(StartScaleMul, OverScaleMul, 1f - (1f - s) * (1f - s));
                    if (canvasGroup) canvasGroup.alpha = s;
                }
                else
                {
                    float s = (t - OverScaleThreshold) / (1f - OverScaleThreshold);
                    scaleMul = Mathf.Lerp(OverScaleMul, 1f, s);
                    if (canvasGroup) canvasGroup.alpha = 1f;
                }

                transform.localScale = Vector3.one * (targetScale * scaleMul);

                if (t >= 1f)
                {
                    transform.localScale = Vector3.one * targetScale;
                    if (canvasGroup) canvasGroup.alpha = 1f;
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
                    canvasGroup.alpha = 1f - Mathf.Clamp01(elapsed / fadeOutDuration);
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
        Camera cam = Camera.main;
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
