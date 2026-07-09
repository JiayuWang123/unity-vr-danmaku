using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.Video;
using UnityEngine.XR.Interaction.Toolkit.UI;

[DisallowMultipleComponent]
public class VideoPlaybackUI : MonoBehaviour
{
    [Header("绑定")]
    public VideoPlayer videoPlayer;
    [Tooltip("面板挂在哪个物体下（默认 screen）")]
    public Transform attachTarget;
    public TMP_FontAsset fontAsset;

    [Header("布局（相对 attachTarget 本地坐标）")]
    public Vector3 localPosition = new Vector3(0f, -0.42f, 0.12f);
    public Vector3 localEulerAngles = Vector3.zero;
    public float canvasScale = 0.002f;
    public Vector2 panelSize = new Vector2(820f, 110f);

    Slider progressSlider;
    TextMeshProUGUI playPauseLabel;
    TextMeshProUGUI timeLabel;
    bool isDraggingSlider;
    bool built;

    void Awake()
    {
        EnsureEventSystem();
    }

    void Start()
    {
        if (videoPlayer == null)
        {
            GameObject screen = GameObject.Find("screen");
            if (screen != null)
                videoPlayer = screen.GetComponent<VideoPlayer>();
        }

        if (attachTarget == null)
        {
            GameObject screen = GameObject.Find("screen");
            if (screen != null)
                attachTarget = screen.transform;
        }

        if (!built)
            BuildUI();

        if (videoPlayer != null)
        {
            videoPlayer.prepareCompleted += OnVideoPrepared;
            if (!videoPlayer.isPrepared)
                videoPlayer.Prepare();
        }
    }

    void OnDestroy()
    {
        if (videoPlayer != null)
            videoPlayer.prepareCompleted -= OnVideoPrepared;

        if (attachTarget != null)
        {
            var existing = attachTarget.Find("VideoPlaybackPanel");
            if (existing != null)
                Destroy(existing.gameObject);
        }
    }

    void OnDisable()
    {
        if (!Application.isPlaying || videoPlayer == null)
            return;

        if (videoPlayer.isPlaying)
            videoPlayer.Stop();
    }

    void Update()
    {
        if (videoPlayer == null || progressSlider == null)
            return;

        if (!isDraggingSlider && videoPlayer.isPrepared && videoPlayer.length > 0.01d)
            progressSlider.SetValueWithoutNotify((float)(videoPlayer.time / videoPlayer.length));

        UpdateTimeLabel();
    }

    void OnVideoPrepared(VideoPlayer source)
    {
        if (progressSlider != null)
            progressSlider.SetValueWithoutNotify(0f);
        UpdateTimeLabel();
    }

    void TogglePlayPause()
    {
        if (videoPlayer == null)
            return;

        if (videoPlayer.isPlaying)
            videoPlayer.Pause();
        else
            videoPlayer.Play();

        UpdatePlayPauseLabel();
    }

    void OnSliderValueChanged(float normalized)
    {
        if (!isDraggingSlider || videoPlayer == null || !videoPlayer.isPrepared || videoPlayer.length <= 0.01d)
            return;

        videoPlayer.time = normalized * videoPlayer.length;
        UpdateTimeLabel();
    }

    void SetSliderDragging(bool dragging)
    {
        isDraggingSlider = dragging;
        if (!dragging && videoPlayer != null && videoPlayer.isPrepared && videoPlayer.length > 0.01d && progressSlider != null)
            videoPlayer.time = progressSlider.value * videoPlayer.length;
    }

    void UpdatePlayPauseLabel()
    {
        if (playPauseLabel == null || videoPlayer == null)
            return;

        playPauseLabel.text = videoPlayer.isPlaying ? "暂停" : "播放";
    }

    void UpdateTimeLabel()
    {
        if (timeLabel == null || videoPlayer == null)
            return;

        double current = videoPlayer.isPrepared ? videoPlayer.time : 0d;
        double total = videoPlayer.isPrepared ? videoPlayer.length : 0d;
        timeLabel.text = $"{FormatTime(current)} / {FormatTime(total)}";
    }

