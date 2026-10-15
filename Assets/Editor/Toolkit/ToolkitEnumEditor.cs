using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

// SO Managerの中から呼ばれる、enumの作成・編集用の道具
public static class ToolkitEnumEditor
{
    private class Member
    {
        public string name;
        public long value;
        public bool isNew;
    }

    private const string SessionKey = "Toolkit_EnumEditor_Selected";
    private static readonly Regex IdentifierRegex = new Regex(@"^[A-Za-z_][A-Za-z0-9_]*$");

    private static List<System.Type> enumTypes = new List<System.Type>();
    private static string[] enumLabels = new string[0];
    private static bool typesLoaded = false;
    private static int selected = 0;

    private static List<Member> members = new List<Member>();
    private static List<string> originalNames = new List<string>();
    private static string loadedTypeName = null;
    private static bool loadQueued = false;

    private static string newEnumName = "";
    private static string newEnumValues = "";

    public static void DrawMenu(MonoScript folderRef)
    {
        if (!typesLoaded) LoadTypes();

        GUILayout.Space(4);
        EditorGUILayout.LabelField("Enumを編集する", EditorStyles.miniBoldLabel);

        if (enumTypes.Count == 0)
        {
            EditorGUILayout.LabelField("(編集できるenumがありません。下で作成してください)", EditorStyles.miniLabel);
        }
        else
        {
            selected = Mathf.Clamp(selected, 0, enumTypes.Count - 1);
            selected = EditorGUILayout.Popup("Enum", selected, enumLabels);

            // 選んだenumが変わったら、中身を読み込み直す
            if (loadedTypeName != enumTypes[selected].FullName && !loadQueued)
            {
                loadQueued = true;
                EditorApplication.delayCall += LoadMembers;
            }

            EditorGUILayout.HelpBox(
                "名前を直して「書き込む」を押すと、enumの定義が書き換わります。各値の番号は固定なので、削除や名前の変更をしても、作ったアセットの値は変わりません。※enumの中のコメントは消えます。",
                MessageType.None);

            for (int i = 0; i < members.Count; i++)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    members[i].name = EditorGUILayout.TextField(members[i].name);
                    EditorGUILayout.LabelField("= " + members[i].value + (members[i].isNew ? " 新" : ""), GUILayout.Width(60));

                    int idx = i;
                    if (GUILayout.Button("×", GUILayout.Width(22)))
                    {
                        EditorApplication.delayCall += () =>
                        {
                            if (idx < members.Count) members.RemoveAt(idx);
                            Repaint();
                        };
                    }
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("＋ 値を追加")) EditorApplication.delayCall += AddMember;
                if (GUILayout.Button("↩ 元に戻す")) EditorApplication.delayCall += LoadMembers;
            }

