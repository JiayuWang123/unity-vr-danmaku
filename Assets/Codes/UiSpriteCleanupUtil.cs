using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 运行时通过代码生成的 Sprite/Texture2D 不会随 GameObject.Destroy 自动释放，
/// 需要在销毁 UI 前显式清理，否则 Quest 上长时间播放会越积越高。
/// Resources 等工程资源不会被销毁。
/// </summary>
public static class UiSpriteCleanupUtil
{
    public static void DestroyGeneratedSprites(GameObject root)
    {
        if (root == null)
            return;

        Image[] images = root.GetComponentsInChildren<Image>(true);
        for (int i = 0; i < images.Length; i++)
        {
            Image image = images[i];
            if (image == null)
                continue;

            Sprite sprite = image.sprite;
            image.sprite = null;
            DestroySprite(sprite);
        }
    }

    public static void DestroySprite(Sprite sprite)
    {
        if (sprite == null)
            return;

        Texture2D texture = sprite.texture;
        if (!IsRuntimeGenerated(texture))
            return;

        Object.Destroy(sprite);
        Object.Destroy(texture);
    }

    static bool IsRuntimeGenerated(Object obj)
    {
        return obj != null && (obj.hideFlags & HideFlags.DontSave) != 0;
    }
}