    static string FormatTime(double seconds)
    {
        if (seconds < 0d || double.IsNaN(seconds))
            seconds = 0d;

        int total = Mathf.Max(0, Mathf.FloorToInt((float)seconds));
        int minutes = total / 60;
        int secs = total % 60;
        return $"{minutes:00}:{secs:00}";
    }

    void BuildUI()
    {
        if (attachTarget == null)
        {
            Debug.LogWarning("VideoPlaybackUI: attachTarget is missing.");
            return;
        }

        var existing = attachTarget.Find("VideoPlaybackPanel");
        if (existing != null)
            Destroy(existing.gameObject);

        var panelRoot = new GameObject("VideoPlaybackPanel", typeof(RectTransform));
        panelRoot.transform.SetParent(attachTarget, false);
        panelRoot.transform.localPosition = localPosition;
        panelRoot.transform.localRotation = Quaternion.Euler(localEulerAngles);
        panelRoot.transform.localScale = Vector3.one;

        var canvas = panelRoot.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 300;

        panelRoot.AddComponent<CanvasScaler>();
        panelRoot.AddComponent<GraphicRaycaster>();
        panelRoot.AddComponent<TrackedDeviceGraphicRaycaster>();

        var panelRect = panelRoot.GetComponent<RectTransform>();
        panelRect.sizeDelta = panelSize;
        panelRect.localScale = Vector3.one * canvasScale;

        var background = CreateUiObject<Image>("Background", panelRect);
        Stretch(background.rectTransform);
        background.color = new Color(0.05f, 0.05f, 0.08f, 0.82f);

        var playButton = CreateButton("PlayPauseButton", panelRect, new Vector2(70f, 0f), new Vector2(96f, 72f), out playPauseLabel);
        playPauseLabel.text = "暂停";
        playButton.onClick.AddListener(TogglePlayPause);

        var timeGo = CreateUiObject<TextMeshProUGUI>("TimeLabel", panelRect);
        var timeRect = timeGo.rectTransform;
        timeRect.anchorMin = new Vector2(0f, 0.5f);
        timeRect.anchorMax = new Vector2(0f, 0.5f);
        timeRect.pivot = new Vector2(0f, 0.5f);
        timeRect.anchoredPosition = new Vector2(132f, 0f);
        timeRect.sizeDelta = new Vector2(120f, 40f);
        timeLabel = timeGo;
        timeLabel.fontSize = 22f;
        timeLabel.alignment = TextAlignmentOptions.MidlineLeft;
        timeLabel.color = Color.white;
        ApplyFont(timeLabel);

        progressSlider = CreateSlider(panelRect);
        var dragHandler = progressSlider.gameObject.AddComponent<SliderDragHandler>();
        dragHandler.DragStateChanged += SetSliderDragging;
        progressSlider.onValueChanged.AddListener(OnSliderValueChanged);

        built = true;
        UpdatePlayPauseLabel();
        UpdateTimeLabel();
    }

    Slider CreateSlider(RectTransform parent)
    {
        var sliderGo = new GameObject("ProgressSlider", typeof(RectTransform), typeof(Slider));
        sliderGo.transform.SetParent(parent, false);
        var sliderRect = sliderGo.GetComponent<RectTransform>();
        sliderRect.anchorMin = new Vector2(0f, 0.5f);
        sliderRect.anchorMax = new Vector2(1f, 0.5f);
        sliderRect.pivot = new Vector2(0.5f, 0.5f);
        sliderRect.anchoredPosition = new Vector2(70f, 0f);
        sliderRect.sizeDelta = new Vector2(-290f, 36f);

        var background = CreateUiObject<Image>("Background", sliderRect);
        Stretch(background.rectTransform);
        background.color = new Color(1f, 1f, 1f, 0.18f);

        var fillArea = new GameObject("Fill Area", typeof(RectTransform));
        fillArea.transform.SetParent(sliderRect, false);
        var fillAreaRect = fillArea.GetComponent<RectTransform>();
        fillAreaRect.anchorMin = new Vector2(0f, 0.25f);
        fillAreaRect.anchorMax = new Vector2(1f, 0.75f);
        fillAreaRect.offsetMin = new Vector2(8f, 0f);
        fillAreaRect.offsetMax = new Vector2(-8f, 0f);

        var fill = CreateUiObject<Image>("Fill", fillAreaRect);
        Stretch(fill.rectTransform);
        fill.color = new Color(0.35f, 0.75f, 1f, 0.95f);

        var handleSlideArea = new GameObject("Handle Slide Area", typeof(RectTransform));
        handleSlideArea.transform.SetParent(sliderRect, false);
        var handleSlideAreaRect = handleSlideArea.GetComponent<RectTransform>();
        Stretch(handleSlideAreaRect);

        var handleGo = new GameObject("Handle", typeof(RectTransform), typeof(Image));
        handleGo.transform.SetParent(handleSlideAreaRect, false);
        var handleRect = handleGo.GetComponent<RectTransform>();
        handleRect.sizeDelta = new Vector2(18f, 36f);
        var handleImage = handleGo.GetComponent<Image>();
        handleImage.color = Color.white;

        var slider = sliderGo.GetComponent<Slider>();
        slider.fillRect = fill.rectTransform;
        slider.handleRect = handleRect;
        slider.targetGraphic = handleImage;
        slider.direction = Slider.Direction.LeftToRight;
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.value = 0f;
        return slider;
    }

