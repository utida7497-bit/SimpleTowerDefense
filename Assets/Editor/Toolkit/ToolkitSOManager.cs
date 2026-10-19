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

    private class PendingEnum
    {
        public string name;
        public List<string> values;
    }

    private const string IdentifierPattern = @"^[A-Za-z_][A-Za-z0-9_]*$";
    private const string SessionKey = "Toolkit_SOManager_Target";
    private const string EnumSessionKey = "Toolkit_SOManager_Enum";
    private const string TabSessionKey = "Toolkit_SOManager_Tab";

    private static readonly string[] tabNames = { "🧱 クラス", "📦 アセット" };
    private static readonly string[] fieldTypeNames = { "int", "float", "bool", "string", "Vector2", "Vector3", "Color", "GameObject", "Sprite", "enum" };

    private static MonoScript targetScript;
    private static System.Type targetType;
    private static int tab = -1;   // -1 = まだ読み込んでいない

    // ---- アセット ----
    private static List<ScriptableObject> found = new List<ScriptableObject>();
    private static List<bool> checks = new List<bool>();
    private static List<bool> expanded = new List<bool>();
    private static Vector2 tableScroll;

    // 表の列
    private static List<string> columnPaths = new List<string>();
    private static List<string> columnLabels = new List<string>();
    private static List<float> columnWidths = new List<float>();
    private static string skippedColumns = "";

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

    // ---- クラス ----
    private static int newFieldTypeIndex = 0;
    private static string newFieldName = "";
    private static string newFieldDefault = "";
    private static int enumPopupIndex = 0;
    private static string[] enumTypeNames = new string[0];   // フィールドの型として選べるenum

    private static string newEnumName = "";
    private static string newEnumValues = "";

    // 書き込み待ちの変更(まとめて1回で書き込む)
    private static List<string> pendingLines = new List<string>();     // フィールドの宣言行
    private static List<string> pendingNames = new List<string>();     // 重複チェック用
    private static List<PendingEnum> pendingEnums = new List<PendingEnum>();

    // このクラスの中のenumと、その編集中の中身
    private static List<System.Type> nestedEnums = new List<System.Type>();
    private static string[] nestedEnumLabels = new string[0];
    private static int nestedEnumIndex = 0;
    private static Dictionary<string, List<EnumMember>> enumBuffers = new Dictionary<string, List<EnumMember>>();
    private static Dictionary<string, List<EnumMember>> enumOriginals = new Dictionary<string, List<EnumMember>>();

    // =====================================================================
    //  画面
    // =====================================================================

    public static void DrawMenu()
    {
        if (tab < 0) tab = SessionState.GetInt(TabSessionKey, 1);

        // 再コンパイルでstatic変数が消えるので、対象のスクリプトを復元する
        if (targetScript == null)
        {
            string saved = SessionState.GetString(SessionKey, "");
            if (!string.IsNullOrEmpty(saved))
            {
                targetScript = AssetDatabase.LoadAssetAtPath<MonoScript>(saved);
                if (targetScript == null) SessionState.SetString(SessionKey, "");
                else EditorApplication.delayCall += SetTarget;
            }
        }

        EditorGUILayout.HelpBox("ScriptableObjectの型を指定して、クラスの中身の編集と、アセットの一覧・編集ができます。", MessageType.Info);

        EditorGUI.BeginChangeCheck();
        targetScript = (MonoScript)EditorGUILayout.ObjectField("SO Script", targetScript, typeof(MonoScript), false);
        if (EditorGUI.EndChangeCheck())
        {
            SessionState.SetString(SessionKey, targetScript != null ? AssetDatabase.GetAssetPath(targetScript) : "");
            EditorApplication.delayCall += SetTarget;
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

        EditorGUILayout.LabelField($"{targetType.Name}: アセット {found.Count}件", EditorStyles.boldLabel);

        // タブの切り替えは、描画の最中に画面の構成が変わらないよう、次のタイミングで行う
        int clickedTab = GUILayout.Toolbar(tab, tabNames);
        if (clickedTab != tab)
        {
            EditorApplication.delayCall += () =>
            {
                tab = clickedTab;
                SessionState.SetInt(TabSessionKey, tab);
                RepaintToolkit();
            };
        }
        GUILayout.Space(4);

        if (tab == 0) DrawClassTab();
        else DrawAssetTab();
    }

    // =====================================================================
    //  クラスタブ
    // =====================================================================

    private static void DrawClassTab()
    {
        // 描画の途中で値が変わっても、画面の構成が崩れないよう、先に一覧を決めておく
        List<string> changedEnums = enumBuffers.Keys.Where(k => DiffEnum(k).Count > 0).ToList();

        EditorGUILayout.LabelField($"{targetType.Name} の中身", EditorStyles.boldLabel);
        DrawContents();

        GUILayout.Space(6);
        EditorGUILayout.LabelField("フィールドを追加", EditorStyles.miniBoldLabel);
        DrawAddFieldRow();

        GUILayout.Space(4);
        EditorGUILayout.LabelField("enumを追加", EditorStyles.miniBoldLabel);
        newEnumName = EditorGUILayout.TextField("Enum名", newEnumName);
        newEnumValues = EditorGUILayout.TextField("値(カンマ区切り)", newEnumValues);
        GUI.enabled = !string.IsNullOrWhiteSpace(newEnumName) && !string.IsNullOrWhiteSpace(newEnumValues);
        if (GUILayout.Button("＋ enumを変更リストに追加", GUILayout.Height(22)))
            EditorApplication.delayCall += QueueEnum;
        GUI.enabled = true;

        GUILayout.Space(4);
        EditorGUILayout.LabelField("enumを編集", EditorStyles.miniBoldLabel);
        DrawEnumEditor();

        GUILayout.Space(8);
        DrawChangeList(changedEnums);
    }

    // 🌟 今のクラスの中身(フィールドとenum)を一覧にする
    private static void DrawContents()
    {
        bool any = false;
        foreach (FieldInfo f in targetType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (!IsSerializedField(f)) continue;
            any = true;
            EditorGUILayout.LabelField($"   {TypeLabel(f.FieldType)}  {f.Name}", EditorStyles.miniLabel);
        }
        if (!any) EditorGUILayout.LabelField("   (フィールドはまだありません)", EditorStyles.miniLabel);

        foreach (System.Type e in nestedEnums)
        {
            string members = enumOriginals.ContainsKey(e.Name)
                ? string.Join(", ", enumOriginals[e.Name].Select(m => $"{m.name} = {m.value}"))
                : "";
            EditorGUILayout.LabelField($"   enum {e.Name} {{ {members} }}", EditorStyles.wordWrappedMiniLabel);
        }
    }

    private static bool IsSerializedField(FieldInfo f)
    {
        if (f.IsStatic || f.IsLiteral) return false;
        if (f.IsPublic) return !System.Attribute.IsDefined(f, typeof(System.NonSerializedAttribute));
        return System.Attribute.IsDefined(f, typeof(SerializeField));
    }

    private static string TypeLabel(System.Type t)
    {
        if (t == typeof(int)) return "int";
        if (t == typeof(float)) return "float";
        if (t == typeof(bool)) return "bool";
        if (t == typeof(string)) return "string";
        if (t.IsArray) return TypeLabel(t.GetElementType()) + "[]";
        if (t.IsGenericType)
        {
            string baseName = t.Name.Substring(0, t.Name.IndexOf('`'));
            return baseName + "<" + string.Join(", ", t.GetGenericArguments().Select(TypeLabel)) + ">";
        }
        return t.Name;
    }

    private static void DrawAddFieldRow()
    {
        string[] enumOptions = GetEnumOptions();
        bool isEnum = fieldTypeNames[newFieldTypeIndex] == "enum";

        // 1段目: 型 と 名前
        using (new EditorGUILayout.HorizontalScope())
        {
            newFieldTypeIndex = EditorGUILayout.Popup(newFieldTypeIndex, fieldTypeNames, GUILayout.Width(80));
            newFieldName = EditorGUILayout.TextField(newFieldName);
        }

        // 2段目: 初期値(enumの時は種類の選択) と ＋ボタン
        using (new EditorGUILayout.HorizontalScope())
        {
            if (isEnum)
            {
                if (enumOptions.Length == 0)
                {
                    EditorGUILayout.LabelField("(enumなし)", EditorStyles.miniLabel);
                }
                else
                {
                    enumPopupIndex = Mathf.Clamp(enumPopupIndex, 0, enumOptions.Length - 1);
                    enumPopupIndex = EditorGUILayout.Popup(enumPopupIndex, enumOptions);
                }
            }
            else
            {
                EditorGUILayout.LabelField("初期値", GUILayout.Width(40));
                GUI.enabled = newFieldTypeIndex <= 3;   // 初期値を書けるのは int / float / bool / string だけ
                newFieldDefault = EditorGUILayout.TextField(newFieldDefault);
                GUI.enabled = true;
            }

            if (GUILayout.Button("＋ 変更リストに追加", GUILayout.Width(130)) && !string.IsNullOrWhiteSpace(newFieldName))
                EditorApplication.delayCall += QueueField;
        }
    }
    // フィールドの型として選べるenum(既にあるもの + これから作るもの)
    private static string[] GetEnumOptions()
    {
        List<string> list = new List<string>(enumTypeNames);
        foreach (PendingEnum pe in pendingEnums) list.Add(pe.name);
        return list.Distinct().ToArray();
    }

    private static void DrawEnumEditor()
    {
        if (nestedEnums.Count == 0)
        {
            EditorGUILayout.LabelField("   (このクラスの中にenumはまだありません)", EditorStyles.miniLabel);
            return;
        }

        nestedEnumIndex = Mathf.Clamp(nestedEnumIndex, 0, nestedEnums.Count - 1);
        int pickedIndex = EditorGUILayout.Popup("編集するenum", nestedEnumIndex, nestedEnumLabels);
        if (pickedIndex != nestedEnumIndex)
        {
            EditorApplication.delayCall += () =>
            {
                nestedEnumIndex = pickedIndex;
                SessionState.SetString(EnumSessionKey, nestedEnums[nestedEnumIndex].Name);
                RepaintToolkit();
            };
        }

        string enumName = nestedEnums[nestedEnumIndex].Name;
        if (!enumBuffers.ContainsKey(enumName)) return;
        List<EnumMember> buffer = enumBuffers[enumName];

        EditorGUILayout.HelpBox(
            "名前を直して「まとめて書き込む」を押すと、enumの定義が書き換わります。各値の番号は固定なので、削除や名前の変更をしても、作ったアセットの値は変わりません。※enumの中のコメントは消えます。",
            MessageType.None);

        for (int i = 0; i < buffer.Count; i++)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                buffer[i].name = EditorGUILayout.TextField(buffer[i].name);
                EditorGUILayout.LabelField("= " + buffer[i].value + (buffer[i].isNew ? " 新" : ""), GUILayout.Width(60));

                int idx = i;
                if (GUILayout.Button("×", GUILayout.Width(22)))
                {
                    EditorApplication.delayCall += () =>
                    {
                        if (idx < buffer.Count) buffer.RemoveAt(idx);
                        RepaintToolkit();
                    };
                }
            }
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("＋ 値を追加")) EditorApplication.delayCall += () => AddEnumMember(enumName);
            if (GUILayout.Button("↩ 元に戻す")) EditorApplication.delayCall += () => RevertEnum(enumName);
        }
    }

    // 🌟 書き込み待ちの変更を一覧にして、まとめて書き込む
    private static void DrawChangeList(List<string> changedEnums)
    {
        int total = pendingLines.Count + pendingEnums.Count + changedEnums.Count;

        EditorGUILayout.LabelField($"変更リスト({total}件)", EditorStyles.boldLabel);
        if (total == 0) EditorGUILayout.LabelField("   (まだ変更はありません)", EditorStyles.miniLabel);

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

        for (int i = 0; i < pendingEnums.Count; i++)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField($"＋ enum {pendingEnums[i].name} {{ {string.Join(", ", pendingEnums[i].values)} }}", EditorStyles.miniLabel);
                int idx = i;
                if (GUILayout.Button("×", GUILayout.Width(22)))
                {
                    EditorApplication.delayCall += () =>
                    {
                        if (idx < pendingEnums.Count) pendingEnums.RemoveAt(idx);
                        RepaintToolkit();
                    };
                }
            }
        }

        foreach (string changedEnum in changedEnums)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField($"～ enum {changedEnum}: {string.Join(" / ", DiffEnum(changedEnum))}", EditorStyles.miniLabel);
                string capturedName = changedEnum;
                if (GUILayout.Button("↩", GUILayout.Width(22)))
                    EditorApplication.delayCall += () => RevertEnum(capturedName);
            }
        }

        GUI.enabled = total > 0;
        if (GUILayout.Button($"📝 まとめて書き込む({total}件)", GUILayout.Height(28)))
            EditorApplication.delayCall += WriteAll;
        GUI.enabled = true;
    }

    // =====================================================================
    //  アセットタブ
    // =====================================================================

    private static void DrawAssetTab()
    {
        if (found.Count == 0)
        {
            EditorGUILayout.HelpBox($"{targetType.Name} のアセットはまだありません。下の「新規作成」で作れます。", MessageType.None);
        }
        else
        {
            DrawTable();
        }

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
    }

    // 🌟 1行が1アセット、1列が1フィールドの表
    private static void DrawTable()
    {
        float height = Mathf.Clamp(50 + found.Count * 22, 80, 280);
        tableScroll = EditorGUILayout.BeginScrollView(tableScroll, GUILayout.Height(height));

        // 見出し
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Label("", GUILayout.Width(18));
            GUILayout.Label("", GUILayout.Width(14));
            GUILayout.Label("名前", EditorStyles.miniBoldLabel, GUILayout.Width(110));
            for (int c = 0; c < columnLabels.Count; c++)
                GUILayout.Label(columnLabels[c], EditorStyles.miniBoldLabel, GUILayout.Width(columnWidths[c]));
        }

        for (int i = 0; i < found.Count; i++)
        {
            if (found[i] == null) continue;
            ScriptableObject so = found[i];

            using (new EditorGUILayout.HorizontalScope())
            {
                checks[i] = EditorGUILayout.Toggle(checks[i], GUILayout.Width(18));
                expanded[i] = GUILayout.Toggle(expanded[i], GUIContent.none, EditorStyles.foldout, GUILayout.Width(14));

                if (GUILayout.Button(new GUIContent(so.name, "クリックで場所を表示"), EditorStyles.label, GUILayout.Width(110)))
                    EditorGUIUtility.PingObject(so);

                SerializedObject serialized = new SerializedObject(so);
                serialized.Update();
                for (int c = 0; c < columnPaths.Count; c++)
                {
                    SerializedProperty prop = serialized.FindProperty(columnPaths[c]);
                    if (prop == null) GUILayout.Space(columnWidths[c]);
                    else EditorGUILayout.PropertyField(prop, GUIContent.none, GUILayout.Width(columnWidths[c]));
                }
                serialized.ApplyModifiedProperties();   // Undo(Ctrl+Z)にも記録される
            }

            // ▸ を開くと、配列など表に出せない項目も含めて、全部を編集できる
            if (expanded[i])
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    SerializedObject detail = new SerializedObject(so);
                    detail.Update();
                    SerializedProperty prop = detail.GetIterator();
                    if (prop.NextVisible(true))
                    {
                        do
                        {
                            if (prop.name == "m_Script") continue;
                            EditorGUILayout.PropertyField(prop, true);
                        } while (prop.NextVisible(false));
                    }
                    detail.ApplyModifiedProperties();
                }
            }
        }

        EditorGUILayout.EndScrollView();

        if (skippedColumns.Length > 0)
            EditorGUILayout.LabelField($"表に出せない項目: {skippedColumns}(行の ▸ を開くと編集できます)", EditorStyles.wordWrappedMiniLabel);
    }

    // =====================================================================
    //  検索・読み込み
    // =====================================================================

    private static void PickFromSelection()
    {
        if (Selection.activeObject is ScriptableObject so)
        {
            targetScript = MonoScript.FromScriptableObject(so);
            SessionState.SetString(SessionKey, AssetDatabase.GetAssetPath(targetScript));
            SetTarget();
        }
    }

    // 対象のスクリプトが変わった時は、書き込み待ちの変更も捨てる
    private static void SetTarget()
    {
        pendingLines.Clear();
        pendingNames.Clear();
        pendingEnums.Clear();
        enumBuffers.Clear();
        enumOriginals.Clear();
        Search();
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
        columnPaths.Clear();
        columnLabels.Clear();
        columnWidths.Clear();
        skippedColumns = "";
        nestedEnums.Clear();
        nestedEnumLabels = new string[0];

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
            BuildColumns(found[0]);
        }

        // このクラスの中にあるenum(編集の対象)。編集中の内容は、更新しても残す
        nestedEnums = targetType.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
            .Where(n => n.IsEnum)
            .OrderBy(n => n.Name)
            .ToList();
        nestedEnumLabels = nestedEnums.Select(n => n.Name).ToArray();

        foreach (System.Type enumType in nestedEnums)
        {
            if (!enumBuffers.ContainsKey(enumType.Name)) LoadEnumBuffer(enumType);
        }

        string savedEnum = SessionState.GetString(EnumSessionKey, "");
        int enumFound = nestedEnums.FindIndex(n => n.Name == savedEnum);
        if (enumFound >= 0) nestedEnumIndex = enumFound;
        nestedEnumIndex = Mathf.Clamp(nestedEnumIndex, 0, Mathf.Max(0, nestedEnums.Count - 1));

        LoadEnumTypes();
        RepaintToolkit();
    }

    // 表の列(配列や構造体は、表に出せないので除く)
    private static void BuildColumns(ScriptableObject sample)
    {
        List<string> skipped = new List<string>();
        SerializedObject so = new SerializedObject(sample);
        SerializedProperty prop = so.GetIterator();

        if (prop.NextVisible(true))
        {
            do
            {
                if (prop.name == "m_Script") continue;
                if (prop.propertyType == SerializedPropertyType.Generic)
                {
                    skipped.Add(prop.displayName);
                    continue;
                }
                columnPaths.Add(prop.propertyPath);
                columnLabels.Add(prop.displayName);
                columnWidths.Add(ColumnWidth(prop.propertyType));
            } while (prop.NextVisible(false));
        }

        skippedColumns = string.Join(", ", skipped);
    }

    private static float ColumnWidth(SerializedPropertyType type)
    {
        switch (type)
        {
            case SerializedPropertyType.Boolean: return 40f;
            case SerializedPropertyType.Integer: return 60f;
            case SerializedPropertyType.Float: return 60f;
            case SerializedPropertyType.Enum: return 90f;
            case SerializedPropertyType.String: return 110f;
            case SerializedPropertyType.Color: return 70f;
            case SerializedPropertyType.Vector2: return 140f;
            case SerializedPropertyType.Vector3: return 200f;
            case SerializedPropertyType.Vector4: return 240f;
            case SerializedPropertyType.Rect: return 240f;
            case SerializedPropertyType.ObjectReference: return 130f;
            default: return 100f;
        }
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

    // フィールドの型として選べるenum(今のクラスの中のenum + プロジェクト内のpublicなenum)
    private static void LoadEnumTypes()
    {
        List<string> names = new List<string>();

        if (targetType != null)
        {
            foreach (System.Type nested in targetType.GetNestedTypes(BindingFlags.Public))
            {
                if (nested.IsEnum) names.Add(nested.FullName.Replace('+', '.'));
            }
        }

        foreach (Assembly assembly in System.AppDomain.CurrentDomain.GetAssemblies())
        {
            string location;
            try { location = assembly.Location.Replace('\\', '/'); }
            catch { continue; }   // 動的アセンブリは場所を持たない

            if (!location.Contains("/Library/ScriptAssemblies/")) continue;
            if (assembly.GetName().Name.Contains("Editor")) continue;

            System.Type[] types;
            try { types = assembly.GetTypes(); }
            catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray(); }

            foreach (System.Type type in types)
            {
                if (type.IsEnum && (type.IsPublic || type.IsNestedPublic))
                    names.Add(type.FullName.Replace('+', '.'));
            }
        }

        enumTypeNames = names.Distinct().OrderBy(n => n).ToArray();
    }

    // =====================================================================
    //  アセットの操作
    // =====================================================================

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

    // =====================================================================
    //  クラスの変更(変更リストに溜める)
    // =====================================================================

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
            string[] options = GetEnumOptions();
            if (options.Length == 0)
            {
                Debug.LogWarning("[Toolkit] 使えるenumがありません。「enumを追加」で作ってください。");
                return;
            }
            typeName = options[Mathf.Clamp(enumPopupIndex, 0, options.Length - 1)];
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

    private static void QueueEnum()
    {
        if (targetType == null) return;

        string name = newEnumName.Trim();
        if (!Regex.IsMatch(name, IdentifierPattern))
        {
            Debug.LogWarning("[Toolkit] enum名には英数字とアンダースコアだけ使えます。");
            return;
        }

        if (targetType.GetNestedType(name, BindingFlags.Public | BindingFlags.NonPublic) != null
            || pendingEnums.Any(p => p.name == name))
        {
            Debug.LogWarning($"[Toolkit] {name} という名前のenumは既に {targetType.Name} クラス内にあります。");
            return;
        }

        List<string> values = Regex.Split(newEnumValues, @"[,\s、]+")
            .Where(v => v.Length > 0).Distinct().ToList();
        if (values.Count == 0 || values.Any(v => !Regex.IsMatch(v, IdentifierPattern)))
        {
            Debug.LogWarning("[Toolkit] 値はカンマかスペース区切りで、英数字とアンダースコアだけ使えます。");
            return;
        }

        pendingEnums.Add(new PendingEnum { name = name, values = values });
        newEnumName = "";
        newEnumValues = "";
        RepaintToolkit();
    }

    // 今のenumの中身(名前と番号)を、コンパイル済みの型から読む
    private static void LoadEnumBuffer(System.Type enumType)
    {
        List<EnumMember> current = new List<EnumMember>();
        List<EnumMember> original = new List<EnumMember>();

        foreach (string memberName in System.Enum.GetNames(enumType))
        {
            long value = System.Convert.ToInt64(System.Enum.Parse(enumType, memberName));
            current.Add(new EnumMember { name = memberName, value = value });
            original.Add(new EnumMember { name = memberName, value = value });
        }

        enumBuffers[enumType.Name] = current;
        enumOriginals[enumType.Name] = original;
    }

    private static void RevertEnum(string enumName)
    {
        System.Type enumType = nestedEnums.FirstOrDefault(n => n.Name == enumName);
        if (enumType != null) LoadEnumBuffer(enumType);
        RepaintToolkit();
    }

    private static void AddEnumMember(string enumName)
    {
        if (!enumBuffers.ContainsKey(enumName)) return;
        List<EnumMember> buffer = enumBuffers[enumName];

        // 消した値の番号を使い回さないよう、元の最大値も見て、その次の番号にする
        long maxValue = -1;
        if (buffer.Count > 0) maxValue = System.Math.Max(maxValue, buffer.Max(x => x.value));
        if (enumOriginals.ContainsKey(enumName) && enumOriginals[enumName].Count > 0)
            maxValue = System.Math.Max(maxValue, enumOriginals[enumName].Max(x => x.value));

        long next = maxValue + 1;
        buffer.Add(new EnumMember { name = "NewValue" + next, value = next, isNew = true });
        RepaintToolkit();
    }

    // 元のenumとの違いを、短い文にして返す
    private static List<string> DiffEnum(string enumName)
    {
        List<string> diffs = new List<string>();
        if (!enumBuffers.ContainsKey(enumName) || !enumOriginals.ContainsKey(enumName)) return diffs;

        List<EnumMember> current = enumBuffers[enumName];
        foreach (EnumMember original in enumOriginals[enumName])
        {
            EnumMember match = current.FirstOrDefault(x => x.value == original.value && !x.isNew);
            if (match == null) diffs.Add($"－ {original.name}");
            else if (match.name != original.name) diffs.Add($"{original.name} → {match.name}");
        }
        foreach (EnumMember added in current.Where(x => x.isNew))
            diffs.Add($"＋ {added.name} = {added.value}");

        return diffs;
    }

    // =====================================================================
    //  書き込み(変更リストを、1回でソースに反映する)
    // =====================================================================

    private static void WriteAll()
    {
        if (targetScript == null || targetType == null) return;

        List<string> changedEnums = enumBuffers.Keys.Where(k => DiffEnum(k).Count > 0).ToList();
        if (pendingLines.Count + pendingEnums.Count + changedEnums.Count == 0) return;

        // 編集したenumの名前を確認する
        foreach (string enumName in changedEnums)
        {
            List<EnumMember> buffer = enumBuffers[enumName];
            if (buffer.Count == 0)
            {
                Debug.LogWarning($"[Toolkit] {enumName} の値が1つもありません。");
                return;
            }
            if (buffer.Any(x => !Regex.IsMatch(x.name ?? "", IdentifierPattern)))
            {
                Debug.LogWarning($"[Toolkit] {enumName} の値の名前には、英数字とアンダースコアだけ使えます(先頭は数字不可)。");
                return;
            }
            if (buffer.GroupBy(x => x.name).Any(g => g.Count() > 1))
            {
                Debug.LogWarning($"[Toolkit] {enumName} に同じ名前の値があります。");
                return;
            }
        }

        // 名前の変更・削除で無くなる値と、それを使っているコードを確認する
        List<string> removedLabels = new List<string>();
        List<string> usedIn = new List<string>();
        foreach (string enumName in changedEnums)
        {
            HashSet<string> newNames = new HashSet<string>(enumBuffers[enumName].Select(x => x.name));
            List<string> removed = enumOriginals[enumName].Select(x => x.name).Where(n => !newNames.Contains(n)).ToList();
            if (removed.Count == 0) continue;

            removedLabels.AddRange(removed.Select(n => enumName + "." + n));
            usedIn.AddRange(FindEnumUsages(enumName, removed));
        }

        if (removedLabels.Count > 0)
        {
            string message = "次の値が、名前の変更または削除で無くなります:\n" + string.Join(", ", removedLabels);
            List<string> files = usedIn.Distinct().ToList();
            if (files.Count > 0)
                message += "\n\nコードで使われているファイル(書き込み後にエラーになります):\n" + string.Join("\n", files.Take(8));

            if (!EditorUtility.DisplayDialog("確認", message, "書き込む", "キャンセル")) return;
        }

        string path = AssetDatabase.GetAssetPath(targetScript);
        string text = File.ReadAllText(path);
        string nl = text.Contains("\r\n") ? "\r\n" : "\n";

        // ① 編集したenumの中身を書き換える
        foreach (string enumName in changedEnums)
        {
            text = ReplaceEnumBody(text, enumName, enumBuffers[enumName], nl);
            if (text == null) return;
        }

        // ② 新しいenumと、新しいフィールドを、クラスの先頭に足す
        Match classMatch = Regex.Match(text, @"class\s+" + Regex.Escape(targetType.Name) + @"\b[^{]*\{");
        if (!classMatch.Success)
        {
            Debug.LogWarning($"[Toolkit] {targetType.Name} のクラス宣言が見つかりませんでした。");
            return;
        }

        int lineStart = text.LastIndexOf('\n', classMatch.Index) + 1;
        string indent = Regex.Match(text.Substring(lineStart), @"^[ \t]*").Value + "    ";

        StringBuilder sb = new StringBuilder();
        foreach (PendingEnum pending in pendingEnums)
        {
            // 番号を明示して書くので、あとから削除・名前変更をしても、他の値はずれない
            sb.Append(nl).Append(indent).Append("public enum ").Append(pending.name);
            sb.Append(nl).Append(indent).Append("{");
            for (int i = 0; i < pending.values.Count; i++)
                sb.Append(nl).Append(indent).Append("    ").Append(pending.values[i]).Append(" = ").Append(i).Append(",");
            sb.Append(nl).Append(indent).Append("}");
            sb.Append(nl);
        }
        foreach (string line in pendingLines) sb.Append(nl).Append(indent).Append(line);

        if (sb.Length > 0) text = text.Insert(classMatch.Index + classMatch.Length, sb.ToString());

        File.WriteAllText(path, text, Encoding.UTF8);

        pendingLines.Clear();
        pendingNames.Clear();
        pendingEnums.Clear();
        enumBuffers.Clear();
        enumOriginals.Clear();

        AssetDatabase.ImportAsset(path);   // ここで再コンパイルが走る
    }

    // enumの { } の中を、番号つきの形で書き直す
    private static string ReplaceEnumBody(string text, string enumName, List<EnumMember> members, string nl)
    {
        Match declaration = Regex.Match(text, @"\benum\s+" + Regex.Escape(enumName) + @"\b[^{]*\{(?<body>[^}]*)\}");
        if (!declaration.Success)
        {
            Debug.LogWarning($"[Toolkit] {enumName} の定義が見つかりませんでした。");
            return null;
        }

        int lineStart = text.LastIndexOf('\n', declaration.Index) + 1;
        string declIndent = Regex.Match(text.Substring(lineStart), @"^[ \t]*").Value;
        string memberIndent = declIndent + "    ";

        StringBuilder sb = new StringBuilder();
        sb.Append(nl);
        foreach (EnumMember member in members.OrderBy(x => x.value))
            sb.Append(memberIndent).Append(member.name).Append(" = ").Append(member.value).Append(',').Append(nl);
        sb.Append(declIndent);

        Group body = declaration.Groups["body"];
        return text.Substring(0, body.Index) + sb.ToString() + text.Substring(body.Index + body.Length);
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

    private static void RepaintToolkit()
    {
        if (EditorWindow.HasOpenInstances<Toolkit>()) EditorWindow.GetWindow<Toolkit>().Repaint();
    }
}