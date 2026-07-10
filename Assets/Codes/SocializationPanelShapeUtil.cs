using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 生成赛博朋克风格的圆角发光面板/圆点 Sprite：
/// 半透明填充 + 青紫渐变描边 + 柔和外发光。
/// </summary>
static class SocializationPanelShapeUtil
{
    public static Sprite CreatePanel(
        int width,
        int height,
        int cornerRadius,
        int glowSize,
        float borderWidth,
        Color fillColor,
        Color borderColorA,
        Color borderColorB,
        Color glowColor)
    {
        width = Mathf.Max(4, width);
        height = Mathf.Max(4, height);
        glowSize = Mathf.Max(0, glowSize);
        borderWidth = Mathf.Max(0.5f, borderWidth);
        cornerRadius = Mathf.Clamp(cornerRadius, 0, Mathf.Min(width, height) / 2);

        int texW = width + glowSize * 2;
        int texH = height + glowSize * 2;
        var tex = new Texture2D(texW, texH, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };

        var pixels = new Color32[texW * texH];

        for (int y = 0; y < texH; y++)
        {
            for (int x = 0; x < texW; x++)
            {
                float px = x - glowSize + 0.5f;
                float py = y - glowSize + 0.5f;
                float d = RoundedRectSDF(px, py, width, height, cornerRadius);

                float t = Mathf.Clamp01(px / width);
                Color edgeColor = Color.Lerp(borderColorA, borderColorB, t);

                Color c;
                if (d < -borderWidth)
                {
                    c = fillColor;
                }
                else if (d < 0f)
                {
                    float innerBlend = Mathf.InverseLerp(-borderWidth, -borderWidth + 1.4f, d);
                    c = Color.Lerp(fillColor, edgeColor, Mathf.Clamp01(innerBlend));
                }
                else if (glowSize > 0)
                {
                    float glowT = Mathf.Clamp01(1f - d / glowSize);
                    glowT *= glowT;
                    float rim = Mathf.Clamp01(1f - d / 2.4f);
                    Color g = glowColor;
                    g.a *= glowT;
                    Color rimColor = edgeColor;
                    rimColor.a = Mathf.Max(g.a, edgeColor.a * rim);
                    c = Color.Lerp(g, rimColor, rim);
                }
                else
                {
                    c = Color.clear;
                }

                pixels[y * texW + x] = c;
            }
        }

        tex.SetPixels32(pixels);
        tex.Apply(false, true);
        return Sprite.Create(tex, new Rect(0, 0, texW, texH), new Vector2(0.5f, 0.5f), 100f);
    }

    public static Sprite CreateGlowDot(int diameter, Color fillColor, Color ringColor, Color glowColor, int glowSize = 6)
    {
        diameter = Mathf.Max(4, diameter);
        return CreatePanel(diameter, diameter, diameter / 2, glowSize, Mathf.Max(1f, diameter * 0.14f),
            fillColor, ringColor, ringColor, glowColor);
    }

    /// <summary>
    /// 生成两端半圆的胶囊形 9-slice Sprite，用于滚动条轨道/滑块：设置好 border 后可任意拉伸高度而不变形。
    /// </summary>
    public static Sprite CreateSliceablePill(int width, int height, Color fillColor, Color borderColor, float borderWidthPx)
    {
        width = Mathf.Max(4, width);
        height = Mathf.Max(width, height);
        int radius = width / 2;

        var tex = new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };

        var pixels = new Color32[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float px = x + 0.5f;
                float py = y + 0.5f;
                float d = RoundedRectSDF(px, py, width, height, radius);

                Color c;
                if (d < -borderWidthPx) c = fillColor;
                else if (d < 0f) c = borderColor;
                else c = Color.clear;

                pixels[y * width + x] = c;
            }
        }

        tex.SetPixels32(pixels);
        tex.Apply(false, true);

        float b = radius;
        return Sprite.Create(tex, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), 100f,
            0, SpriteMeshType.FullRect, new Vector4(b, b, b, b));
    }

    public static void Apply(Image img, Sprite sprite)
    {
        img.sprite = sprite;
        img.type = Image.Type.Simple;
        img.color = Color.white;
        img.preserveAspect = false;
    }

    /// <summary>
    /// 生成"警戒线/警戒带"样式的长条：四边是斜向条纹（像施工警示带），中间是纯色（半透明）填充。
    /// 边角是直角，不带圆角/发光，专门给"提醒向"的横幅用，跟聊天气泡的赛博朋克圆角面板是两种风格。
    /// </summary>
    public static Sprite CreateHazardBar(
        int width,
        int height,
        int borderThickness,
        float stripeWidthPx,
        Color stripeColorA,
        Color stripeColorB,
        Color fillColor)
    {
        width = Mathf.Max(4, width);
        height = Mathf.Max(4, height);
        borderThickness = Mathf.Clamp(borderThickness, 0, Mathf.Min(width, height) / 2);
        stripeWidthPx = Mathf.Max(2f, stripeWidthPx);

        var tex = new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };

        var pixels = new Color32[width * height];
        for (int y = 0; y < height; y++)
        {
            bool rowInBorder = y < borderThickness || y >= height - borderThickness;
            for (int x = 0; x < width; x++)
            {
                bool inBorder = rowInBorder || x < borderThickness || x >= width - borderThickness;
                Color c;
                if (inBorder)
                {
                    int stripeIdx = Mathf.FloorToInt((x + y) / stripeWidthPx);
                    c = (stripeIdx & 1) == 0 ? stripeColorA : stripeColorB;
                }
                else
                {
                    c = fillColor;
                }
                pixels[y * width + x] = c;
            }
        }

        tex.SetPixels32(pixels);
        tex.Apply(false, true);

        return Sprite.Create(tex, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
    }

    static float RoundedRectSDF(float px, float py, float w, float h, float r)
    {
        r = Mathf.Min(r, Mathf.Min(w, h) * 0.5f);
        float cx = w * 0.5f;
        float cy = h * 0.5f;
        float qx = Mathf.Abs(px - cx) - (cx - r);
        float qy = Mathf.Abs(py - cy) - (cy - r);
        float ax = Mathf.Max(qx, 0f);
        float ay = Mathf.Max(qy, 0f);
        float outside = Mathf.Sqrt(ax * ax + ay * ay);
        float inside = Mathf.Min(Mathf.Max(qx, qy), 0f);
        return outside + inside - r;
    }
}
