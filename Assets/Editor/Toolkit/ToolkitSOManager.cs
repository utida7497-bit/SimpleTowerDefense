using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

public static class ToolkitSOManager
{
    private static MonoScript targetScript;
    private static System.Type targetType;

    private static List<ScriptableObject> found = new List<ScriptableObject>();
    private static List<bool> checks = new List<bool>();
    private static List<bool> expanded = new List<bool>();
    private static Vector2 scroll;

    // 一括変更用
    private static List<string> batchPaths = new List<string>();
    private static List<SerializedPropertyType> batchTypes = new List<SerializedPropertyType>();
    private static string[] batchLabels = new string[0];
    private static int batchIndex = 0;
    private static float batchNumber = 0f;
    private static bool batchBool = false;
    private static string batchString = "";

    // 新規作成用
    private static string newName = "NewData";
    private static string targetFolder = "Assets";

    private const string SessionKey = "Toolkit_SOManager_Target";

    private static readonly string[] fieldTypeNames = { "int", "float", "bool", "string", "Vector2", "Vector3", "Color", "GameObject", "Sprite" };
    private static int newFieldTypeIndex = 0;
    private static string newFieldName = "";
    private static string newFieldDefault = "";
    private static bool showAddField = false;
    private static List<string> pendingLines = new List<string>();   // 書き込み待ちの宣言行
    private static List<string> pendingNames = new List<string>();   // 重複チェック用

    public static void DrawMenu()
    {
        if (targetScript == null)
        {
            string saved = SessionState.GetString(SessionKey, "");
            if (!string.IsNullOrEmpty(saved))
            {
                targetScript = AssetDatabase.LoadAssetAtPath<MonoScript>(saved);
                if (targetScript == null) SessionState.SetString(SessionKey, "");
                else EditorApplication.delayCall += Search;
            }
        }

        EditorGUILayout.HelpBox("ScriptableObjectの型を指定して、一覧・編集・一括変更・作成・複製ができます。", MessageType.Info);

        EditorGUI.BeginChangeCheck();
        targetScript = (MonoScript)EditorGUILayout.ObjectField("SO Script", targetScript, typeof(MonoScript), false);
        if (EditorGUI.EndChangeCheck())
        {
            SessionState.SetString(SessionKey, targetScript != null ? AssetDatabase.GetAssetPath(targetScript) : "");
            EditorApplication.delayCall += Search;
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("🔄 検索 / 更新")) EditorApplication.delayCall += Search;
            if (GUILayout.Button("選択中のSOから型を取得")) EditorApplication.delayCall += PickFromSelection;
        }

        if (targetType == null)
        {
            EditorGUILayout.LabelField("ScriptableObjectのスクリプトを指定してください。", EditorStyles.miniLabel);
            return;
        }

        EditorGUILayout.LabelField($"{targetType.Name}: {found.Count}件", EditorStyles.boldLabel);

        scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.Height(220));
        for (int i = 0; i < found.Count; i++)
        {
            if (found[i] == null) continue;
            DrawRow(i);
        }
        EditorGUILayout.EndScrollView();

        int checkedCount = checks.Count(c => c);

        // 🌟 一括変更
        if (batchLabels.Length > 0)
        {
            GUILayout.Space(6);
            EditorGUILayout.LabelField("チェックした項目に一括適用", EditorStyles.boldLabel);
            batchIndex = Mathf.Clamp(batchIndex, 0, batchLabels.Length - 1);
            batchIndex = EditorGUILayout.Popup("Field", batchIndex, batchLabels);

            switch (batchTypes[batchIndex])
            {
                case SerializedPropertyType.Integer: batchNumber = EditorGUILayout.IntField("Value", Mathf.RoundToInt(batchNumber)); break;
                case SerializedPropertyType.Float: batchNumber = EditorGUILayout.FloatField("Value", batchNumber); break;
                case SerializedPropertyType.Boolean: batchBool = EditorGUILayout.Toggle("Value", batchBool); break;
                case SerializedPropertyType.String: batchString = EditorGUILayout.TextField("Value", batchString); break;
            }

            GUI.enabled = checkedCount > 0;
            if (GUILayout.Button($"✅ チェックした{checkedCount}件に適用", GUILayout.Height(24))) ApplyBatch();
            GUI.enabled = true;
        }

        // 🌟 作成・複製
        GUILayout.Space(6);
        EditorGUILayout.LabelField("新規作成 / 複製", EditorStyles.boldLabel);
        newName = EditorGUILayout.TextField("Name", newName);
        targetFolder = EditorGUILayout.TextField("Folder", targetFolder);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("➕ 新規作成", GUILayout.Height(24))) EditorApplication.delayCall += CreateNew;

            GUI.enabled = checkedCount > 0;
            if (GUILayout.Button($"📄 チェックした{checkedCount}件を複製", GUILayout.Height(24))) EditorApplication.delayCall += DuplicateChecked;
            GUI.enabled = true;
        }

        // 🌟 フィールド追加(クラスのソースに書き込む)
        GUILayout.Space(6);
        showAddField = EditorGUILayout.Foldout(showAddField, "フィールドを追加(クラスに書き込む)", true);
        if (showAddField)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                newFieldTypeIndex = EditorGUILayout.Popup(newFieldTypeIndex, fieldTypeNames, GUILayout.Width(90));
                newFieldName = EditorGUILayout.TextField(newFieldName);

                GUI.enabled = newFieldTypeIndex <= 3;   // 初期値を書けるのは int / float / bool / string だけ
                newFieldDefault = EditorGUILayout.TextField(newFieldDefault, GUILayout.Width(60));
                GUI.enabled = true;

                if (GUILayout.Button("＋", GUILayout.Width(26)) && !string.IsNullOrWhiteSpace(newFieldName))
                    EditorApplication.delayCall += QueueField;
            }
            EditorGUILayout.LabelField("左から: 型 / 名前 / 初期値", EditorStyles.miniLabel);

            for (int i = 0; i < pendingLines.Count; i++)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("＋ " + pendingLines[i], EditorStyles.miniLabel);
                    int idx = i;
                    if (GUILayout.Button("×", GUILayout.Width(22)))
                    {
                        EditorApplication.delayCall += () =>
                        {
                            if (idx < pendingLines.Count) { pendingLines.RemoveAt(idx); pendingNames.RemoveAt(idx); }
                            RepaintToolkit();
                        };
                    }
                }
            }

            GUI.enabled = pendingLines.Count > 0;
            if (GUILayout.Button($"📝 {pendingLines.Count}件をクラスに書き込む", GUILayout.Height(24)))
                EditorApplication.delayCall += WriteFields;
            GUI.enabled = true;
        }
    }

    private static void DrawRow(int i)
    {
        ScriptableObject so = found[i];

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                checks[i] = EditorGUILayout.Toggle(checks[i], GUILayout.Width(20));
                expanded[i] = EditorGUILayout.Foldout(expanded[i], so.name, true);
                if (GUILayout.Button("Ping", EditorStyles.miniButton, GUILayout.Width(45)))
                {
                    EditorGUIUtility.PingObject(so);
                }
            }

            if (expanded[i])
            {
                SerializedObject serialized = new SerializedObject(so);
                serialized.Update();
                SerializedProperty prop = serialized.GetIterator();
                if (prop.NextVisible(true))
                {
                    do
                    {
                        if (prop.name == "m_Script") continue;
                        EditorGUILayout.PropertyField(prop, true);
                    } while (prop.NextVisible(false));
                }
                serialized.ApplyModifiedProperties();
            }
        }
    }

    private static void PickFromSelection()
    {
        if (Selection.activeObject is ScriptableObject so)
        {
            targetScript = MonoScript.FromScriptableObject(so);
            SessionState.SetString(SessionKey, AssetDatabase.GetAssetPath(targetScript));
            Search();
        }
    }

    private static void Search()
    {
        found.Clear();
        checks.Clear();
        expanded.Clear();
        batchPaths.Clear();
        batchTypes.Clear();
        batchLabels = new string[0];
        batchIndex = 0;

        targetType = targetScript != null ? targetScript.GetClass() : null;
        if (targetType == null || !typeof(ScriptableObject).IsAssignableFrom(targetType))
        {
            targetType = null;
            RepaintToolkit();
            return;
        }

        foreach (string guid in AssetDatabase.FindAssets("t:" + targetType.Name))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            ScriptableObject so = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
            if (so != null && targetType.IsInstanceOfType(so)) found.Add(so);
        }

        found = found.OrderBy(s => s.name).ToList();
        checks = found.Select(_ => false).ToList();
        expanded = found.Select(_ => false).ToList();

        if (found.Count > 0)
        {
            // 既存アセットと同じフォルダを、作成先の初期値にする
            targetFolder = Path.GetDirectoryName(AssetDatabase.GetAssetPath(found[0])).Replace("\\", "/");
            BuildBatchFields(found[0]);
        }

        RepaintToolkit();
    }

    // 一括変更で選べるフィールド(int / float / bool / string のトップレベルのみ)
    private static void BuildBatchFields(ScriptableObject sample)
    {
        List<string> labels = new List<string>();
        SerializedObject so = new SerializedObject(sample);
        SerializedProperty prop = so.GetIterator();

        if (prop.NextVisible(true))
        {
            do
            {
                if (prop.name == "m_Script") continue;
                SerializedPropertyType t = prop.propertyType;
                if (t == SerializedPropertyType.Integer || t == SerializedPropertyType.Float ||
                    t == SerializedPropertyType.Boolean || t == SerializedPropertyType.String)
                {
                    batchPaths.Add(prop.propertyPath);
                    batchTypes.Add(t);
                    labels.Add(prop.displayName);
                }
            } while (prop.NextVisible(false));
        }

        batchLabels = labels.ToArray();
    }

    private static void ApplyBatch()
    {
        if (batchLabels.Length == 0) return;
        string path = batchPaths[batchIndex];
        SerializedPropertyType type = batchTypes[batchIndex];

        for (int i = 0; i < found.Count; i++)
        {
            if (!checks[i] || found[i] == null) continue;

            SerializedObject so = new SerializedObject(found[i]);
            SerializedProperty prop = so.FindProperty(path);
            if (prop == null) continue;

            switch (type)
            {
                case SerializedPropertyType.Integer: prop.intValue = Mathf.RoundToInt(batchNumber); break;
                case SerializedPropertyType.Float: prop.floatValue = batchNumber; break;
                case SerializedPropertyType.Boolean: prop.boolValue = batchBool; break;
                case SerializedPropertyType.String: prop.stringValue = batchString; break;
            }
            so.ApplyModifiedProperties();   // Undo(Ctrl+Z)にも記録される
        }

        AssetDatabase.SaveAssets();
    }

    private static void CreateNew()
    {
        if (targetType == null) return;
        if (!AssetDatabase.IsValidFolder(targetFolder)) targetFolder = "Assets";

        string path = AssetDatabase.GenerateUniqueAssetPath($"{targetFolder}/{newName}.asset");
        ScriptableObject so = ScriptableObject.CreateInstance(targetType);
        AssetDatabase.CreateAsset(so, path);
        AssetDatabase.SaveAssets();

        Search();
        EditorGUIUtility.PingObject(so);
    }

    private static void DuplicateChecked()
    {
        for (int i = 0; i < found.Count; i++)
        {
            if (!checks[i] || found[i] == null) continue;
            string src = AssetDatabase.GetAssetPath(found[i]);
            string dst = AssetDatabase.GenerateUniqueAssetPath(src);   // 「名前 1.asset」のように自動で重複回避
            AssetDatabase.CopyAsset(src, dst);
        }

        AssetDatabase.SaveAssets();
        Search();
    }

    private static void RepaintToolkit()
    {
        if (EditorWindow.HasOpenInstances<Toolkit>()) EditorWindow.GetWindow<Toolkit>().Repaint();
    }
    private static void QueueField()
    {
        if (targetType == null) return;

        string name = newFieldName.Trim();
        if (!Regex.IsMatch(name, @"^[A-Za-z_][A-Za-z0-9_]*$"))
        {
            Debug.LogWarning("[Toolkit] フィールド名には英数字とアンダースコアだけ使えます(先頭は数字不可)。");
            return;
        }

        bool exists = targetType.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) != null
                      || pendingNames.Contains(name);
        if (exists)
        {
            Debug.LogWarning($"[Toolkit] {name} は既にあります。");
            return;
        }

        pendingLines.Add(BuildFieldLine(fieldTypeNames[newFieldTypeIndex], name, newFieldDefault));
        pendingNames.Add(name);
        newFieldName = "";
        newFieldDefault = "";
        RepaintToolkit();
    }

    private static string BuildFieldLine(string type, string name, string def)
    {
        string value = "";
        def = def == null ? "" : def.Trim();

        if (def.Length > 0)
        {
            switch (type)
            {
                case "int":
                    if (int.TryParse(def, out int iv)) value = " = " + iv;
                    break;
                case "float":
                    if (float.TryParse(def.TrimEnd('f', 'F'), System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out float fv))
                        value = " = " + fv.ToString(System.Globalization.CultureInfo.InvariantCulture) + "f";
                    break;
                case "bool":
                    if (bool.TryParse(def, out bool bv)) value = " = " + (bv ? "true" : "false");
                    break;
                case "string":
                    value = " = \"" + def.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
                    break;
            }
        }
        return $"public {type} {name}{value};";
    }

    private static void WriteFields()
    {
        if (targetScript == null || targetType == null || pendingLines.Count == 0) return;

        string path = AssetDatabase.GetAssetPath(targetScript);
        string text = File.ReadAllText(path);
        string nl = text.Contains("\r\n") ? "\r\n" : "\n";

        // クラス宣言から、最初の「{」までを探す
        Match m = Regex.Match(text, @"class\s+" + Regex.Escape(targetType.Name) + @"\b[^{]*\{");
        if (!m.Success)
        {
            Debug.LogWarning($"[Toolkit] {targetType.Name} のクラス宣言が見つかりませんでした。");
            return;
        }

        // クラス宣言の行の字下げ + 4 を、宣言行の字下げにする
        int lineStart = text.LastIndexOf('\n', m.Index) + 1;
        string indent = Regex.Match(text.Substring(lineStart), @"^[ \t]*").Value + "    ";

        StringBuilder sb = new StringBuilder();
        foreach (string line in pendingLines) sb.Append(nl).Append(indent).Append(line);

        text = text.Insert(m.Index + m.Length, sb.ToString());
        File.WriteAllText(path, text, Encoding.UTF8);

        pendingLines.Clear();
        pendingNames.Clear();
        AssetDatabase.ImportAsset(path);   // ここで再コンパイルが走る
    }
}