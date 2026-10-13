using UnityEditor;
using UnityEngine;
using System.Reflection;
using System.Collections.Generic;
using System.Linq;

public static class ToolkitObjectTracker
{
    private class TrackedField
    {
        public string label;
        public System.Func<float> getter;
        public bool isChecked;
        public bool isLogExpanded = true;
        public float lastValue;
        public bool hasLastValue;
        public List<string> logLines = new List<string>();
        public Vector2 logScroll;
    }

    private static GameObject targetObject;
    private static bool isLocked = false;
    private static List<TrackedField> trackedFields = new List<TrackedField>();
    private static bool isTracking = false;
    private static int maxLogLines = 100;
    private static float changeThreshold = 0.01f;
    private static float sampleInterval = 0f;
    private static double lastSampleTime = 0;

    public static void DrawMenu()
    {
        EditorGUILayout.HelpBox("オブジェクトを選び、追跡したい数値にチェックを入れて記録します。値が変化した瞬間だけログに残ります。", MessageType.Info);

        GameObject activeObj = isLocked ? targetObject : Selection.activeGameObject;

        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField("Target", activeObj != null ? activeObj.name : "(未選択)");
            bool nextLock = GUILayout.Toggle(isLocked, isLocked ? "🔒 Locked" : "🔓 Lock", EditorStyles.miniButton, GUILayout.Width(70));
            if (nextLock != isLocked)
            {
                isLocked = nextLock;
                if (isLocked) targetObject = Selection.activeGameObject;
            }
        }

        if (activeObj == null) return;

        if (GUILayout.Button("🔄 追跡可能なフィールドを取得"))
        {
            ScanFields(activeObj);
        }

        GUILayout.Space(5);

