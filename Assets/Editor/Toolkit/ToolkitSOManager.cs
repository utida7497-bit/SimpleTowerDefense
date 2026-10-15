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
    private class EnumMember
    {
        public string name;
        public long value;
        public bool isNew;
    }

    private const string IdentifierPattern = @"^[A-Za-z_][A-Za-z0-9_]*$";
    private const string SessionKey = "Toolkit_SOManager_Target";
    private const string EnumSessionKey = "Toolkit_SOManager_Enum";

    private static MonoScript targetScript;
    private static System.Type targetType;

    private static List<ScriptableObject> found = new List<ScriptableObject>();
    private static List<bool> checks = new List<bool>();
    private static List<bool> expanded = new List<bool>();
    private static Vector2 scroll;

    // 一括変更用
    private static List<string> batchPaths = new List<string>();
    private static List<SerializedPropertyType> batchTypes = new List<SerializedPropertyType>();
    private static List<string[]> batchEnumNames = new List<string[]>();   // enum以外は null
    private static string[] batchLabels = new string[0];
    private static int batchIndex = 0;
    private static float batchNumber = 0f;
    private static bool batchBool = false;
    private static string batchString = "";
    private static int batchEnumIndex = 0;

    // 新規作成用
    private static string newName = "NewData";
    private static string targetFolder = "Assets";

    // フィールド追加用
    private static readonly string[] fieldTypeNames = { "int", "float", "bool", "string", "Vector2", "Vector3", "Color", "GameObject", "Sprite", "enum" };
    private static int newFieldTypeIndex = 0;
    private static string newFieldName = "";
    private static string newFieldDefault = "";
    private static bool showAddField = false;
    private static List<string> pendingLines = new List<string>();   // 書き込み待ちの宣言行
    private static List<string> pendingNames = new List<string>();   // 重複チェック用

    // フィールドの型として選べるenum(プロジェクト内のpublicなもの)
    private static string[] enumTypeNames = new string[0];
    private static bool enumTypesLoaded = false;
    private static int enumPopupIndex = 0;

    // enumの作成・編集用(今のSOクラスの中にあるenum)
    private static string newEnumName = "";
    private static string newEnumValues = "";
    private static List<System.Type> nestedEnums = new List<System.Type>();
    private static string[] nestedEnumLabels = new string[0];
    private static int nestedEnumIndex = 0;
    private static List<EnumMember> enumMembers = new List<EnumMember>();
    private static List<string> enumOriginalNames = new List<string>();
    private static string loadedEnumName = null;
    private static bool enumLoadQueued = false;

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
                case SerializedPropertyType.Enum:
                    string[] names = batchEnumNames[batchIndex];
                    batchEnumIndex = Mathf.Clamp(batchEnumIndex, 0, names.Length - 1);
                    batchEnumIndex = EditorGUILayout.Popup("Value", batchEnumIndex, names);
                    break;
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

        // 🌟 フィールド追加・enum(クラスのソースに書き込む)
        GUILayout.Space(6);
        showAddField = EditorGUILayout.Foldout(showAddField, "フィールドを追加 / enumを編集(クラスに書き込む)", true);
        if (showAddField)
        {
            if (!enumTypesLoaded) LoadEnumTypes();

            using (new EditorGUILayout.HorizontalScope())
            {
                bool isEnum = fieldTypeNames[newFieldTypeIndex] == "enum";

                newFieldTypeIndex = EditorGUILayout.Popup(newFieldTypeIndex, fieldTypeNames, GUILayout.Width(70));
                newFieldName = EditorGUILayout.TextField(newFieldName);

                if (isEnum)
                {
                    if (enumTypeNames.Length == 0)
                    {
                        EditorGUILayout.LabelField("(enumなし)", EditorStyles.miniLabel, GUILayout.Width(110));
                    }
                    else
                    {
                        enumPopupIndex = Mathf.Clamp(enumPopupIndex, 0, enumTypeNames.Length - 1);
                        enumPopupIndex = EditorGUILayout.Popup(enumPopupIndex, enumTypeNames, GUILayout.Width(110));
                    }
                }
                else
                {
                    GUI.enabled = newFieldTypeIndex <= 3;   // 初期値を書けるのは int / float / bool / string だけ
                    newFieldDefault = EditorGUILayout.TextField(newFieldDefault, GUILayout.Width(40));
                    GUI.enabled = true;
                }

                if (GUILayout.Button("＋", GUILayout.Width(24)) && !string.IsNullOrWhiteSpace(newFieldName))
                    EditorApplication.delayCall += QueueField;
            }
            EditorGUILayout.LabelField("左から: 型 / 名前 / 初期値(enumは種類を選択)", EditorStyles.miniLabel);

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

            DrawEnumSection();
        }
    }

    // 🌟 今のSOクラスの中のenumを、作る・編集する
    private static void DrawEnumSection()
    {
        GUILayout.Space(6);
        EditorGUILayout.LabelField($"{targetType.Name} の中のenum", EditorStyles.miniBoldLabel);

        // ---- 新しいenumを作る(SOのスクリプトの中に書き込む) ----
        newEnumName = EditorGUILayout.TextField("Enum名", newEnumName);
        newEnumValues = EditorGUILayout.TextField("値(カンマ区切り)", newEnumValues);

        GUI.enabled = !string.IsNullOrWhiteSpace(newEnumName) && !string.IsNullOrWhiteSpace(newEnumValues);
        if (GUILayout.Button("🧾 enumを作成", GUILayout.Height(22)))
            EditorApplication.delayCall += CreateEnum;
        GUI.enabled = true;

        // ---- 作ったenumを編集する ----
        if (nestedEnums.Count == 0)
        {
            EditorGUILayout.LabelField("(このクラスの中にenumはまだありません)", EditorStyles.miniLabel);
            return;
        }

        GUILayout.Space(4);
        nestedEnumIndex = Mathf.Clamp(nestedEnumIndex, 0, nestedEnums.Count - 1);
        nestedEnumIndex = EditorGUILayout.Popup("編集するenum", nestedEnumIndex, nestedEnumLabels);

        // 選んだenumが変わったら、中身を読み込み直す
        if (loadedEnumName != nestedEnums[nestedEnumIndex].Name && !enumLoadQueued)
        {
            enumLoadQueued = true;
            EditorApplication.delayCall += LoadEnumMembers;
        }

        EditorGUILayout.HelpBox(
            "名前を直して「書き込む」を押すと、enumの定義が書き換わります。各値の番号は固定なので、削除や名前の変更をしても、作ったアセットの値は変わりません。※enumの中のコメントは消えます。",
            MessageType.None);

        for (int i = 0; i < enumMembers.Count; i++)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                enumMembers[i].name = EditorGUILayout.TextField(enumMembers[i].name);
                EditorGUILayout.LabelField("= " + enumMembers[i].value + (enumMembers[i].isNew ? " 新" : ""), GUILayout.Width(60));

                int idx = i;
                if (GUILayout.Button("×", GUILayout.Width(22)))
                {
                    EditorApplication.delayCall += () =>
                    {
                        if (idx < enumMembers.Count) enumMembers.RemoveAt(idx);
                        RepaintToolkit();
                    };
                }
            }
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("＋ 値を追加")) EditorApplication.delayCall += AddEnumMember;
            if (GUILayout.Button("↩ 元に戻す")) EditorApplication.delayCall += LoadEnumMembers;
        }

        if (GUILayout.Button("📝 enumに書き込む", GUILayout.Height(24)))
            EditorApplication.delayCall += WriteEnumMembers;
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
        batchEnumNames.Clear();
        batchTypes.Clear();
        batchLabels = new string[0];
        batchIndex = 0;

        nestedEnums.Clear();
        nestedEnumLabels = new string[0];
        enumMembers.Clear();
        enumOriginalNames.Clear();
        loadedEnumName = null;
        enumLoadQueued = false;

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

        // このクラスの中にあるenumを集める(編集の対象)
        nestedEnums = targetType.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
            .Where(n => n.IsEnum)
            .OrderBy(n => n.Name)
            .ToList();
        nestedEnumLabels = nestedEnums.Select(n => n.Name).ToArray();

        string savedEnum = SessionState.GetString(EnumSessionKey, "");
        int enumFound = nestedEnums.FindIndex(n => n.Name == savedEnum);
        nestedEnumIndex = enumFound >= 0 ? enumFound : 0;

        RepaintToolkit();
    }

    // 一括変更で選べるフィールド(int / float / bool / string / enum のトップレベルのみ)
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
                    t == SerializedPropertyType.Boolean || t == SerializedPropertyType.String ||
                    t == SerializedPropertyType.Enum)
                {
                    batchPaths.Add(prop.propertyPath);
                    batchTypes.Add(t);
                    batchEnumNames.Add(t == SerializedPropertyType.Enum ? prop.enumDisplayNames : null);
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
                case SerializedPropertyType.Enum: prop.enumValueIndex = batchEnumIndex; break;
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

    // ---------------- フィールド追加 ----------------

    private static void QueueField()
    {
        if (targetType == null) return;

        string name = newFieldName.Trim();
        if (!Regex.IsMatch(name, IdentifierPattern))
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

        string typeName = fieldTypeNames[newFieldTypeIndex];
        if (typeName == "enum")
        {
            if (enumTypeNames.Length == 0)
            {
                Debug.LogWarning("[Toolkit] 使えるenumがありません。「enumを作成」で作ってください。");
                return;
            }
            typeName = enumTypeNames[Mathf.Clamp(enumPopupIndex, 0, enumTypeNames.Length - 1)];
        }

        pendingLines.Add(BuildFieldLine(typeName, name, newFieldDefault));
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

    // フィールドの型として選べるenum(プロジェクト内のpublicなもの。クラスの中のenumも含む)
    private static void LoadEnumTypes()
    {
        enumTypesLoaded = true;
        enumTypeNames = System.AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => a.GetName().Name == "Assembly-CSharp")
            .SelectMany(a =>
            {
                try { return a.GetTypes(); }
                catch (ReflectionTypeLoadException e) { return e.Types.Where(t => t != null).ToArray(); }
            })
            .Where(t => t.IsEnum && (t.IsPublic || t.IsNestedPublic))
            .Select(t => t.FullName.Replace('+', '.'))
            .OrderBy(n => n)
            .ToArray();
    }

    // ---------------- enumの作成・編集 ----------------

    // 🌟 SOのスクリプトの中に、新しいenumを書き込む(各値に番号を付けて書く)
    private static void CreateEnum()
    {
        if (targetScript == null || targetType == null) return;

        string name = newEnumName.Trim();
        if (!Regex.IsMatch(name, IdentifierPattern))
        {
            Debug.LogWarning("[Toolkit] enum名には英数字とアンダースコアだけ使えます。");
            return;
        }

        // 重複チェック(同じ名前のenumが、このクラスの中にないか)
        if (targetType.GetNestedType(name, BindingFlags.Public | BindingFlags.NonPublic) != null)
        {
            Debug.LogWarning($"[Toolkit] {name} という名前のenumは既に {targetType.Name} クラス内に存在します。");
            return;
        }

        List<string> values = Regex.Split(newEnumValues, @"[,\s、]+")
            .Where(v => v.Length > 0).Distinct().ToList();
        if (values.Count == 0 || values.Any(v => !Regex.IsMatch(v, IdentifierPattern)))
        {
            Debug.LogWarning("[Toolkit] 値はカンマかスペース区切りで、英数字とアンダースコアだけ使えます。");
            return;
        }

        string path = AssetDatabase.GetAssetPath(targetScript);
        string text = File.ReadAllText(path);
        string nl = text.Contains("\r\n") ? "\r\n" : "\n";

        // クラス宣言から、最初の「{」までを探す (WriteFieldsと同じ仕組み)
        Match m = Regex.Match(text, @"class\s+" + Regex.Escape(targetType.Name) + @"\b[^{]*\{");
        if (!m.Success)
        {
            Debug.LogWarning($"[Toolkit] {targetType.Name} のクラス宣言が見つかりませんでした。");
            return;
        }

        int lineStart = text.LastIndexOf('\n', m.Index) + 1;
        string indent = Regex.Match(text.Substring(lineStart), @"^[ \t]*").Value + "    ";

        // enumの文字列を組み立て(番号を明示して書くので、あとから削除・名前変更をしても他の値はずれない)
        StringBuilder sb = new StringBuilder();
        sb.Append(nl).Append(indent).Append("public enum ").Append(name);
        sb.Append(nl).Append(indent).Append("{");
        for (int i = 0; i < values.Count; i++)
        {
            sb.Append(nl).Append(indent).Append("    ").Append(values[i]).Append(" = ").Append(i).Append(",");
        }
        sb.Append(nl).Append(indent).Append("}");
        sb.Append(nl);   // 改行を挟んで既存のフィールドと離す

        // クラス宣言の「{」の直後に挿入
        text = text.Insert(m.Index + m.Length, sb.ToString());
        File.WriteAllText(path, text, Encoding.UTF8);

        SessionState.SetString(EnumSessionKey, name);   // 再コンパイル後、作ったenumが選ばれた状態にする
        newEnumName = "";
        newEnumValues = "";
        AssetDatabase.ImportAsset(path);   // 再コンパイルを実行
    }

    // 今のenumの中身(名前と番号)を、コンパイル済みの型から読む
    private static void LoadEnumMembers()
    {
        enumLoadQueued = false;
        enumMembers.Clear();
        enumOriginalNames.Clear();

        if (nestedEnums.Count == 0)
        {
            loadedEnumName = null;
            return;
        }

        nestedEnumIndex = Mathf.Clamp(nestedEnumIndex, 0, nestedEnums.Count - 1);
        System.Type enumType = nestedEnums[nestedEnumIndex];

        foreach (string memberName in System.Enum.GetNames(enumType))
        {
            long value = System.Convert.ToInt64(System.Enum.Parse(enumType, memberName));
            enumMembers.Add(new EnumMember { name = memberName, value = value });
            enumOriginalNames.Add(memberName);
        }

        loadedEnumName = enumType.Name;
        SessionState.SetString(EnumSessionKey, enumType.Name);
        RepaintToolkit();
    }

    private static void AddEnumMember()
    {
        long next = enumMembers.Count > 0 ? enumMembers.Max(x => x.value) + 1 : 0;
        enumMembers.Add(new EnumMember { name = "NewValue" + next, value = next, isNew = true });
        RepaintToolkit();
    }

    // 🌟 enumの定義を、番号つきで書き換える
    private static void WriteEnumMembers()
    {
        if (targetScript == null || nestedEnums.Count == 0) return;
        System.Type enumType = nestedEnums[Mathf.Clamp(nestedEnumIndex, 0, nestedEnums.Count - 1)];

        if (enumMembers.Count == 0)
        {
            Debug.LogWarning("[Toolkit] 値が1つもありません。");
            return;
        }
        if (enumMembers.Any(x => !Regex.IsMatch(x.name ?? "", IdentifierPattern)))
        {
            Debug.LogWarning("[Toolkit] 値の名前には、英数字とアンダースコアだけ使えます(先頭は数字不可)。");
            return;
        }
        if (enumMembers.GroupBy(x => x.name).Any(g => g.Count() > 1))
        {
            Debug.LogWarning("[Toolkit] 同じ名前の値があります。");
            return;
        }

        // enumはSOのスクリプトの中にあるので、そのファイルだけを見ればよい
        string path = AssetDatabase.GetAssetPath(targetScript);
        string text = File.ReadAllText(path);

        Match declaration = Regex.Match(text, @"\benum\s+" + Regex.Escape(enumType.Name) + @"\b[^{]*\{(?<body>[^}]*)\}");
        if (!declaration.Success)
        {
            Debug.LogWarning($"[Toolkit] {enumType.Name} の定義が {Path.GetFileName(path)} の中に見つかりませんでした。");
            return;
        }

        // 名前の変更・削除で無くなる値と、それを使っているコードを確認する
        HashSet<string> newNames = new HashSet<string>(enumMembers.Select(x => x.name));
        List<string> removed = enumOriginalNames.Where(n => !newNames.Contains(n)).ToList();
        if (removed.Count > 0)
        {
            List<string> usedIn = FindEnumUsages(enumType.Name, removed);
            string message = "次の値が、名前の変更または削除で無くなります:\n" + string.Join(", ", removed);
            if (usedIn.Count > 0)
                message += "\n\nコードで使われているファイル(書き込み後にエラーになります):\n" + string.Join("\n", usedIn.Take(8));

            if (!EditorUtility.DisplayDialog("確認", message, "書き込む", "キャンセル")) return;
        }

        string nl = text.Contains("\r\n") ? "\r\n" : "\n";
        int lineStart = text.LastIndexOf('\n', declaration.Index) + 1;
        string declIndent = Regex.Match(text.Substring(lineStart), @"^[ \t]*").Value;
        string memberIndent = declIndent + "    ";

        StringBuilder sb = new StringBuilder();
        sb.Append(nl);
        foreach (EnumMember member in enumMembers.OrderBy(x => x.value))
            sb.Append(memberIndent).Append(member.name).Append(" = ").Append(member.value).Append(',').Append(nl);
        sb.Append(declIndent);

        Group body = declaration.Groups["body"];
        text = text.Substring(0, body.Index) + sb.ToString() + text.Substring(body.Index + body.Length);
        File.WriteAllText(path, text, Encoding.UTF8);

        SessionState.SetString(EnumSessionKey, enumType.Name);
        AssetDatabase.ImportAsset(path);   // ここで再コンパイルが走る
    }

    // 消える名前を、コードのどこで使っているかを探す(例: TowerType.Arrow)
    private static List<string> FindEnumUsages(string enumName, List<string> oldNames)
    {
        string alternatives = string.Join("|", oldNames.Select(Regex.Escape));
        Regex usage = new Regex(@"\b" + Regex.Escape(enumName) + @"\s*\.\s*(" + alternatives + @")\b");

        List<string> result = new List<string>();
        foreach (string guid in AssetDatabase.FindAssets("t:MonoScript", new[] { "Assets" }))
        {
            string candidate = AssetDatabase.GUIDToAssetPath(guid);
            if (!candidate.EndsWith(".cs")) continue;
            if (usage.IsMatch(File.ReadAllText(candidate))) result.Add(candidate);
        }
        return result;
    }
}