using UnityEditor;
using UnityEngine;
using TMPro;
using System.Collections.Generic;
using System.Linq;

public static class ToolkitTextManager
{
    private enum ScanMode { AllInScene, UnderSelected }
    private static ScanMode scanMode = ScanMode.AllInScene;

    private static List<TextMeshProUGUI> foundTexts = new List<TextMeshProUGUI>();
    private static List<bool> checks = new List<bool>();
    private static List<bool> expanded = new List<bool>();
    private static Vector2 scroll;

    // 一括変更用(フォント・サイズのみ)
    private static bool applyFont = false;
    private static TMP_FontAsset newFont;
    private static bool applyFontSize = false;
    private static float newFontSize = 24f;

    public static void DrawMenu()
    {
        EditorGUILayout.HelpBox("TextMeshProUGUIを一覧取得します。チェックで一括変更、▼を開くと個別編集ができます。", MessageType.Info);

        scanMode = (ScanMode)EditorGUILayout.EnumPopup("Scan Mode", scanMode);

        if (GUILayout.Button("🔄 取得", GUILayout.Height(24)))
        {
            ScanTexts();
        }

        GUILayout.Space(5);

        if (foundTexts.Count > 0)
        {
            scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.Height(200));
            for (int i = 0; i < foundTexts.Count; i++)
            {
                if (foundTexts[i] == null) continue;
                DrawRow(i);
            }
            EditorGUILayout.EndScrollView();

            GUILayout.Space(8);
            EditorGUILayout.LabelField("チェックした項目に一括適用(フォント・サイズのみ)", EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                applyFont = EditorGUILayout.Toggle(applyFont, GUILayout.Width(20));
                newFont = (TMP_FontAsset)EditorGUILayout.ObjectField("Font", newFont, typeof(TMP_FontAsset), false);
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                applyFontSize = EditorGUILayout.Toggle(applyFontSize, GUILayout.Width(20));
                newFontSize = EditorGUILayout.FloatField("Font Size", newFontSize);
            }

            GUILayout.Space(5);
            int checkedCount = checks.Count(c => c);
            GUI.enabled = checkedCount > 0 && (applyFont || applyFontSize);
            if (GUILayout.Button($"✅ 選択した{checkedCount}件に一括適用", GUILayout.Height(28)))
            {
                ApplyBatch();
            }
            GUI.enabled = true;
        }
        else
        {
            EditorGUILayout.LabelField("「取得」を押してください。", EditorStyles.miniLabel);
        }
    }

    private static void DrawRow(int i)
    {
        TextMeshProUGUI tmp = foundTexts[i];

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                checks[i] = EditorGUILayout.Toggle(checks[i], GUILayout.Width(20));
                expanded[i] = EditorGUILayout.Foldout(expanded[i], tmp.gameObject.name, true);
                if (GUILayout.Button("Ping", GUILayout.Width(45)))
                {
                    EditorGUIUtility.PingObject(tmp.gameObject);
                }
            }

            // 🌟 ここが個別編集エリア。開いた行だけ、そのオブジェクト専用のPosition/Font/Sizeをその場で編集
            if (expanded[i])
            {
                EditorGUI.indentLevel++;
                RectTransform rt = tmp.rectTransform;

                EditorGUI.BeginChangeCheck();
                Vector2 pos = EditorGUILayout.Vector2Field("Position", rt.anchoredPosition);
                TMP_FontAsset font = (TMP_FontAsset)EditorGUILayout.ObjectField("Font", tmp.font, typeof(TMP_FontAsset), false);
                float size = EditorGUILayout.FloatField("Font Size", tmp.fontSize);

                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(rt, "Toolkit Text Edit");
                    Undo.RecordObject(tmp, "Toolkit Text Edit");
                    rt.anchoredPosition = pos;
                    tmp.font = font;
                    tmp.fontSize = size;
                    EditorUtility.SetDirty(tmp);
                }
                EditorGUI.indentLevel--;
            }
        }
    }

    private static void ScanTexts()
    {
        foundTexts.Clear();
        checks.Clear();
        expanded.Clear();

        IEnumerable<TextMeshProUGUI> results;

        if (scanMode == ScanMode.AllInScene)
        {
            results = Object.FindObjectsByType<TextMeshProUGUI>(FindObjectsSortMode.None);
        }
        else
        {
            GameObject root = Selection.activeGameObject;
            results = root != null ? root.GetComponentsInChildren<TextMeshProUGUI>(true) : new TextMeshProUGUI[0];
        }

        foundTexts = results.OrderBy(t => t.gameObject.name).ToList();
        checks = foundTexts.Select(_ => false).ToList();
        expanded = foundTexts.Select(_ => false).ToList();
    }

    private static void ApplyBatch()
    {
        for (int i = 0; i < foundTexts.Count; i++)
        {
            if (!checks[i] || foundTexts[i] == null) continue;

            TextMeshProUGUI tmp = foundTexts[i];
            Undo.RecordObject(tmp, "Toolkit UI Batch Edit");

            if (applyFont) tmp.font = newFont;
            if (applyFontSize) tmp.fontSize = newFontSize;

            EditorUtility.SetDirty(tmp);
        }
    }
}