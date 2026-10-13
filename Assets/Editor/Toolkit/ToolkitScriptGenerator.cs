using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class ToolkitScriptGenerator
{
    private static string newScriptName = "SimpleGimmick";
    private static string targetFolder = "Assets";
    private static bool makeSingleton = false;
    private static bool makeScriptableObject = false;
    private static List<MonoScript> customRequires = new List<MonoScript>();

    // 🌟 RequireComponentに指定できる候補(順番を入れ替える場合は requireChecks と一緒に)
    private static readonly string[] requireTypes =
    {
        "Rigidbody", "Rigidbody2D", "BoxCollider", "BoxCollider2D", "CircleCollider2D",
        "SphereCollider", "CapsuleCollider", "AudioSource", "Animator", "SpriteRenderer", "CharacterController"
    };
    private static bool[] requireChecks = new bool[requireTypes.Length];
    private static bool showRequire = false;

    public static void DrawMenu()
    {
        EditorGUILayout.HelpBox(
            makeScriptableObject
                ? "名前と保存先フォルダを指定して、空のScriptableObjectを作成します。"
                : "名前と保存先フォルダを指定して、シンプルなMonoBehaviourを作成します。",
            MessageType.Info);

        newScriptName = EditorGUILayout.TextField("Script Name", newScriptName);
        newScriptName = System.Text.RegularExpressions.Regex.Replace(newScriptName, @"[^a-zA-Z0-9_]", "");

        makeScriptableObject = EditorGUILayout.Toggle("ScriptableObject", makeScriptableObject);

        // 🌟 SingletonとRequireComponentはMonoBehaviour専用なので、SOのときは触れなくする
        GUI.enabled = !makeScriptableObject;
        makeSingleton = EditorGUILayout.Toggle("Make Singleton", makeSingleton);

        int requireCount = 0;
        foreach (bool b in requireChecks) if (b) requireCount++;
        foreach (MonoScript s in customRequires) if (s != null) requireCount++;

        showRequire = EditorGUILayout.Foldout(showRequire, $"RequireComponent ({requireCount})", true);
        if (showRequire)
        {
            EditorGUI.indentLevel++;
            for (int i = 0; i < requireTypes.Length; i++)
            {
                requireChecks[i] = EditorGUILayout.ToggleLeft(requireTypes[i], requireChecks[i]);
            }

            // 🌟 自作スクリプト用の枠(ドラッグで指定)
            GUILayout.Space(4);
            EditorGUILayout.LabelField("自作スクリプト", EditorStyles.miniBoldLabel);
            for (int i = 0; i < customRequires.Count; i++)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    customRequires[i] = (MonoScript)EditorGUILayout.ObjectField(customRequires[i], typeof(MonoScript), false);
                    int idx = i;
                    if (GUILayout.Button("×", GUILayout.Width(22)))
                    {
                        EditorApplication.delayCall += () =>
                        {
                            if (idx < customRequires.Count) customRequires.RemoveAt(idx);
                            RepaintToolkit();
                        };
                    }
                }
            }
            if (GUILayout.Button("＋ 自作スクリプトを追加", EditorStyles.miniButton))
            {
                EditorApplication.delayCall += () =>
                {
                    customRequires.Add(null);
                    RepaintToolkit();
                };
            }

            EditorGUI.indentLevel--;
        }
        GUI.enabled = true;

        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField("Target Folder", targetFolder);
            if (GUILayout.Button("Select", GUILayout.Width(60)))
            {
                string startPath = AssetDatabase.IsValidFolder(GetSelectedFolderPath()) ? GetSelectedFolderPath() : "Assets";
                string absPath = EditorUtility.OpenFolderPanel("Select Target Folder", startPath, "");
                if (!string.IsNullOrEmpty(absPath))
                {
                    string relPath = ConvertToAssetsRelativePath(absPath);
                    if (relPath != null) targetFolder = relPath;
                    else Debug.LogWarning("[Toolkit] Assetsフォルダ内を選んでください。");
                }
            }
            if (GUILayout.Button("Use Selected", GUILayout.Width(90)))
            {
                string sel = GetSelectedFolderPath();
                if (AssetDatabase.IsValidFolder(sel)) targetFolder = sel;
            }
        }

        GUILayout.Space(5);
        if (GUILayout.Button($"🔨 Create {newScriptName}.cs", GUILayout.Height(26)))
        {
            GeneratePerfectSlimScript();
        }
    }

    private static string GetSelectedFolderPath()
    {
        if (Selection.activeObject == null) return "Assets";
        string path = AssetDatabase.GetAssetPath(Selection.activeObject);
        if (string.IsNullOrEmpty(path)) return "Assets";
        return AssetDatabase.IsValidFolder(path) ? path : Path.GetDirectoryName(path).Replace("\\", "/");
    }

    private static string ConvertToAssetsRelativePath(string absolutePath)
    {
        absolutePath = absolutePath.Replace("\\", "/");
        string dataPath = Application.dataPath;
        if (!absolutePath.StartsWith(dataPath)) return null;
        return "Assets" + absolutePath.Substring(dataPath.Length);
    }

    private static void RepaintToolkit()
    {
        if (EditorWindow.HasOpenInstances<Toolkit>()) EditorWindow.GetWindow<Toolkit>().Repaint();
    }

    private static void GeneratePerfectSlimScript()
    {
        if (string.IsNullOrEmpty(newScriptName)) return;
        if (!AssetDatabase.IsValidFolder(targetFolder)) targetFolder = "Assets";

        string fullPath = Path.Combine(targetFolder, $"{newScriptName}.cs");
        if (File.Exists(fullPath))
        {
            if (!EditorUtility.DisplayDialog("警告", $"既に '{newScriptName}.cs' が存在します。上書きしますか？", "上書き", "キャンセル"))
                return;
        }

        string codeTemplate;

        if (makeScriptableObject)
        {
            // 🌟 ScriptableObject用テンプレート(右クリックのCreateメニューからもアセットを作れる)
            codeTemplate =
$@"using UnityEngine;

[CreateAssetMenu(fileName = ""{newScriptName}"", menuName = ""Data/{newScriptName}"")]
public class {newScriptName} : ScriptableObject
{{

}}
";
        }
        else
        {
            // 🌟 シングルトン部分だけON/OFFで差し替え
            string singletonField = makeSingleton
                ? $"    public static {newScriptName} Instance;\n\n"
                : "";

            string singletonAwake = makeSingleton
                ? @"    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else if (Instance != this)
        {
            Destroy(gameObject);
        }
    }

"
                : "";

            // 🌟 チェックされたRequireComponentを属性として並べる(1行ずつ書けば複数指定できる)
            string requireAttrs = "";
            for (int i = 0; i < requireTypes.Length; i++)
            {
                if (requireChecks[i]) requireAttrs += $"[RequireComponent(typeof({requireTypes[i]}))]\n";
            }

            // 🌟 自作スクリプトの指定(名前空間つきの名前で書くので using は不要)
            foreach (MonoScript s in customRequires)
            {
                if (s == null) continue;
                System.Type t = s.GetClass();
                if (t == null || !typeof(Component).IsAssignableFrom(t)) continue;   // コンポーネントでないものは無視
                string typeName = t.FullName.Replace('+', '.');
                if (typeName == newScriptName) continue;                              // 自分自身は指定しない
                requireAttrs += $"[RequireComponent(typeof({typeName}))]\n";
            }

            if (requireAttrs.Length > 0) requireAttrs = "\n" + requireAttrs;

            codeTemplate =
$@"using UnityEngine;
{requireAttrs}public class {newScriptName} : MonoBehaviour
{{
{singletonField}{singletonAwake}    void Start()
    {{
        
    }}
    void Update()
    {{
        
    }}
}}
";
        }

        File.WriteAllText(fullPath, codeTemplate, System.Text.Encoding.UTF8);
        AssetDatabase.Refresh();
        Object obj = AssetDatabase.LoadAssetAtPath<Object>(fullPath);
        EditorGUIUtility.PingObject(obj);
        Selection.activeObject = obj;
        Debug.Log($"[Toolkit] スクリプトを生成しました: {fullPath}");
    }
}