using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 运行时通过代码生成的 Sprite/Texture2D 不会随 GameObject.Destroy 自动释放，
/// 需要在销毁 UI 前显式清理，否则 Quest 上长时间播放会越积越高。
/// </summary>
public static class UiSpriteCleanupUtil
{
    public static void DestroyGeneratedSprites(GameObject root)
    {
        if (root == null)
            return;

        Image[] images = root.GetComponentsInChildren<Image>(true);
        for (int i = 0; i < images.Length; i++)
            DestroySprite(images[i] != null ? images[i].sprite : null);
    }

    public static void DestroySprite(Sprite sprite)
    {
        if (sprite == null)
            return;

        Texture2D texture = sprite.texture;
        Object.Destroy(sprite);
        if (texture != null)
            Object.Destroy(texture);
    }
}
