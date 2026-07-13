using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(SemanticDanmakuController))]
public class SemanticDanmakuControllerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        SemanticDanmakuController controller = (SemanticDanmakuController)target;
        EditorGUILayout.Space(8f);
        EditorGUILayout.HelpBox(
            "初步语义云弹幕：\n" +
            "1. 在 screen 下创建 CurvedDanmakuCloudRig，并放 NearEmotionLayer / MidInfoLayer / FarInfoLayer 三个子物体\n" +
            "2. 每层挂 CurvedDanmakuSurfaceLayer\n" +
            "3. 暂时 Disable 旧的 PopUpDanmakuController\n" +
            "4. 分类 JSON 放在 StreamingAssets/SemanticDanmaku/\n\n" +
            "当前默认模式（Use Far Layer Category Files 勾选）：\n" +
            "只读取 Far Layer File A / B 两个文件，在 FarInfoLayer 左右各一条滚动区展示。\n" +
            "开启 Far Info Use Scroll Ticker 后，每条弹幕带喇叭图标从左向右滚过，左右互不越界。\n" +
            "关闭滚动条时，退回原来的两簇垂直堆叠布局。",
            MessageType.Info);

        if (GUILayout.Button("自动查找 Cloud Rig / Social Panel"))
        {
            controller.cloudRig = FindObjectOfType<CurvedDanmakuCloudRig>();
            controller.socialPanel = FindObjectOfType<HeadLockedSocialPanel>();
            EditorUtility.SetDirty(controller);
        }
    }
}

[CustomEditor(typeof(CurvedDanmakuCloudRig))]
public class CurvedDanmakuCloudRigEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        CurvedDanmakuCloudRig rig = (CurvedDanmakuCloudRig)target;
        EditorGUILayout.Space(8f);
        EditorGUILayout.HelpBox(
            "screen 物体本身被拉伸缩放（用于铺满视频画面），如果 CurvedDanmakuCloudRig 直接继承这个缩放，\n" +
            "会导致下面各层的 Radius / Position 被放大好几倍，弹幕看起来离得极近、糊成一片。\n" +
            "「重新计算缩放」会自动把这个拉伸抵消掉，让 Radius 单位变回接近真实的米数。",
            MessageType.Info);

        if (GUILayout.Button("重新计算缩放（抵消 screen 拉伸）"))
        {
            rig.NormalizeParentScale();
            EditorUtility.SetDirty(rig);
        }

        if (GUILayout.Button("解析子物体并补全曲面层组件"))
        {
            rig.ResolveLayers();
            EditorUtility.SetDirty(rig);
        }

        if (GUILayout.Button("应用默认三区布局（左右上 + 近处下方）"))
        {
            rig.NormalizeParentScale();
            rig.ApplyAllLayerPresets(true);
            EditorUtility.SetDirty(rig);
        }
    }
}