            if (GUILayout.Button("📝 enumに書き込む", GUILayout.Height(24)))
                EditorApplication.delayCall += WriteMembers;
        }

        GUILayout.Space(4);
        EditorGUILayout.LabelField("新しいenumを作る(SOと同じフォルダに保存)", EditorStyles.miniBoldLabel);
        newEnumName = EditorGUILayout.TextField("Enum名", newEnumName);
        newEnumValues = EditorGUILayout.TextField("値(カンマ区切り)", newEnumValues);

        GUI.enabled = folderRef != null
                      && !string.IsNullOrWhiteSpace(newEnumName)
                      && !string.IsNullOrWhiteSpace(newEnumValues);
        if (GUILayout.Button("🧾 enumを作成", GUILayout.Height(22)))
            EditorApplication.delayCall += () => CreateEnum(folderRef);
        GUI.enabled = true;
    }

    // 🌟 プロジェクト内のpublicなenumを集める
    private static void LoadTypes()
    {
        typesLoaded = true;
        enumTypes = System.AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => a.GetName().Name == "Assembly-CSharp")
            .SelectMany(a =>
            {
                try { return a.GetTypes(); }
                catch (ReflectionTypeLoadException e) { return e.Types.Where(t => t != null).ToArray(); }
            })
            .Where(t => t.IsEnum && (t.IsPublic || t.IsNestedPublic))
            .OrderBy(t => t.FullName)
            .ToList();

        enumLabels = enumTypes.Select(t => t.FullName.Replace('+', '.')).ToArray();

        string saved = SessionState.GetString(SessionKey, "");
        int found = enumTypes.FindIndex(t => t.FullName == saved);
        selected = found >= 0 ? found : 0;
    }

    // 🌟 今のenumの中身(名前と番号)を、コンパイル済みの型から読む
    private static void LoadMembers()
    {
        loadQueued = false;
        members.Clear();
        originalNames.Clear();

        if (enumTypes.Count == 0)
        {
            loadedTypeName = null;
            return;
        }

        selected = Mathf.Clamp(selected, 0, enumTypes.Count - 1);
        System.Type type = enumTypes[selected];

        foreach (string memberName in System.Enum.GetNames(type))
        {
            long value = System.Convert.ToInt64(System.Enum.Parse(type, memberName));
            members.Add(new Member { name = memberName, value = value });
            originalNames.Add(memberName);
        }

        loadedTypeName = type.FullName;
        SessionState.SetString(SessionKey, type.FullName);
        Repaint();
    }

    private static void AddMember()
    {
        long next = members.Count > 0 ? members.Max(x => x.value) + 1 : 0;
        members.Add(new Member { name = "NewValue" + next, value = next, isNew = true });
        Repaint();
    }

    // 🌟 enumの定義を、番号つきで書き換える
    private static void WriteMembers()
    {
        if (enumTypes.Count == 0) return;
        System.Type type = enumTypes[Mathf.Clamp(selected, 0, enumTypes.Count - 1)];

        if (members.Count == 0)
        {
            Debug.LogWarning("[Toolkit] 値が1つもありません。");
            return;
        }
        if (members.Any(x => !IdentifierRegex.IsMatch(x.name ?? "")))
        {
            Debug.LogWarning("[Toolkit] 値の名前には、英数字とアンダースコアだけ使えます(先頭は数字不可)。");
            return;
        }
        if (members.GroupBy(x => x.name).Any(g => g.Count() > 1))
        {
            Debug.LogWarning("[Toolkit] 同じ名前の値があります。");
            return;
        }

        // enumが書かれているファイルを探す
        Regex declaration = new Regex(@"\benum\s+" + Regex.Escape(type.Name) + @"\b[^{]*\{(?<body>[^}]*)\}");
        string path = null;
        string text = null;
        Match found = null;

        foreach (string guid in AssetDatabase.FindAssets("t:MonoScript", new[] { "Assets" }))
        {
            string candidate = AssetDatabase.GUIDToAssetPath(guid);
            if (!candidate.EndsWith(".cs")) continue;

            string source = File.ReadAllText(candidate);
            Match hit = declaration.Match(source);
            if (hit.Success) { path = candidate; text = source; found = hit; break; }
        }

        if (found == null)
        {
            Debug.LogWarning($"[Toolkit] {type.Name} の定義が見つかりませんでした。");
            return;
        }

        // 名前の変更・削除で無くなる値と、それを使っているコードを確認する
        HashSet<string> newNames = new HashSet<string>(members.Select(x => x.name));
        List<string> removed = originalNames.Where(n => !newNames.Contains(n)).ToList();
        if (removed.Count > 0)
        {
            List<string> usedIn = FindUsages(type.Name, removed, path);
            string message = "次の値が、名前の変更または削除で無くなります:\n" + string.Join(", ", removed);
            if (usedIn.Count > 0)
                message += "\n\nコードで使われているファイル(書き込み後にエラーになります):\n" + string.Join("\n", usedIn.Take(8));

            if (!EditorUtility.DisplayDialog("確認", message, "書き込む", "キャンセル")) return;
        }

        string nl = text.Contains("\r\n") ? "\r\n" : "\n";
        int lineStart = text.LastIndexOf('\n', found.Index) + 1;
        string declIndent = Regex.Match(text.Substring(lineStart), @"^[ \t]*").Value;
        string memberIndent = declIndent + "    ";

        StringBuilder sb = new StringBuilder();
        sb.Append(nl);
        foreach (Member mem in members.OrderBy(x => x.value))
            sb.Append(memberIndent).Append(mem.name).Append(" = ").Append(mem.value).Append(',').Append(nl);
        sb.Append(declIndent);

        Group body = found.Groups["body"];
        text = text.Substring(0, body.Index) + sb.ToString() + text.Substring(body.Index + body.Length);
        File.WriteAllText(path, text, Encoding.UTF8);

        AssetDatabase.ImportAsset(path);   // ここで再コンパイルが走る
    }

    private static List<string> FindUsages(string typeName, List<string> oldNames, string skipPath)
    {
        string alternatives = string.Join("|", oldNames.Select(Regex.Escape));
        Regex usage = new Regex(@"\b" + Regex.Escape(typeName) + @"\s*\.\s*(" + alternatives + @")\b");

        List<string> result = new List<string>();
        foreach (string guid in AssetDatabase.FindAssets("t:MonoScript", new[] { "Assets" }))
        {
            string candidate = AssetDatabase.GUIDToAssetPath(guid);
            if (candidate == skipPath || !candidate.EndsWith(".cs")) continue;
            if (usage.IsMatch(File.ReadAllText(candidate))) result.Add(candidate);
        }
        return result;
    }

    private static void CreateEnum(MonoScript folderRef)
    {
        if (folderRef == null) return;

        string name = newEnumName.Trim();
        if (!IdentifierRegex.IsMatch(name))
        {
            Debug.LogWarning("[Toolkit] enum名には英数字とアンダースコアだけ使えます。");
            return;
        }
        if (enumTypes.Any(t => t.Name == name))
        {
            Debug.LogWarning($"[Toolkit] {name} という名前のenumは既にあります。");
            return;
        }

        List<string> values = Regex.Split(newEnumValues, @"[,\s、]+")
            .Where(v => v.Length > 0).Distinct().ToList();
        if (values.Count == 0 || values.Any(v => !IdentifierRegex.IsMatch(v)))
        {
            Debug.LogWarning("[Toolkit] 値はカンマかスペース区切りで、英数字とアンダースコアだけ使えます。");
            return;
        }

        string folder = Path.GetDirectoryName(AssetDatabase.GetAssetPath(folderRef)).Replace("\\", "/");
        string path = folder + "/" + name + ".cs";
        if (File.Exists(path))
        {
            Debug.LogWarning($"[Toolkit] {path} は既に存在します。");
            return;
        }

        StringBuilder sb = new StringBuilder();
        sb.Append("public enum ").Append(name).Append('\n').Append("{\n");
        for (int i = 0; i < values.Count; i++)
            sb.Append("    ").Append(values[i]).Append(" = ").Append(i).Append(",\n");
        sb.Append("}\n");

        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);

        SessionState.SetString(SessionKey, name);   // 再コンパイル後、作ったenumが選ばれた状態にする
        newEnumName = "";
        newEnumValues = "";
        AssetDatabase.ImportAsset(path);
    }

    private static void Repaint()
    {
        if (EditorWindow.HasOpenInstances<Toolkit>()) EditorWindow.GetWindow<Toolkit>().Repaint();
    }
}