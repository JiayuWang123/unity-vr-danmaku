using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 情绪气泡下方的油漆/墨水滴落拖尾效果：
/// 多条宽窄不一的渐变竖条从文字底部向下悬挂，随气泡一起淡入淡出。
/// 作为 Canvas 内的 UI 节点运行，无外部纹理依赖；共享一张纹理节省内存。
/// </summary>
public class EmotionBubbleOrbitRing : MonoBehaviour
{
    // 所有气泡共用一张滴落纹理，在所有实例都销毁后才释放
    static Texture2D s_dripTex;
    static Sprite    s_dripSprite;
    static int       s_refCount;

    CanvasGroup group;
    float baseAlpha;

    /// <param name="color">滴落颜色（alpha = 基础不透明度）</param>
    /// <param name="bubbleWidth">气泡文字宽（Canvas 本地像素单位）</param>
    /// <param name="bubbleHeight">气泡文字高（Canvas 本地像素单位）</param>
    /// <param name="dripCount">滴落条数</param>
    /// <param name="dripMaxHeightScale">最长滴落高度相对 bubbleHeight 的比例</param>
    /// <param name="dripMaxWidth">单条最大宽度（Canvas 像素单位）</param>
    public void Setup(Color color, float bubbleWidth, float bubbleHeight,
                      int dripCount, float dripMaxHeightScale, float dripMaxWidth)
    {
        baseAlpha = color.a;

        // ── CanvasGroup 统一控制淡入淡出 ─────────────────────────
        group = gameObject.AddComponent<CanvasGroup>();
        group.alpha         = 0f;
        group.interactable  = false;
        group.blocksRaycasts= false;

        // ── 共享纹理 ──────────────────────────────────────────────
        EnsureSharedResources();

        // ── 生成各条滴落 ──────────────────────────────────────────
        for (int i = 0; i < Mathf.Max(1, dripCount); i++)
        {
            float x = UnityEngine.Random.Range(-bubbleWidth * 0.42f, bubbleWidth * 0.42f);
            float h = bubbleHeight * UnityEngine.Random.Range(0.22f, dripMaxHeightScale);
            float w = UnityEngine.Random.Range(2f, Mathf.Max(3f, dripMaxWidth));

            var dripGo = new GameObject($"Drip{i}", typeof(RectTransform));
            dripGo.transform.SetParent(transform, false);

            var rt      = dripGo.GetComponent<RectTransform>();
            rt.anchorMin= rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot    = new Vector2(0.5f, 1f);    // 顶端为挂点，向下延伸
            rt.sizeDelta= new Vector2(w, h);
            // 顶端略渗入文字底边，让拖尾看起来是从文字里流出来的
            rt.anchoredPosition = new Vector2(x, -bubbleHeight * 0.46f);

            var img = dripGo.AddComponent<Image>();
            img.sprite         = s_dripSprite;
            img.color          = color;
            img.type           = Image.Type.Simple;
            img.preserveAspect = false;
            img.raycastTarget  = false;
        }
    }

    /// <summary>由 EmotionBubbleInstance 调用，传入归一化透明度 [0,1]</summary>
    public void SetAlpha(float alpha)
    {
        if (group != null)
            group.alpha = baseAlpha * Mathf.Clamp01(alpha);
    }

    void OnDestroy()
    {
        s_refCount--;
        if (s_refCount <= 0)
        {
            s_refCount = 0;
            if (s_dripSprite != null) { Object.Destroy(s_dripSprite); s_dripSprite = null; }
            if (s_dripTex    != null) { Object.Destroy(s_dripTex);    s_dripTex    = null; }
        }
    }

    // ── 静态工具 ─────────────────────────────────────────────────────

    static void EnsureSharedResources()
    {
        if (s_dripTex != null)
        {
            s_refCount++;
            return;
        }

        const int W = 16, H = 128;
        s_dripTex = BuildDripTexture(W, H);
        // pivot(0.5, 1) 让顶边对齐锚点，sprite 向下延伸
        s_dripSprite = Sprite.Create(
            s_dripTex,
            new Rect(0, 0, W, H),
            new Vector2(0.5f, 1f),
            100f);
        s_dripSprite.hideFlags = HideFlags.DontSave;
        s_refCount = 1;
    }

    static Texture2D BuildDripTexture(int w, int h)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode   = TextureWrapMode.Clamp,
            hideFlags  = HideFlags.DontSave
        };

        float halfW = w * 0.5f;
        var pixels  = new Color32[w * h];

        for (int y = 0; y < h; y++)
        {
            // 纵向梯度：顶部（y=h-1）不透明 → 底部（y=0）透明
            // 使用 smoothstep 曲线，底部加速淡出模拟流体自然收尖
            float yt = y / (float)(h - 1);                   // 0=底端 1=顶端
            float ya = yt * yt * (3f - 2f * yt);             // smoothstep

            for (int x = 0; x < w; x++)
            {
                // 横向：中心最亮，边缘柔化，模拟圆润的液滴截面
                float xt = Mathf.Abs(x + 0.5f - halfW) / halfW;   // 0=中 1=边
                float xa = 1f - xt * xt;

                pixels[y * w + x] = new Color32(255, 255, 255, (byte)(ya * xa * 255f));
            }
        }

        tex.SetPixels32(pixels);
        tex.Apply();
        return tex;
    }
}
