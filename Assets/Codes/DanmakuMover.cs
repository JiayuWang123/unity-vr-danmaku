using UnityEngine;

public class DanmakuMover : MonoBehaviour
{
    public float speed = 400f; // 弹幕速度（现在的单位是 像素/秒，安全了！）
    private RectTransform rectTransform;

    void Start()
    {
        // 获取 UI 的专属组件
        rectTransform = GetComponent<RectTransform>();
    }

    void Update()
    {
        // 让 UI 坐标按像素往左平移，不再是按“米”平移了
        rectTransform.anchoredPosition += new Vector2(-speed * Time.deltaTime, 0);

        // 如果飞出了屏幕左边界（比如X坐标小于 -1500），就销毁它
        if (rectTransform.anchoredPosition.x < -1500f)
        {
            Destroy(gameObject);
        }
    }
}