using TMPro;
using UnityEngine;

public static class TmpDanmakuTextUtility
{
    public static void ApplyReadableStyle(TextMeshProUGUI label, float outlineWidth, Color outlineColor)
    {
        if (label == null)
            return;

        label.fontStyle = FontStyles.Normal;

        if (outlineWidth <= 0f || label.font == null)
            return;

        label.ForceMeshUpdate(true, true);

        if (label.fontSharedMaterial == null)
        {
            label.fontStyle = FontStyles.Bold;
            return;
        }

        try
        {
            label.outlineWidth = outlineWidth;
            label.outlineColor = outlineColor;
        }
        catch
        {
            // SIMHEI 等部分 SDF 字体不支持运行时 outline，退回加粗保证可读性。
            label.fontStyle = FontStyles.Bold;
        }
    }
}
