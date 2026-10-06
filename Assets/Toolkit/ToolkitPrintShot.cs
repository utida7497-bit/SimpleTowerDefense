using UnityEditor;
using UnityEngine;
using System.IO;
public static class ToolkitPrintShot
{
    private enum ShotMode { ToolkitWindow, SceneView, GameView, Inspector }
    private static ShotMode currentShotMode = ShotMode.ToolkitWindow;

    public static void DrawMenu(EditorWindow ownerWindow)
    {
        EditorGUILayout.HelpBox("撮影対象を選んで高画質なPNG画像として保存します。", MessageType.Info);
        currentShotMode = (ShotMode)EditorGUILayout.EnumPopup("Capture Target", currentShotMode);
        GUILayout.Space(5);
        if (GUILayout.Button("📸 指定したウィンドウをPNG保存", GUILayout.Height(28)))
        {
            EditorApplication.delayCall += () => ExecutePerfectOSCapture(ownerWindow);
        }
    }

    private static void ExecutePerfectOSCapture(EditorWindow ownerWindow)
    {
        EditorWindow targetWin = ownerWindow;
        if (currentShotMode == ShotMode.SceneView)
        {
            targetWin = EditorWindow.GetWindow<SceneView>();
        }
        else if (currentShotMode == ShotMode.GameView)
        {
            System.Type gameViewType = System.Type.GetType("UnityEditor.GameView,UnityEditor");
            if (gameViewType != null) targetWin = EditorWindow.GetWindow(gameViewType);
        }
        else if (currentShotMode == ShotMode.Inspector)
        {
            System.Type inspectorType = System.Type.GetType("UnityEditor.InspectorWindow,UnityEditor");
            if (inspectorType != null) targetWin = EditorWindow.GetWindow(inspectorType);
        }
        if (targetWin == null) return;
        Rect rect = targetWin.position;
        int w = (int)rect.width;
        int h = (int)rect.height;
        targetWin.Focus();
        Color[] screenPixels = UnityEditorInternal.InternalEditorUtility.ReadScreenPixel(
            new Vector2(rect.x, rect.y), w, h);
        Texture2D finalTex = new Texture2D(w, h, TextureFormat.RGB24, false);
        finalTex.SetPixels(screenPixels);
        finalTex.Apply();
        byte[] bytes = finalTex.EncodeToPNG();
        Object.DestroyImmediate(finalTex);
        string folderPath = "Assets/Editor/WindowSnapshots";
        if (!Directory.Exists(folderPath)) Directory.CreateDirectory(folderPath);
        string filename = $"{currentShotMode}_{System.DateTime.Now:yyyyMMdd_HHmmss}.png";
        string fullPath = Path.Combine(folderPath, filename);
        File.WriteAllBytes(fullPath, bytes);
        AssetDatabase.Refresh();
        Debug.Log($"[Toolkit] {currentShotMode} を見た目通り正確に保存しました: {fullPath}");
    }
}