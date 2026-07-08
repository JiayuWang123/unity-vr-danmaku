using UnityEngine;

/// <summary>
/// 聊天列表单条消息的淡入动画。
/// </summary>
[RequireComponent(typeof(CanvasGroup))]
public class SocializationChatItem : MonoBehaviour
{
    const float FadeInDuration = 0.3f;

    CanvasGroup canvasGroup;
    float elapsed;
    bool playing;

    void Awake()
    {
        canvasGroup = GetComponent<CanvasGroup>();
    }

    public void Play()
    {
        if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();
        canvasGroup.alpha = 0f;
        elapsed = 0f;
        playing = true;
    }

    void Update()
    {
        if (!playing) return;

        elapsed += Time.deltaTime;
        float t = Mathf.Clamp01(elapsed / FadeInDuration);
        canvasGroup.alpha = 1f - (1f - t) * (1f - t);

        if (t >= 1f)
        {
            canvasGroup.alpha = 1f;
            playing = false;
        }
    }
}
