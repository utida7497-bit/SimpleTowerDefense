using UnityEditor;
using UnityEngine;

public static class ToolkitTimeScale
{
    private static float timeScale = 1f;

    public static void DrawMenu()
    {
        EditorGUILayout.HelpBox("再生中のゲームの時間の流れを調整します(0=停止 〜 2=倍速)。", MessageType.Info);

        GUI.enabled = Application.isPlaying;

        EditorGUI.BeginChangeCheck();
        timeScale = EditorGUILayout.Slider("Time Scale", timeScale, 0f, 2f);
        if (EditorGUI.EndChangeCheck())
        {
            Time.timeScale = timeScale;
        }

        GUILayout.Space(5);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("⏸ 停止 (0x)")) SetTimeScale(0f);
            if (GUILayout.Button("▶ 通常 (1x)")) SetTimeScale(1f);
            if (GUILayout.Button("⏩ 倍速 (2x)")) SetTimeScale(2f);
        }

        if (!Application.isPlaying)
        {
            GUILayout.Space(5);
            EditorGUILayout.LabelField("※ 再生中のみ操作できます。", EditorStyles.miniLabel);
        }

        GUI.enabled = true;
    }

    private static void SetTimeScale(float value)
    {
        timeScale = value;
        Time.timeScale = value;
    }

    // 🌟 再生モード終了時、timeScaleを1に戻し忘れて次回困る事故を防止
    [InitializeOnLoadMethod]
    private static void RegisterPlayModeReset()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingPlayMode)
        {
            timeScale = 1f;
            Time.timeScale = 1f;
        }
    }
}