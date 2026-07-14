using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Video;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// StadiumScene 运行时自动挂载 longAudio 分支的 TTS / 观众声浪系统。
/// pop_upScene 若已手动配置 AudioTimelineManager 则跳过。
/// </summary>
[DefaultExecutionOrder(-250)]
public class StadiumAudioBootstrap : MonoBehaviour
{
    const string ManagerName = "AudioTimelineManager";
    const string AmbientGroupName = "AmbientAudioGroup";

    [Header("声音弹幕（TTS）")]
    [Tooltip("TTS 排期 JSON（相对 StreamingAssets）。留空则按场景名自动选择。")]
    public string candidatesFileName = "";

    [Tooltip("对数音量倍率（0.5–5000），映射到 AudioSource 0.05–1.0。Play 模式下改此值会实时生效。")]
    [Range(0.5f, 5000f)] public float ttsPlaybackGain = 1.35f;

    [Header("视频解说")]
    [Tooltip("全程视频解说音量；TTS 播放时也不会被压低")]
    [Range(0f, 1f)] public float videoCommentaryVolume = 1f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoInstall()
    {
        var screen = GameObject.Find("screen");
        if (screen == null)
            return;

        var vp = screen.GetComponent<VideoPlayer>();
        if (vp == null)
            return;

        var bootstrap = FindObjectOfType<StadiumAudioBootstrap>();
        TtsSceneCatalog.Profile profile = bootstrap != null
            ? TtsSceneCatalog.Resolve(bootstrap.candidatesFileName)
            : TtsSceneCatalog.Resolve();
        TtsDisplayedTextFilter.Configure(profile.candidatesFile);

        var existingManager = GameObject.Find(ManagerName);
        if (existingManager != null)
            Destroy(existingManager);

        if (bootstrap != null)
        {
            bootstrap.Build(screen.transform, vp);
            return;
        }

        var go = new GameObject(nameof(StadiumAudioBootstrap));
        go.hideFlags = HideFlags.HideAndDontSave;
        var ephemeral = go.AddComponent<StadiumAudioBootstrap>();
        ephemeral.Build(screen.transform, vp);
        Destroy(go);
    }

    void OnValidate()
    {
        if (!Application.isPlaying)
            return;

        ApplyLiveSettings();
    }

    void ApplyLiveSettings()
    {
        var tts = FindObjectOfType<AudioDanmakuController>();
        if (tts == null)
            return;

        tts.ttsPlaybackGain = ttsPlaybackGain;
        tts.videoCommentaryVolume = videoCommentaryVolume;
    }

    void Build(Transform screen, VideoPlayer videoPlayer)
    {
        var ambient = EnsureAmbientGroup(screen);
        var manager = new GameObject(ManagerName);

        string candidates = ResolveCandidatesFileName();
        TtsSceneCatalog.Profile profile = TtsSceneCatalog.Resolve(candidates);
        TtsDisplayedTextFilter.Configure(profile.candidatesFile);

        var tts = manager.AddComponent<AudioDanmakuController>();
        tts.videoPlayer = videoPlayer;
        tts.videoCommentarySource = screen.GetComponent<AudioSource>();
        tts.candidatesFileName = profile.candidatesFile;
        tts.ttsClipFolder = profile.clipFolder;
        tts.ttsPlaybackGain = ttsPlaybackGain;
        tts.videoCommentaryVolume = videoCommentaryVolume;
        tts.enableVideoDucking = false;
        tts.anchorFront = ambient.front;
        tts.anchorBack = ambient.back;
        tts.anchorLeft = ambient.left;
        tts.anchorRight = ambient.right;

        var crowd = manager.AddComponent<CrowdAudioScheduleController>();
        crowd.videoPlayer = videoPlayer;
        crowd.sourceFront = ambient.frontSource;
        crowd.sourceBack = ambient.backSource;
        crowd.sourceLeft = ambient.leftSource;
        crowd.sourceRight = ambient.rightSource;
        crowd.clipBindings = BuildCrowdClipBindings();
        crowd.enabled = crowd.clipBindings != null && crowd.clipBindings.Length > 0;

        Debug.Log($"[StadiumAudio] 已挂载 TTS / 观众声浪（{profile.candidatesFile} → {profile.clipFolder}）。");
    }

