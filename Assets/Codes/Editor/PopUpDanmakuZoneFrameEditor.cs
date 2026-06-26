using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(PopUpDanmakuZoneFrame))]
[CanEditMultipleObjects]
public class PopUpDanmakuZoneFrameEditor : Editor
{
    void OnSceneGUI()
    {
        if (Application.isPlaying)
            return;

        foreach (Object obj in targets)
        {
            if (obj is PopUpDanmakuZoneFrame frame)
                DrawFrameHandles(frame);
        }
    }

    void DrawFrameHandles(PopUpDanmakuZoneFrame frame)
    {
        Transform t = frame.transform;
        Vector3 center = t.position;
        Quaternion rotation = t.rotation;

        Handles.color = frame.GetZoneColor();
        Handles.Label(center + t.up * (frame.frameSize.y * 0.5f + 0.08f), frame.GetZoneLabel(), EditorStyles.whiteBoldLabel);

        EditorGUI.BeginChangeCheck();
        Vector3 newCenter = Handles.PositionHandle(center, rotation);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(t, "Move Pop-up Zone");
            t.position = newCenter;
            EditorUtility.SetDirty(frame);
        }

        DrawResizeHandles(frame, t);
        DrawAnchorHandles(frame);
    }

    void DrawResizeHandles(PopUpDanmakuZoneFrame frame, Transform t)
    {
        float halfW = frame.frameSize.x * 0.5f;
        float halfH = frame.frameSize.y * 0.5f;

        Vector3 right = t.TransformPoint(new Vector3(halfW, 0f, 0f));
        Vector3 top = t.TransformPoint(new Vector3(0f, halfH, 0f));

        EditorGUI.BeginChangeCheck();
        var fmh_53_13_639180720292721297 = Quaternion.identity; Vector3 newRight = Handles.FreeMoveHandle(
            right,
            HandleUtility.GetHandleSize(right) * 0.06f,
            Vector3.zero,
            Handles.DotHandleCap);

        var fmh_60_13_639180720292744133 = Quaternion.identity; Vector3 newTop = Handles.FreeMoveHandle(
            top,
            HandleUtility.GetHandleSize(top) * 0.06f,
            Vector3.zero,
            Handles.DotHandleCap);

        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(frame, "Resize Pop-up Zone");
            Vector3 localRight = t.InverseTransformPoint(newRight);
            Vector3 localTop = t.InverseTransformPoint(newTop);
            frame.frameSize = new Vector2(Mathf.Abs(localRight.x) * 2f, Mathf.Abs(localTop.y) * 2f);
            frame.SyncAnchorLocalPositions();
            frame.UpdateEditorVisual();
            EditorUtility.SetDirty(frame);
        }
    }

    void DrawAnchorHandles(PopUpDanmakuZoneFrame frame)
    {
        frame.EnsureAnchors();
        foreach (PopUpDanmakuAnchor anchor in frame.Anchors)
        {
            if (anchor == null)
                continue;

            Transform anchorTransform = anchor.transform;
            EditorGUI.BeginChangeCheck();
            var fmh_89_17_639180720292747043 = Quaternion.identity; Vector3 newPos = Handles.FreeMoveHandle(
                anchorTransform.position,
                HandleUtility.GetHandleSize(anchorTransform.position) * 0.05f,
                Vector3.zero,
                Handles.SphereHandleCap);

            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(anchorTransform, "Move Pop-up Anchor");
                anchorTransform.position = newPos;
                EditorUtility.SetDirty(anchorTransform);
            }
        }
    }
}
