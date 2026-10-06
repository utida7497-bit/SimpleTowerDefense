using UnityEditor;
using UnityEngine;
using System.Collections.Generic;

public static class ToolkitAssetLauncher
{
    [System.Serializable]
    public class LauncherContainer
    {
        public List<Object> assets = new List<Object>() { null };
    }

    private static List<LauncherContainer> allLaunchers = new List<LauncherContainer>();
    private static bool isLoaded = false;
    private const string SaveKeyPrefix = "Toolkit_AssetLauncher_SaveData_";

    // 🌟 大元の Toolkit.cs が各コンポーネントの「現在の正確な縦幅」を計算するために要素数を返す関数
    public static int GetAssetCount(int compIndex)
    {
        if (!isLoaded) LoadAllData();
        if (compIndex >= allLaunchers.Count) return 1;
        return allLaunchers[compIndex].assets.Count;
    }

    // 1. ハードディスクからセーブデータを安全にロード
    private static void LoadAllData()
    {
        if (isLoaded) return;

        allLaunchers.Clear();
        for (int compIdx = 0; compIdx < 50; compIdx++)
        {
            string countKey = SaveKeyPrefix + compIdx + "_Count";
            if (!EditorPrefs.HasKey(countKey)) continue;

            int count = EditorPrefs.GetInt(countKey);
            LauncherContainer container = new LauncherContainer();
            container.assets.Clear();

            for (int i = 0; i < count; i++)
            {
                string pathKey = SaveKeyPrefix + compIdx + "_AssetPath_" + i;
                string assetPath = EditorPrefs.GetString(pathKey, "");

                Object loadedAsset = null;
                if (!string.IsNullOrEmpty(assetPath))
                {
                    loadedAsset = AssetDatabase.LoadAssetAtPath<Object>(assetPath);
                }
                container.assets.Add(loadedAsset);
            }
            allLaunchers.Add(container);
        }
        isLoaded = true;
    }

    // 2. 落ち着いたタイミングで安全に書き出しを行うセーブエンジン
    private static void SaveAllData()
    {
        for (int compIdx = 0; compIdx < 50; compIdx++)
        {
            string countKey = SaveKeyPrefix + compIdx + "_Count";
            if (EditorPrefs.HasKey(countKey)) EditorPrefs.DeleteKey(countKey);
        }

        for (int compIdx = 0; compIdx < allLaunchers.Count; compIdx++)
        {
            LauncherContainer container = allLaunchers[compIdx];
            EditorPrefs.SetInt(SaveKeyPrefix + compIdx + "_Count", container.assets.Count);

            for (int i = 0; i < container.assets.Count; i++)
            {
                string pathKey = SaveKeyPrefix + compIdx + "_AssetPath_" + i;
                if (container.assets[i] != null)
                {
                    if ((container.assets[i].hideFlags & HideFlags.DontSave) != 0)
                    {
                        EditorPrefs.SetString(pathKey, "");
                        continue;
                    }

                    string assetPath = AssetDatabase.GetAssetPath(container.assets[i]);
                    EditorPrefs.SetString(pathKey, assetPath);
                }
                else
                {
                    EditorPrefs.SetString(pathKey, "");
                }
            }
        }
    }

