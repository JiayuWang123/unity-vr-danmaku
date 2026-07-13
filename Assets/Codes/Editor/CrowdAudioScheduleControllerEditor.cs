#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(CrowdAudioScheduleController))]
public class CrowdAudioScheduleControllerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        if (GUILayout.Button("自动绑定环境音 Clip"))
        {
            var ctrl = (CrowdAudioScheduleController)target;
            ctrl.clipBindings = new[]
            {
                Bind("cheer_long", "Assets/Audio/欢呼声/long.mp3"),
                Bind("normal_ambient", "Assets/Audio/正常阶段声音/normal.mp3"),
                Bind("tension_heart", "Assets/Audio/心跳声/heart.mp3"),
            };
            EditorUtility.SetDirty(ctrl);
        }
    }

    static CrowdClipBinding Bind(string key, string path)
    {
        return new CrowdClipBinding
        {
            clipKey = key,
            clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path)
        };
    }
}
#endif
