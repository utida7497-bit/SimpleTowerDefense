using UnityEditor;
using UnityEngine;

public class ToolkitPopup : EditorWindow
{
    private System.Action<string> onToolSelected;

    public static void Open(Rect buttonRect, System.Action<string> onSelected)
    {
        ToolkitPopup window = CreateInstance<ToolkitPopup>();
        window.onToolSelected = onSelected;
        Vector2 screenPos = GUIUtility.GUIToScreenPoint(new Vector2(buttonRect.x, buttonRect.y + buttonRect.height));
        window.ShowAsDropDown(new Rect(screenPos.x, screenPos.y, 0, 0), new Vector2(300, 260));
    }

    private void OnGUI()
    {
        GUILayout.Label("🔌 追加するコンポーネントを選択", EditorStyles.boldLabel);
        GUILayout.Space(5);

        if (GUILayout.Button("📸 Window Print Shot (スクショ) を追加", GUILayout.Height(24))) { onToolSelected?.Invoke("WindowPrintShot"); Close(); }
        if (GUILayout.Button("🔨 Script Generator (生成) を追加", GUILayout.Height(24))) { onToolSelected?.Invoke("ScriptGenerator"); Close(); }
        if (GUILayout.Button("🔍 Attached Script Checker (オブジェクト確認) を追加", GUILayout.Height(24))) { onToolSelected?.Invoke("AttachedScriptChecker"); Close(); }
        if (GUILayout.Button("🔍 ToolkitScriptFinder (スクリプト確認) を追加", GUILayout.Height(24))) { onToolSelected?.Invoke("ToolkitScriptFinder"); Close(); }
        if (GUILayout.Button("🚀 Asset Launcher (ドラッグ配置) を追加", GUILayout.Height(24))) { onToolSelected?.Invoke("AssetLauncher"); Close(); }
        if (GUILayout.Button("⏱ Time Scale (時間操作) を追加", GUILayout.Height(24))) { onToolSelected?.Invoke("TimeScale"); Close(); }
        if (GUILayout.Button("🔤 UI Text Manager (一括編集) を追加", GUILayout.Height(24))) { onToolSelected?.Invoke("TextManager"); Close(); }
        // ⭕【大開通】：大元のswitch分岐なんて一切無視！
        // ボタンが押されたそのミリ秒の瞬間に、イラスト通りの独立2ペインメモ帳タブをその場からダイレクトに一発起動させます！
        if (GUILayout.Button("📝 Simple Notepad (簡易メモ帳) を追加", GUILayout.Height(24))) { ToolkitSimpleNotepad.OpenWindow(); Close(); }
        if (GUILayout.Button("📈 Object Tracker (値の推移記録) を追加", GUILayout.Height(24))) { onToolSelected?.Invoke("ObjectTracker"); Close(); }
    }
}