    Button CreateButton(string name, RectTransform parent, Vector2 anchoredPosition, Vector2 size, out TextMeshProUGUI label)
    {
        var buttonGo = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        buttonGo.transform.SetParent(parent, false);

        var buttonRect = buttonGo.GetComponent<RectTransform>();
        buttonRect.anchorMin = new Vector2(0f, 0.5f);
        buttonRect.anchorMax = new Vector2(0f, 0.5f);
        buttonRect.pivot = new Vector2(0f, 0.5f);
        buttonRect.anchoredPosition = anchoredPosition;
        buttonRect.sizeDelta = size;

        buttonGo.GetComponent<Image>().color = new Color(0.2f, 0.22f, 0.28f, 0.95f);
        var button = buttonGo.GetComponent<Button>();

        var labelGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        labelGo.transform.SetParent(buttonRect, false);
        var labelRect = labelGo.GetComponent<RectTransform>();
        Stretch(labelRect);
        label = labelGo.GetComponent<TextMeshProUGUI>();
        label.fontSize = 24f;
        label.alignment = TextAlignmentOptions.Center;
        label.color = Color.white;
        ApplyFont(label);

        return button;
    }

    TComponent CreateUiObject<TComponent>(string name, Transform parent) where TComponent : Component
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go.AddComponent<TComponent>();
    }

    void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    void ApplyFont(TextMeshProUGUI text)
    {
        if (fontAsset != null)
            text.font = fontAsset;
    }

    static void EnsureEventSystem()
    {
        var modules = UnityEngine.Object.FindObjectsOfType<XRUIInputModule>(true);
        if (modules.Length > 0)
            return;

        var eventSystems = UnityEngine.Object.FindObjectsOfType<EventSystem>(true);
        if (eventSystems.Length > 0)
        {
            var module = eventSystems[0].gameObject.AddComponent<XRUIInputModule>();
            module.enableMouseInput = true;
            module.enableTouchInput = true;
            module.enableBuiltinActionsAsFallback = true;

            for (int i = 1; i < eventSystems.Length; i++)
                eventSystems[i].gameObject.SetActive(false);
            return;
        }

        var roots = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
        {
            if (roots[i].name == "EventSystem")
                roots[i].SetActive(false);
        }

        var eventSystemGo = new GameObject("EventSystem");
        eventSystemGo.AddComponent<EventSystem>();
        var createdModule = eventSystemGo.AddComponent<XRUIInputModule>();
        createdModule.enableMouseInput = true;
        createdModule.enableTouchInput = true;
        createdModule.enableBuiltinActionsAsFallback = true;
    }

    sealed class SliderDragHandler : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        public event Action<bool> DragStateChanged;

        public void OnPointerDown(PointerEventData eventData) => DragStateChanged?.Invoke(true);

        public void OnPointerUp(PointerEventData eventData) => DragStateChanged?.Invoke(false);

        public void OnPointerExit(PointerEventData eventData)
        {
            if (!eventData.dragging)
                DragStateChanged?.Invoke(false);
        }
    }
}
