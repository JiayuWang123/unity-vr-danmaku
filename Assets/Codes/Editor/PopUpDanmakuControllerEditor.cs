using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(PopUpDanmakuController))]
public class PopUpDanmakuControllerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        PopUpDanmakuController controller = (PopUpDanmakuController)target;
        EditorGUILayout.Space(8f);
        EditorGUILayout.HelpBox(
            "Scene 视图：拖动区域中心移动整块；拖动右侧/上侧圆点改大小；拖动小球单独微调锚点。\n" +
            "Play 时线框自动隐藏。如需把锚点贴回框边，在区域框组件右键 Snap Anchors To Frame。",
            MessageType.Info);

        if (GUILayout.Button("在 Screen 下创建/重建三区调节框"))
        {
            controller.CreateZoneFrameGuidesInEditor();
            EditorUtility.SetDirty(controller);
        }
    }
}
