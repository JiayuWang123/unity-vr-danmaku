using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR;
using UnityEngine.XR.Management;

/// <summary>
/// VR 启动时修复常见立体渲染叠影：关闭模拟器、隐藏编辑器线框、优化视频屏幕渲染。
/// </summary>
[DefaultExecutionOrder(-300)]
public class VrStereoFixBootstrap : MonoBehaviour
{
    [Tooltip("真机 VR 运行时自动禁用 XR Device Simulator")]
    public bool disableDeviceSimulatorInXr = true;

    [Tooltip("Play 时隐藏 PopUp/Curved 区域编辑器线框（LineRenderer）")]
    public bool hideEditorZoneGuides = true;

    [Tooltip("关闭视频 screen 的 Motion Vector，减少 Quest 重投影叠影")]
    public bool disableVideoScreenMotionVectors = true;

    [Tooltip("Screen Space Camera 的旧版弹幕 Canvas 在 VR 里容易导致单眼叠影，运行时自动关闭")]
    public bool disableLegacyScreenSpaceDanmakuCanvasInXr = true;

    static bool applied;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void ResetAppliedFlag()
    {
        applied = false;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoApplyOnSceneLoad()
    {
        if (applied)
            return;

        var go = new GameObject(nameof(VrStereoFixBootstrap));
        go.hideFlags = HideFlags.HideAndDontSave;
        go.AddComponent<VrStereoFixBootstrap>();
    }

    void Awake()
    {
        if (applied)
            return;

        applied = true;
        ApplyFixes();
    }

    void ApplyFixes()
    {
        bool xrActive = IsXrActive();

        if (disableDeviceSimulatorInXr && xrActive)
            DisableDeviceSimulator();

        if (hideEditorZoneGuides)
            HideEditorZoneGuides();

        if (disableVideoScreenMotionVectors)
            FixVideoScreenRenderer();

        if (disableLegacyScreenSpaceDanmakuCanvasInXr && xrActive)
            DisableLegacyDanmakuCanvas();
    }

    static bool IsXrActive()
    {
        if (XRSettings.isDeviceActive)
            return true;

        var general = XRGeneralSettings.Instance;
        if (general == null || general.Manager == null)
            return false;

        return general.Manager.isInitializationComplete && general.Manager.activeLoader != null;
    }

    static void DisableDeviceSimulator()
    {
        var simulators = FindObjectsOfType<Transform>(true);
        for (int i = 0; i < simulators.Length; i++)
        {
            Transform t = simulators[i];
            if (t == null || t.name != "XR Device Simulator")
                continue;

            t.gameObject.SetActive(false);
            Debug.Log("[VrStereoFix] 已禁用 XR Device Simulator（真机 VR 不需要）。");
        }
    }

    static void HideEditorZoneGuides()
    {
        int hidden = 0;
        var all = FindObjectsOfType<Transform>(true);
        for (int i = 0; i < all.Length; i++)
        {
            Transform t = all[i];
            if (t == null || t.name != "EditorZoneGuide")
                continue;

            var line = t.GetComponent<LineRenderer>();
            if (line != null)
                line.enabled = false;

            if (t.gameObject.activeSelf)
            {
                t.gameObject.SetActive(false);
                hidden++;
            }
        }

        if (hidden > 0)
            Debug.Log($"[VrStereoFix] 已隐藏 {hidden} 个编辑器区域线框。");
    }

    static void FixVideoScreenRenderer()
    {
        var screen = GameObject.Find("screen");
        if (screen == null)
            return;

        var renderer = screen.GetComponent<MeshRenderer>();
        if (renderer == null)
            return;

        renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
    }

    static void DisableLegacyDanmakuCanvas()
    {
        var canvases = FindObjectsOfType<Canvas>(true);
        for (int i = 0; i < canvases.Length; i++)
        {
            Canvas canvas = canvases[i];
            if (canvas == null || canvas.renderMode != RenderMode.ScreenSpaceCamera)
                continue;

            if (canvas.name != "DanmakuCanvas2")
                continue;

            canvas.gameObject.SetActive(false);
            Debug.Log("[VrStereoFix] 已关闭 DanmakuCanvas2（Screen Space Camera 在 VR 中易导致右眼叠影）。");
        }
    }
}