        foreach (var tf in trackedFields)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                tf.isChecked = EditorGUILayout.Toggle(tf.isChecked, GUILayout.Width(20));
                GUILayout.Label(tf.label);
                if (tf.hasLastValue)
                {
                    GUILayout.FlexibleSpace();
                    GUILayout.Label(tf.lastValue.ToString("F2"), GUILayout.Width(60));
                }
            }
        }

        if (trackedFields.Count == 0)
        {
            EditorGUILayout.LabelField("「取得」を押してください。", EditorStyles.miniLabel);
        }

        GUILayout.Space(5);

        changeThreshold = EditorGUILayout.FloatField("感度(変化量の閾値)", changeThreshold);
        if (changeThreshold < 0f) changeThreshold = 0f;

        using (new EditorGUILayout.HorizontalScope())
        {
            sampleInterval = EditorGUILayout.FloatField("サンプリング間隔(秒・0=毎フレーム)", sampleInterval);
            if (sampleInterval < 0f) sampleInterval = 0f;

            GUI.enabled = trackedFields.Any(t => t.isChecked);
            if (GUILayout.Button("📍今すぐ記録", GUILayout.Width(90)))
            {
                SampleOnce();
            }
            GUI.enabled = true;
        }

        GUILayout.Space(5);

        GUI.enabled = Application.isPlaying && trackedFields.Any(t => t.isChecked);
        if (!isTracking)
        {
            if (GUILayout.Button("⏺ 記録開始", GUILayout.Height(24))) StartTracking();
        }
        else
        {
            GUI.color = Color.red;
            if (GUILayout.Button("⏹ 記録停止", GUILayout.Height(24))) StopTracking();
            GUI.color = Color.white;
        }
        GUI.enabled = true;

        if (!Application.isPlaying)
        {
            EditorGUILayout.LabelField("※ 自動記録は再生中のみ有効です(手動記録は再生外でも可)。", EditorStyles.miniLabel);
        }

        GUILayout.Space(8);

        foreach (var tf in trackedFields.Where(t => t.isChecked && t.logLines.Count > 0))
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                tf.isLogExpanded = EditorGUILayout.Foldout(tf.isLogExpanded, $"📋 {tf.label} ({tf.logLines.Count}件)", true);
                if (tf.isLogExpanded)
                {
                    tf.logScroll = EditorGUILayout.BeginScrollView(tf.logScroll, GUILayout.Height(100));
                    foreach (var line in tf.logLines)
                    {
                        EditorGUILayout.LabelField(line, EditorStyles.miniLabel);
                    }
                    EditorGUILayout.EndScrollView();
                }
            }
            GUILayout.Space(4);
        }
    }

    private static void ScanFields(GameObject obj)
    {
        trackedFields.Clear();

        Transform t = obj.transform;
        trackedFields.Add(new TrackedField { label = "Position.x", getter = () => t.position.x });
        trackedFields.Add(new TrackedField { label = "Position.y", getter = () => t.position.y });
        trackedFields.Add(new TrackedField { label = "Position.z", getter = () => t.position.z });
        trackedFields.Add(new TrackedField { label = "Rotation.y (Euler)", getter = () => t.eulerAngles.y });

        foreach (var comp in obj.GetComponents<Component>())
        {
            if (comp == null || comp is Transform) continue;
            System.Type type = comp.GetType();

            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (field.FieldType != typeof(float) && field.FieldType != typeof(int)) continue;

                var capturedComp = comp;
                var capturedField = field;
                trackedFields.Add(new TrackedField
                {
                    label = $"{type.Name}.{field.Name}",
                    getter = () => System.Convert.ToSingle(capturedField.GetValue(capturedComp))
                });
            }
        }
    }

    private static void StartTracking()
    {
        isTracking = true;
        lastSampleTime = EditorApplication.timeSinceStartup;
        foreach (var tf in trackedFields.Where(t => t.isChecked))
        {
            tf.logLines.Clear();
            tf.hasLastValue = false;
        }
        EditorApplication.update += OnUpdate;
    }

    private static void StopTracking()
    {
        isTracking = false;
        EditorApplication.update -= OnUpdate;
    }

    private static void OnUpdate()
    {
        if (!Application.isPlaying) return;

        // 🌟 サンプリング間隔が設定されていれば、その間隔まで待つ(0なら毎フレーム実行)
        if (sampleInterval > 0f)
        {
            if (EditorApplication.timeSinceStartup - lastSampleTime < sampleInterval) return;
            lastSampleTime = EditorApplication.timeSinceStartup;
        }

        bool changed = false;

        foreach (var tf in trackedFields.Where(t => t.isChecked))
        {
            float value;
            try { value = tf.getter(); }
            catch { continue; }

            if (!tf.hasLastValue || Mathf.Abs(tf.lastValue - value) >= changeThreshold)
            {
                string time = System.DateTime.Now.ToString("HH:mm:ss");
                tf.logLines.Add($"[{time}] {tf.lastValue:F2} → {value:F2}");
                if (tf.logLines.Count > maxLogLines) tf.logLines.RemoveAt(0);

                tf.lastValue = value;
                tf.hasLastValue = true;
                changed = true;
            }
        }

        if (changed && EditorWindow.HasOpenInstances<Toolkit>())
        {
            EditorWindow.GetWindow<Toolkit>().Repaint();
        }
    }

    // 🌟 間隔・記録状態に関係なく、押した瞬間だけ強制的に1回サンプリングする
    private static void SampleOnce()
    {
        bool changed = false;
        foreach (var tf in trackedFields.Where(t => t.isChecked))
        {
            float value;
            try { value = tf.getter(); }
            catch { continue; }

            string time = System.DateTime.Now.ToString("HH:mm:ss");
            tf.logLines.Add($"[{time}] {tf.lastValue:F2} → {value:F2} (手動)");
            if (tf.logLines.Count > maxLogLines) tf.logLines.RemoveAt(0);

            tf.lastValue = value;
            tf.hasLastValue = true;
            changed = true;
        }

        if (changed && EditorWindow.HasOpenInstances<Toolkit>())
        {
            EditorWindow.GetWindow<Toolkit>().Repaint();
        }
    }
}