using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

public static class ToolkitPrintShot
{
    private static EditorWindow targetWindow;   // null = Toolkit自身
    private static List<EditorWindow> openWindows = new List<EditorWindow>();
    private static string[] windowLabels = new string[0];

    public static void DrawMenu(EditorWindow ownerWindow)
    {
        EditorGUILayout.HelpBox("撮影するタブを選んでPNG保存します。裏に隠れているタブは、自動で前面に出して撮影します。", MessageType.Info);

        if (openWindows.Count == 0) RefreshWindows();

        int current = targetWindow != null ? openWindows.IndexOf(targetWindow) : openWindows.IndexOf(ownerWindow);
        if (current < 0) current = 0;

        using (new EditorGUILayout.HorizontalScope())
        {
            int next = EditorGUILayout.Popup("Capture Target", current, windowLabels);
            if (next != current && next >= 0 && next < openWindows.Count) targetWindow = openWindows[next];

            // タブを開閉した後は、ここで一覧を更新する
            if (GUILayout.Button("🔄", GUILayout.Width(28)))
                EditorApplication.delayCall += RefreshWindows;
        }

        GUILayout.Space(5);
        if (GUILayout.Button("📸 選んだタブをPNG保存", GUILayout.Height(28)))
        {
            EditorWindow target = targetWindow != null ? targetWindow : ownerWindow;
            EditorApplication.delayCall += () => BeginCapture(target, ownerWindow);
        }
    }

    // 🌟 今存在するEditorWindow(裏のタブも含む)を集めて、選択肢にする
    private static void RefreshWindows()
    {
        openWindows = Resources.FindObjectsOfTypeAll<EditorWindow>()
            .Where(w => w != null && !string.IsNullOrWhiteSpace(w.titleContent.text))
            .OrderBy(w => w.titleContent.text)
            .ToList();

        Dictionary<string, int> counts = new Dictionary<string, int>();
        List<string> labels = new List<string>();
        foreach (EditorWindow w in openWindows)
        {
            string title = w.titleContent.text.Replace('/', '-');   // 「/」はメニューの階層になってしまうため
            counts.TryGetValue(title, out int n);
            counts[title] = n + 1;
            labels.Add(n == 0 ? title : $"{title} ({n + 1})");       // 同名のタブは (2) を付けて区別
        }
        windowLabels = labels.ToArray();

        if (EditorWindow.HasOpenInstances<Toolkit>()) EditorWindow.GetWindow<Toolkit>().Repaint();
    }

    // 🌟 対象を前面に出して、描き直しを少し待ってから撮る(裏のタブは、前面に出ないと画面に写らない)
    private static void BeginCapture(EditorWindow target, EditorWindow owner)
    {
        if (target == null) return;

        string label = target.titleContent.text;
        target.Focus();
        target.Repaint();

        double captureAt = EditorApplication.timeSinceStartup + 0.2;
        EditorApplication.CallbackFunction wait = null;
        wait = () =>
        {
            if (EditorApplication.timeSinceStartup < captureAt) return;
            EditorApplication.update -= wait;

            if (target != null) CaptureNow(target, label);
            if (owner != null) owner.Focus();   // 撮り終わったらToolkitに戻る
        };
        EditorApplication.update += wait;
    }

    private static void CaptureNow(EditorWindow target, string label)
    {
        Rect rect = target.position;
        int w = (int)rect.width;
        int h = (int)rect.height;
        if (w <= 0 || h <= 0)
        {
            Debug.LogWarning($"[Toolkit] {label} のサイズが取得できませんでした(最小化されている可能性があります)。");
            return;
        }

        Color[] screenPixels = UnityEditorInternal.InternalEditorUtility.ReadScreenPixel(new Vector2(rect.x, rect.y), w, h);
        Texture2D tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.SetPixels(screenPixels);
        tex.Apply();
        byte[] bytes = tex.EncodeToPNG();
        Object.DestroyImmediate(tex);

        string folderPath = "Assets/Editor/WindowSnapshots";
        if (!Directory.Exists(folderPath)) Directory.CreateDirectory(folderPath);

        string safeName = Regex.Replace(label, @"[\\/:*?""<>|\s]", "_");
        string fullPath = Path.Combine(folderPath, $"{safeName}_{System.DateTime.Now:yyyyMMdd_HHmmss}.png");
        File.WriteAllBytes(fullPath, bytes);
        AssetDatabase.Refresh();

        Debug.Log($"[Toolkit] {label} を保存しました: {fullPath}");
    }
}