    string ResolveCandidatesFileName()
    {
        if (!string.IsNullOrWhiteSpace(candidatesFileName))
            return candidatesFileName.Replace('\\', '/');

        return TtsSceneCatalog.ResolveCandidatesForScene(SceneManager.GetActiveScene().name);
    }

    struct AmbientRefs
    {
        public Transform front, back, left, right;
        public AudioSource frontSource, backSource, leftSource, rightSource;
    }

    static AmbientRefs EnsureAmbientGroup(Transform screen)
    {
        var existing = GameObject.Find(AmbientGroupName);
        if (existing != null)
            return ReadAmbientRefs(existing.transform);

        var root = new GameObject(AmbientGroupName);
        Vector3 center = screen.position;
        Vector3 fwd = screen.forward;
        Vector3 right = screen.right;
        float dist = 3f;

        return new AmbientRefs
        {
            front = CreateSpeaker(root.transform, "AudioScource_Font", center + fwd * dist, screen.rotation),
            back = CreateSpeaker(root.transform, "AudioScource_Back", center - fwd * dist, screen.rotation),
            left = CreateSpeaker(root.transform, "AudioScource_Left", center - right * dist, screen.rotation),
            right = CreateSpeaker(root.transform, "AudioScource_Right", center + right * dist, screen.rotation),
            frontSource = root.transform.Find("AudioScource_Font")?.GetComponent<AudioSource>(),
            backSource = root.transform.Find("AudioScource_Back")?.GetComponent<AudioSource>(),
            leftSource = root.transform.Find("AudioScource_Left")?.GetComponent<AudioSource>(),
            rightSource = root.transform.Find("AudioScource_Right")?.GetComponent<AudioSource>()
        };
    }

    static AmbientRefs ReadAmbientRefs(Transform root)
    {
        Transform F(string n) => root.Find(n);
        return new AmbientRefs
        {
            front = F("AudioScource_Font") ?? F("AudioScource_Front"),
            back = F("AudioScource_Back"),
            left = F("AudioScource_Left"),
            right = F("AudioScource_Right"),
            frontSource = (F("AudioScource_Font") ?? F("AudioScource_Front"))?.GetComponent<AudioSource>(),
            backSource = F("AudioScource_Back")?.GetComponent<AudioSource>(),
            leftSource = F("AudioScource_Left")?.GetComponent<AudioSource>(),
            rightSource = F("AudioScource_Right")?.GetComponent<AudioSource>()
        };
    }

    static Transform CreateSpeaker(Transform parent, string name, Vector3 worldPos, Quaternion worldRot)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, worldPositionStays: true);
        go.transform.SetPositionAndRotation(worldPos, worldRot);

        var src = go.AddComponent<AudioSource>();
        src.spatialBlend = 1f;
        src.playOnAwake = false;
        src.loop = true;
        src.volume = 0.22f;
        src.minDistance = 1f;
        src.maxDistance = 30f;
        return go.transform;
    }

    static CrowdClipBinding[] BuildCrowdClipBindings()
    {
        var cheer = LoadAudioClip("Assets/Audio/欢呼声/long.mp3");
        var normal = LoadAudioClip("Assets/Audio/正常阶段声音/normal.mp3");
        var heart = LoadAudioClip("Assets/Audio/心跳声/heart.mp3");
        if (cheer == null && normal == null && heart == null)
            return null;

        var list = new System.Collections.Generic.List<CrowdClipBinding>();
        if (cheer != null) list.Add(new CrowdClipBinding { clipKey = "cheer_long", clip = cheer });
        if (normal != null) list.Add(new CrowdClipBinding { clipKey = "normal_ambient", clip = normal });
        if (heart != null) list.Add(new CrowdClipBinding { clipKey = "tension_heart", clip = heart });
        return list.ToArray();
    }

    static AudioClip LoadAudioClip(string assetPath)
    {
#if UNITY_EDITOR
        return AssetDatabase.LoadAssetAtPath<AudioClip>(assetPath);
#else
        return null;
#endif
    }
}
