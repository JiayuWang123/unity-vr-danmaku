using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.Video;

[DefaultExecutionOrder(300)]
public class VideoProgressBarController : MonoBehaviour
{
    [Header("Timeline")]
    public VideoPlayer videoPlayer;
    public AudioDanmakuController audioDanmakuController;
    public DanmakuPlaybackController danmakuPlaybackController;

    [Header("Runtime UI")]
    public bool createRuntimeUi = true;
    public Slider progressSlider;
    public Text timeLabel;
    public string canvasName = "VideoProgressCanvas";
    public string sliderObjectName = "VideoProgressSlider";
    public float bottomOffset = 34f;
    public float width = 920f;
    public float height = 44f;
    public bool showTimeLabel = true;

    [Header("Keyboard Seek")]
    public bool enableKeyboardShortcuts = true;
    public float keyboardSeekStepSeconds = 10f;

    private bool updatingSlider;
    private static Sprite fallbackSprite;

    private void Start()
    {
        ResolveReferences();

        if (createRuntimeUi && progressSlider == null)
            CreateRuntimeProgressUi();

        if (progressSlider != null)
        {
            progressSlider.minValue = 0f;
            progressSlider.maxValue = 1f;
            progressSlider.wholeNumbers = false;
            progressSlider.onValueChanged.AddListener(OnSliderValueChanged);
        }
    }

    private void Update()
    {
        ResolveReferences();

        if (videoPlayer == null)
            return;

        if (enableKeyboardShortcuts)
            HandleKeyboardSeek();

        double duration = GetDurationSeconds();
        if (duration <= 0.01d || progressSlider == null)
        {
            UpdateTimeLabel(0d, duration);
            return;
        }

        updatingSlider = true;
        progressSlider.SetValueWithoutNotify(Mathf.Clamp01((float)(videoPlayer.time / duration)));
        updatingSlider = false;
        UpdateTimeLabel(videoPlayer.time, duration);
    }

    private void ResolveReferences()
    {
        if (videoPlayer == null)
            videoPlayer = FindObjectOfType<VideoPlayer>();

        if (audioDanmakuController == null)
            audioDanmakuController = FindObjectOfType<AudioDanmakuController>();

        if (danmakuPlaybackController == null)
            danmakuPlaybackController = FindObjectOfType<DanmakuPlaybackController>();
    }

    private void OnSliderValueChanged(float normalizedValue)
    {
        if (updatingSlider || videoPlayer == null)
            return;

        SeekToNormalized(normalizedValue);
    }

    private void SeekToNormalized(float normalizedValue)
    {
        double duration = GetDurationSeconds();
        if (duration <= 0.01d || !videoPlayer.canSetTime)
            return;

        double target = Mathf.Clamp01(normalizedValue) * duration;
        bool wasPlaying = videoPlayer.isPlaying;
        videoPlayer.time = target;
        NotifyTimelineControllers((float)target);

        if (wasPlaying && !videoPlayer.isPlaying)
            videoPlayer.Play();
    }

    private void HandleKeyboardSeek()
    {
        if (Input.GetKeyDown(KeyCode.RightArrow))
            SeekBy(keyboardSeekStepSeconds);
        else if (Input.GetKeyDown(KeyCode.LeftArrow))
            SeekBy(-keyboardSeekStepSeconds);
    }

    private void SeekBy(float deltaSeconds)
    {
        double duration = GetDurationSeconds();
        if (duration <= 0.01d || videoPlayer == null || !videoPlayer.canSetTime)
            return;

        double target = Mathf.Clamp((float)(videoPlayer.time + deltaSeconds), 0f, (float)duration);
        bool wasPlaying = videoPlayer.isPlaying;
        videoPlayer.time = target;
        NotifyTimelineControllers((float)target);

        if (wasPlaying && !videoPlayer.isPlaying)
            videoPlayer.Play();
    }

    private void NotifyTimelineControllers(float targetSeconds)
    {
        if (audioDanmakuController != null)
            audioDanmakuController.NotifyVideoSeek(targetSeconds);

        if (danmakuPlaybackController != null)
            danmakuPlaybackController.NotifyVideoSeek(targetSeconds);
    }

    private double GetDurationSeconds()
    {
        if (videoPlayer == null)
            return 0d;

        if (videoPlayer.length > 0d && !double.IsInfinity(videoPlayer.length) && !double.IsNaN(videoPlayer.length))
            return videoPlayer.length;

        if (videoPlayer.frameCount > 0 && videoPlayer.frameRate > 0d)
            return videoPlayer.frameCount / videoPlayer.frameRate;

        return 0d;
    }

