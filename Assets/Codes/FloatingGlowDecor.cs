using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 面板周边的悬浮发光方块/菱形装饰：缓慢上下漂浮 + 自转 + 呼吸式发光。
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class FloatingGlowDecor : MonoBehaviour
{
    public float floatAmplitude = 5f;
    public float floatSpeed = 1f;
    public float rotateSpeed = 16f;
    public float pulseSpeed = 1.3f;
    [Range(0f, 1f)] public float pulseMin = 0.45f;
    [Range(0f, 1f)] public float pulseMax = 1f;

    RectTransform rt;
    Image img;
    Vector2 basePos;
    float seed;
    float baseAlpha = 1f;

    void Awake()
    {
        rt = (RectTransform)transform;
        img = GetComponent<Image>();
        basePos = rt.anchoredPosition;
        seed = Random.value * 100f;
        if (img != null) baseAlpha = img.color.a;
    }

    void Update()
    {
        float t = Time.time * floatSpeed + seed;
        rt.anchoredPosition = basePos + new Vector2(0f, Mathf.Sin(t) * floatAmplitude);
        rt.Rotate(0f, 0f, rotateSpeed * Time.deltaTime);

        if (img != null)
        {
            float pulse = Mathf.Lerp(pulseMin, pulseMax, (Mathf.Sin(Time.time * pulseSpeed + seed) + 1f) * 0.5f);
            Color c = img.color;
            c.a = baseAlpha * pulse;
            img.color = c;
        }
    }
}
