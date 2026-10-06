using UnityEditor;
using UnityEngine;
using System.Collections.Generic;

public class ToolkitSimpleNotepad : EditorWindow
{
    private const string SaveKeyPrefix = "Toolkit_SimpleNotepad_SaveData_V4_";
    private const string NotepadControlName = "Toolkit_NotepadTextArea";

    private static string SaveKey => SaveKeyPrefix + Application.productName;

    public static void OpenWindow() => GetWindow<ToolkitSimpleNotepad>("簡易メモ帳").Show();

    [System.Serializable]
    public class SimpleNotepadSaveWrapper
    {
        public string notepadMainText = "";
        public List<int> assetInstanceIDs = new List<int>();
    }

    [SerializeField] private string notepadMainText = "";
    [SerializeField] private List<int> assetInstanceIDs = new List<int>();
    private List<Object> assetObjects = new List<Object>();
    private Vector2 leftScroll;
    private Vector2 rightScroll;

    private void OnEnable()
    {
        string json = EditorPrefs.GetString(SaveKey, "");
        if (!string.IsNullOrEmpty(json))
        {
            var wrapper = JsonUtility.FromJson<SimpleNotepadSaveWrapper>(json);
            notepadMainText = wrapper.notepadMainText;
            assetInstanceIDs = wrapper.assetInstanceIDs;
            assetObjects.Clear();
            foreach (int id in assetInstanceIDs)
            {
                assetObjects.Add(id != 0 ? EditorUtility.InstanceIDToObject(id) : null);
            }
        }
    }

    private void OnGUI()
    {
        EditorGUILayout.BeginHorizontal();
        EditorGUI.BeginChangeCheck();

        // 🧱 左側：アセット起動ショートカットリスト（幅260px固定）
        EditorGUILayout.BeginVertical(GUILayout.Width(260), GUILayout.ExpandHeight(true));
        leftScroll = EditorGUILayout.BeginScrollView(leftScroll, "box", GUILayout.Width(245), GUILayout.ExpandHeight(true));

        int itemToRemove = -1;
        while (assetObjects.Count < assetInstanceIDs.Count) assetObjects.Add(null);

        for (int i = 0; i < assetInstanceIDs.Count; i++)
        {
            Rect itemRect = EditorGUILayout.BeginHorizontal("helpBox", GUILayout.Width(225), GUILayout.Height(38));

            EditorGUILayout.BeginVertical(); GUILayout.Space(11);
            Object oldObj = assetObjects[i];
            assetObjects[i] = EditorGUILayout.ObjectField(oldObj, typeof(Object), true, GUILayout.Width(150));
            if (assetObjects[i] != oldObj) assetInstanceIDs[i] = assetObjects[i] != null ? assetObjects[i].GetInstanceID() : 0;
            EditorGUILayout.EndVertical();

            GUILayout.Space(2); EditorGUILayout.BeginVertical(); GUILayout.Space(2);
            if (GUILayout.Button("▲", GUILayout.Width(20), GUILayout.Height(15)) && i > 0)
            {
                var tID = assetInstanceIDs[i]; assetInstanceIDs[i] = assetInstanceIDs[i - 1]; assetInstanceIDs[i - 1] = tID;
                var tObj = assetObjects[i]; assetObjects[i] = assetObjects[i - 1]; assetObjects[i - 1] = tObj;
            }
            if (GUILayout.Button("▼", GUILayout.Width(20), GUILayout.Height(15)) && i < assetInstanceIDs.Count - 1)
            {
                var tID = assetInstanceIDs[i]; assetInstanceIDs[i] = assetInstanceIDs[i + 1]; assetInstanceIDs[i + 1] = tID;
                var tObj = assetObjects[i]; assetObjects[i] = assetObjects[i + 1]; assetObjects[i + 1] = tObj;
            }
            EditorGUILayout.EndVertical();

            GUILayout.Space(2); EditorGUILayout.BeginVertical(); GUILayout.Space(10);
            if (GUILayout.Button("×", GUILayout.Width(18), GUILayout.Height(18)))
            {
                itemToRemove = i;

                // 🌟 バグスナップショット(BugSnapshotsフォルダ内のプレハブ)なら、実ファイルも一緒に削除
                if (assetObjects[i] != null)
                {
                    string path = AssetDatabase.GetAssetPath(assetObjects[i]);
                    if (path.StartsWith("Assets/Editor/BugSnapshots/"))
                    {
                        AssetDatabase.DeleteAsset(path);
                    }
                }
            }
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal(); GUILayout.Space(2);
        }

        if (itemToRemove != -1) { assetInstanceIDs.RemoveAt(itemToRemove); assetObjects.RemoveAt(itemToRemove); }

        GUILayout.Space(8);
        if (GUILayout.Button("アセットを追加 ➕", GUILayout.Width(225), GUILayout.Height(24)))
        {
            assetInstanceIDs.Add(0); assetObjects.Add(null);
        }
        GUILayout.Space(4);
        GUI.color = new Color(1f, 0.5f, 0.4f, 1f);
        if (GUILayout.Button("🐛 バグとして記録", GUILayout.Width(225), GUILayout.Height(24)))
        {
            RecordBugSnapshot();
        }
        GUI.color = Color.white;

        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();

        GUILayout.Space(5);

        // 🧱 右側：たった1枚の完全に独立した広大な「共通フリーメモ帳」
        EditorGUILayout.BeginVertical("box", GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
        EditorGUILayout.LabelField("📝 メモ帳", EditorStyles.boldLabel);
        GUILayout.Space(5);

        rightScroll = EditorGUILayout.BeginScrollView(rightScroll, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));

        GUI.SetNextControlName(NotepadControlName);
        // Editor専用の EditorGUILayout.TextArea を使用。
        // IME(日本語入力)、Backspace/Delete、テンキーなど、Editor上でのテキスト編集に
        // 必要なネイティブ連携はこちらでないと正しく機能しない。
        notepadMainText = EditorGUILayout.TextArea(notepadMainText, EditorStyles.textArea, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));