    private void CreateRuntimeProgressUi()
    {
        EnsureEventSystem();

        GameObject canvasObject = new GameObject(canvasName, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 5000;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        GameObject panel = CreateUiImage("ProgressPanel", canvasObject.transform, new Color(0f, 0f, 0f, 0.55f));
        RectTransform panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.5f, 0f);
        panelRect.anchorMax = new Vector2(0.5f, 0f);
        panelRect.pivot = new Vector2(0.5f, 0f);
        panelRect.anchoredPosition = new Vector2(0f, bottomOffset);
        panelRect.sizeDelta = new Vector2(width, height);

        GameObject sliderObject = new GameObject(sliderObjectName, typeof(RectTransform), typeof(Slider));
        sliderObject.transform.SetParent(panel.transform, false);
        RectTransform sliderRect = sliderObject.GetComponent<RectTransform>();
        sliderRect.anchorMin = new Vector2(0f, 0.5f);
        sliderRect.anchorMax = new Vector2(1f, 0.5f);
        sliderRect.pivot = new Vector2(0.5f, 0.5f);
        sliderRect.anchoredPosition = showTimeLabel ? new Vector2(-70f, 0f) : Vector2.zero;
        sliderRect.sizeDelta = showTimeLabel ? new Vector2(-160f, 22f) : new Vector2(-42f, 22f);

        GameObject background = CreateUiImage("Background", sliderObject.transform, new Color(1f, 1f, 1f, 0.28f));
        RectTransform backgroundRect = background.GetComponent<RectTransform>();
        Stretch(backgroundRect, Vector2.zero, Vector2.zero);

        GameObject fillArea = new GameObject("Fill Area", typeof(RectTransform));
        fillArea.transform.SetParent(sliderObject.transform, false);
        RectTransform fillAreaRect = fillArea.GetComponent<RectTransform>();
        Stretch(fillAreaRect, new Vector2(8f, 0f), new Vector2(-8f, 0f));

        GameObject fill = CreateUiImage("Fill", fillArea.transform, new Color(0.22f, 0.62f, 1f, 0.95f));
        RectTransform fillRect = fill.GetComponent<RectTransform>();
        Stretch(fillRect, Vector2.zero, Vector2.zero);

        GameObject handleArea = new GameObject("Handle Slide Area", typeof(RectTransform));
        handleArea.transform.SetParent(sliderObject.transform, false);
        RectTransform handleAreaRect = handleArea.GetComponent<RectTransform>();
        Stretch(handleAreaRect, new Vector2(8f, 0f), new Vector2(-8f, 0f));

        GameObject handle = CreateUiImage("Handle", handleArea.transform, new Color(1f, 1f, 1f, 1f));
        RectTransform handleRect = handle.GetComponent<RectTransform>();
        handleRect.sizeDelta = new Vector2(24f, 24f);

        progressSlider = sliderObject.GetComponent<Slider>();
        progressSlider.targetGraphic = handle.GetComponent<Image>();
        progressSlider.fillRect = fillRect;
        progressSlider.handleRect = handleRect;
        progressSlider.direction = Slider.Direction.LeftToRight;

        if (showTimeLabel)
            timeLabel = CreateTimeLabel(panel.transform);
    }

    private void EnsureEventSystem()
    {
        if (FindObjectOfType<EventSystem>() != null)
            return;

        new GameObject("RuntimeEventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
    }

    private static GameObject CreateUiImage(string name, Transform parent, Color color)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        Image image = go.GetComponent<Image>();
        image.sprite = GetUiSprite();
        image.color = color;
        return go;
    }

    private static Sprite GetUiSprite()
    {
        Sprite sprite = Resources.GetBuiltinResource<Sprite>("UI/Skin/UISprite.psd");
        if (sprite != null)
            return sprite;

        if (fallbackSprite == null)
        {
            Texture2D texture = Texture2D.whiteTexture;
            fallbackSprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f));
        }

        return fallbackSprite;
    }

    private Text CreateTimeLabel(Transform parent)
    {
        GameObject go = new GameObject("TimeLabel", typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(1f, 0.5f);
        rect.anchorMax = new Vector2(1f, 0.5f);
        rect.pivot = new Vector2(1f, 0.5f);
        rect.anchoredPosition = new Vector2(-18f, 0f);
        rect.sizeDelta = new Vector2(145f, 28f);

        Text label = go.GetComponent<Text>();
        label.alignment = TextAnchor.MiddleRight;
        label.color = Color.white;
        label.fontSize = 18;
        label.raycastTarget = false;
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (label.font == null)
            label.font = Resources.GetBuiltinResource<Font>("Arial.ttf");

        return label;
    }

    private static void Stretch(RectTransform rect, Vector2 offsetMin, Vector2 offsetMax)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
    }

    private void UpdateTimeLabel(double current, double duration)
    {
        if (timeLabel == null)
            return;

        timeLabel.text = FormatTime(current) + " / " + FormatTime(duration);
    }

    private static string FormatTime(double seconds)
    {
        if (seconds < 0d || double.IsNaN(seconds) || double.IsInfinity(seconds))
            seconds = 0d;

        int whole = Mathf.Max(0, Mathf.FloorToInt((float)seconds));
        return string.Format("{0:00}:{1:00}", whole / 60, whole % 60);
    }
}
