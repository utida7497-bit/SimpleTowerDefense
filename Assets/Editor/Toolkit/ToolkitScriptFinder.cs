using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public static class ToolkitScriptFinder
{
    private static MonoScript targetScript;
    private static List<Object> sceneResults = new List<Object>();
    private static List<Object> prefabResults = new List<Object>();
    private static Vector2 scroll;

    public static void DrawMenu()
    {
        EditorGUILayout.HelpBox("指定したスクリプトが付いているオブジェクトを、シーン内とAssets内のプレハブから検索します。", MessageType.Info);
        targetScript = (MonoScript)EditorGUILayout.ObjectField("Find Script", targetScript, typeof(MonoScript), false);

        GUI.enabled = targetScript != null;
        if (GUILayout.Button("🔍 検索", GUILayout.Height(24)))
        {
            Search();
        }
        GUI.enabled = true;

        GUILayout.Space(5);

        bool hasResults = sceneResults.Count > 0 || prefabResults.Count > 0;

        if (hasResults)
        {
            scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.Height(100));

            if (sceneResults.Count > 0)
            {
                EditorGUILayout.LabelField($"🎬 シーン内 ({sceneResults.Count})", EditorStyles.boldLabel);
                foreach (var obj in sceneResults) DrawResultRow(obj);
            }

            if (prefabResults.Count > 0)
            {
                EditorGUILayout.LabelField($"📦 プレハブ ({prefabResults.Count})", EditorStyles.boldLabel);
                foreach (var obj in prefabResults) DrawResultRow(obj);
            }

            EditorGUILayout.EndScrollView();
        }
        else if (targetScript != null)
        {
            EditorGUILayout.LabelField("見つかりませんでした。");
        }
    }

    private static void DrawResultRow(Object obj)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.ObjectField(obj, typeof(Object), true);
            if (GUILayout.Button("Ping", GUILayout.Width(45)))
            {
                EditorGUIUtility.PingObject(obj);
                Selection.activeObject = obj;
            }
        }
    }

    private static void Search()
    {
        sceneResults.Clear();
        prefabResults.Clear();

        System.Type type = targetScript.GetClass();
        if (type == null || !typeof(Component).IsAssignableFrom(type)) return;

        // 🌟 シーン内(開いているシーンに配置済みのオブジェクト)
        Object[] sceneFound = Object.FindObjectsByType(type, FindObjectsSortMode.None);
        foreach (var obj in sceneFound)
        {
            GameObject go = ((Component)obj).gameObject;
            // シーンビュー上のプレハブインスタンスも含めシーン内として扱う
            sceneResults.Add(go);
        }

        // 🌟 Assets内のプレハブ(シーンに置かれていないものも含む)
        string[] guids = AssetDatabase.FindAssets("t:Prefab");
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) continue;

            if (prefab.GetComponentInChildren(type, true) != null)
            {
                prefabResults.Add(prefab);
            }
        }
    }
}