        EditorGUILayout.EndScrollView();

        EditorGUILayout.EndVertical();
        EditorGUILayout.EndHorizontal();

        if (EditorGUI.EndChangeCheck())
        {
            SaveData();
        }
    }
    private void RecordBugSnapshot()
    {
        GameObject target = Selection.activeGameObject;
        if (target == null)
        {
            EditorUtility.DisplayDialog("警告", "記録するオブジェクトを選択してください。", "OK");
            return;
        }

        string folderPath = "Assets/Editor/BugSnapshots";
        if (!AssetDatabase.IsValidFolder(folderPath))
        {
            AssetDatabase.CreateFolder("Assets/Editor", "BugSnapshots");
        }

        string timestamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string fileName = $"{target.name}_{timestamp}.prefab";
        string fullPath = $"{folderPath}/{fileName}";

        // 🌟 実行中の値をそのまま凍結したプレハブを生成
        GameObject snapshot = PrefabUtility.SaveAsPrefabAsset(target, fullPath, out bool success);

        if (success && snapshot != null)
        {
            // メモ帳の左リストに自動追加
            assetInstanceIDs.Add(snapshot.GetInstanceID());
            assetObjects.Add(snapshot);

            // メモ本文に自動で書き出しを追記
            string header = $"\n[{System.DateTime.Now:yyyy/MM/dd HH:mm}] {target.name} でバグ発生 - ";
            notepadMainText += header;

            // カーソルをテキストの末尾に移動(次の操作で続きをすぐ書けるように)
            GUI.FocusControl(NotepadControlName);

            Debug.Log($"[Toolkit] バグスナップショットを保存: {fullPath}");
        }
        else
        {
            Debug.LogWarning("[Toolkit] スナップショットの保存に失敗しました。");
        }
    }
    private void SaveData()
    {
        var wrapper = new SimpleNotepadSaveWrapper { notepadMainText = notepadMainText, assetInstanceIDs = assetInstanceIDs };
        EditorPrefs.SetString(SaveKey, JsonUtility.ToJson(wrapper));
    }
}