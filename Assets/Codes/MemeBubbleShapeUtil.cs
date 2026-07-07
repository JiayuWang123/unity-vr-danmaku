using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 生成一体化对话框 Sprite：矩形 + 斜下尖角连通，外圈统一描边。
/// </summary>
static class MemeBubbleShapeUtil
{
    public static Sprite CreateUnifiedBubble(
        int width,
        int bodyHeight,
        bool isLeft,
        int tailBaseWidth,
        int tailHeight,
        int tailSideInset,
        int borderWidth,
        Color fillColor,
        Color borderColor)
    {
        width = Mathf.Max(32, width);
        bodyHeight = Mathf.Max(16, bodyHeight);
        tailBaseWidth = Mathf.Max(8, tailBaseWidth);
        tailHeight = Mathf.Max(6, tailHeight);
        tailSideInset = Mathf.Max(0, tailSideInset);
        borderWidth = Mathf.Max(1, borderWidth);

        int totalH = bodyHeight + tailHeight;
        var inside = new bool[width, totalH];

        for (int y = 0; y < totalH; y++)
        {
            for (int x = 0; x < width; x++)
                inside[x, y] = IsInside(x, y, width, bodyHeight, tailHeight, tailBaseWidth, tailSideInset, isLeft);
        }

        var tex = new Texture2D(width, totalH, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;

        for (int y = 0; y < totalH; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (!inside[x, y])
                {
                    tex.SetPixel(x, y, Color.clear);
                    continue;
                }

                tex.SetPixel(x, y, IsBorder(inside, x, y, width, totalH, borderWidth) ? borderColor : fillColor);
            }
        }

        tex.Apply(false, true);
        return Sprite.Create(tex, new Rect(0, 0, width, totalH), new Vector2(0.5f, 0.5f), 100f);
    }

    public static void ApplyUnifiedBubble(Image img, Sprite sprite)
    {
        img.sprite = sprite;
        img.type = Image.Type.Simple;
        img.color = Color.white;
        img.preserveAspect = false;
    }

    static bool IsInside(int x, int y, int w, int bodyH, int tailH, int tailW, int sideInset, bool isLeft)
    {
        // 主体矩形（尖角上方）；底边 y=tailH 整行也属于矩形，外侧会留出一段水平边
        if (y >= tailH && x >= 0 && x < w)
            return true;

        float inset = sideInset;

        // 左下尖角：从 inset 处起，外侧 [0, inset) 保留底边横线
        if (isLeft)
            return PointInTriangle(x + 0.5f, y + 0.5f, inset, tailH, inset + tailW, tailH, inset, 0f);

        // 右下尖角：从 right-inset 处起，外侧 (right-inset, right] 保留底边横线
        float right = w - 1f;
        return PointInTriangle(x + 0.5f, y + 0.5f, right - inset, tailH, right - inset - tailW, tailH, right - inset, 0f);
    }

    static bool PointInTriangle(float px, float py,
        float ax, float ay, float bx, float by, float cx, float cy)
    {
        float d1 = Sign(px, py, ax, ay, bx, by);
        float d2 = Sign(px, py, bx, by, cx, cy);
        float d3 = Sign(px, py, cx, cy, ax, ay);
        bool hasNeg = d1 < 0f || d2 < 0f || d3 < 0f;
        bool hasPos = d1 > 0f || d2 > 0f || d3 > 0f;
        return !(hasNeg && hasPos);
    }

    static float Sign(float px, float py, float ax, float ay, float bx, float by)
        => (px - bx) * (ay - by) - (ax - bx) * (py - by);

    static bool IsBorder(bool[,] inside, int x, int y, int w, int h, int borderW)
    {
        for (int dy = -borderW; dy <= borderW; dy++)
        {
            for (int dx = -borderW; dx <= borderW; dx++)
            {
                if (dx * dx + dy * dy > borderW * borderW)
                    continue;

                int nx = x + dx;
                int ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= w || ny >= h || !inside[nx, ny])
                    return true;
            }
        }
        return false;
    }
}
