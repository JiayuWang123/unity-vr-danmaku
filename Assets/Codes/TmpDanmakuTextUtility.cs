using TMPro;
using UnityEngine;

public static class TmpDanmakuTextUtility
{
    public static void ApplyReadableStyle(TextMeshProUGUI label, float outlineWidth, Color outlineColor)
    {
        if (label == null)
            return;

        if (outlineWidth <= 0f || label.font == null)
        {
            label.fontStyle = FontStyles.Bold;
            return;
        }

        label.fontStyle = FontStyles.Normal;
        label.ForceMeshUpdate(true, true);

        try
        {
            Material mat = label.fontMaterial;
            if (mat != null)
                mat.EnableKeyword("OUTLINE_ON");

            label.outlineWidth = outlineWidth;
            label.outlineColor = outlineColor;
            label.UpdateMeshPadding();
            label.ForceMeshUpdate(true, true);
        }
        catch
        {
            // 部分 SDF 字体不支持运行时 outline，退回加粗保证可读性。
            label.fontStyle = FontStyles.Bold;
        }
    }
}