    // Toolkit.csの大元画面から呼ばれる、描画コントロール処理
    public static void DrawMenu(int compIndex, EditorWindow window)
    {
        if (!isLoaded) LoadAllData();

        while (allLaunchers.Count <= compIndex)
        {
            allLaunchers.Add(new LauncherContainer());
        }

        EditorGUILayout.HelpBox("右側の水色パレットを長押しドラッグしてヒエラルキーへ配置してください。\n🌟ヒエラルキーやSceneビューのオブジェクトを直接枠に落として登録も可能です！", MessageType.None);
        LauncherContainer currentContainer = allLaunchers[compIndex];

        bool isChanged = false;

        for (int i = 0; i < currentContainer.assets.Count; i++)
        {
            // 1行に「アセット名、丸マーク、水色パレット、✕ボタン」を美しく収めるレイアウト
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                Object savedAsset = currentContainer.assets[i];

                // アセット登録フィールドの幅を「45%」に強制固定し、右側ボタンの圧迫を完全ブロック
                float fieldWidth = (window.position.width - 80f) * 0.45f;
                if (fieldWidth < 100f) fieldWidth = 100f;

                Object nextAsset = EditorGUILayout.ObjectField(savedAsset, typeof(Object), false, GUILayout.Width(fieldWidth));

                if (nextAsset != savedAsset)
                {
                    // 【ヒエラルキー直接ドロップ対応】シーン内のオブジェクトなら大元のプレハブを逆引き
                    if (nextAsset is GameObject gameObject)
                    {
                        Object sourcePrefab = PrefabUtility.GetCorrespondingObjectFromSource(gameObject);
                        if (sourcePrefab != null)
                        {
                            nextAsset = sourcePrefab;
                        }
                        else
                        {
                            string path = AssetDatabase.GetAssetPath(gameObject);
                            if (string.IsNullOrEmpty(path))
                            {
                                Debug.LogWarning("[Toolkit] プレハブ化されていないシーン内のオブジェクトは登録できません。一度プレハブにしてから登録してください。");
                                nextAsset = null;
                            }
                        }
                    }

                    if (nextAsset != null && (nextAsset.hideFlags & HideFlags.DontSave) != 0)
                    {
                        nextAsset = null;
                    }

                    currentContainer.assets[i] = nextAsset;
                    isChanged = true;
                    window.Repaint();
                }

                // 登録されている場合、すぐ右側の隙間に幅広のドラッグパレットをジャスト配置！
                if (currentContainer.assets[i] != null)
                {
                    Object assetObj = currentContainer.assets[i];
                    GUIStyle palletStyle = new GUIStyle(EditorStyles.objectFieldThumb) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, fontSize = 10 };
                    palletStyle.normal.textColor = Color.cyan;

                    // 🌟 ✕ボタン(20px)+間隔(4px)を確実に確保した「残り幅」だけをパレットに割り当てる
                    float remainingWidth = (window.position.width - 80f) - fieldWidth - 24f;
                    if (remainingWidth < 40f) remainingWidth = 40f;

                    string label = $" 🖐️ 掴んで配置: {assetObj.name} ";
                    Rect dragRect = GUILayoutUtility.GetRect(remainingWidth, 18f, palletStyle);
                    GUI.Box(dragRect, label, palletStyle);

                    Event ev = Event.current;
                    if (ev.type == EventType.MouseDrag && dragRect.Contains(ev.mousePosition))
                    {
                        DragAndDrop.PrepareStartDrag();
                        DragAndDrop.objectReferences = new Object[] { assetObj };
                        DragAndDrop.paths = new string[] { AssetDatabase.GetAssetPath(assetObj) };
                        DragAndDrop.StartDrag($"Dragging {assetObj.name}");
                        ev.Use();
                    }
                }
                else
                {
                    GUIStyle infoStyle = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleLeft };
                    infoStyle.normal.textColor = Color.gray;
                    GUILayout.Label("（ここにアセットをドロップ）", infoStyle, GUILayout.ExpandWidth(true));
                }

                if (GUILayout.Button("✕", EditorStyles.miniButton, GUILayout.Width(20), GUILayout.Height(18)))
                {
                    currentContainer.assets.RemoveAt(i);
                    isChanged = true;
                    window.Repaint();
                    return;
                }
            }
        }

        GUILayout.Space(2);
        GUI.color = new Color(0.5f, 0.9f, 0.5f, 1f);
        if (GUILayout.Button("➕ 新しいお気に入り保存枠を追加", GUILayout.Height(20)))
        {
            currentContainer.assets.Add(null);
            isChanged = true;
            window.Repaint();
        }
        GUI.color = Color.white;

        if (isChanged)
        {
            EditorApplication.delayCall += SaveAllData;
        }
    }

    public static void OnComponentSwapped(int oldIndex, int newIndex)
    {
        if (!isLoaded) LoadAllData();
        if (oldIndex >= allLaunchers.Count || newIndex >= allLaunchers.Count) return;

        LauncherContainer temp = allLaunchers[oldIndex];
        allLaunchers[oldIndex] = allLaunchers[newIndex];
        allLaunchers[newIndex] = temp;

        EditorApplication.delayCall += SaveAllData;
    }

    public static void OnComponentRemoved(int index)
    {
        if (!isLoaded) LoadAllData();
        if (index < allLaunchers.Count)
        {
            allLaunchers.RemoveAt(index);
            EditorApplication.delayCall += SaveAllData;
        }
    }
}
