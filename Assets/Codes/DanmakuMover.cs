using UnityEngine;

public class DanmakuMover : MonoBehaviour
{
    public RectTransform canvas;
    public float speed = 420f;
    public float destroyPadding = 240f;

    private RectTransform rectTransform;

    private void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
    }

    private void Update()
    {
        if (rectTransform == null)
            return;

        rectTransform.anchoredPosition += Vector2.left * speed * Time.deltaTime;

        float leftBoundary = -1600f;
        if (canvas != null)
            leftBoundary = -canvas.rect.width * 0.5f - destroyPadding;

        if (rectTransform.anchoredPosition.x < leftBoundary)
            Destroy(gameObject);
    }
}
