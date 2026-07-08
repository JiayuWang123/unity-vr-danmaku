using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// 按下时轻微缩小、抬起/移出时恢复，给按钮一个明显的按压反馈。
/// </summary>
public class UiPressEffect : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
{
    [Range(0.7f, 0.99f)] public float pressedScale = 0.92f;
    public float speed = 20f;

    Vector3 normalScale;
    float targetMul = 1f;
    float currentMul = 1f;

    void Awake()
    {
        normalScale = transform.localScale;
    }

    public void OnPointerDown(PointerEventData eventData) => targetMul = pressedScale;
    public void OnPointerUp(PointerEventData eventData) => targetMul = 1f;
    public void OnPointerExit(PointerEventData eventData) => targetMul = 1f;

    void Update()
    {
        if (Mathf.Approximately(currentMul, targetMul)) return;
        currentMul = Mathf.MoveTowards(currentMul, targetMul, speed * Time.deltaTime);
        transform.localScale = normalScale * currentMul;
    }
}
