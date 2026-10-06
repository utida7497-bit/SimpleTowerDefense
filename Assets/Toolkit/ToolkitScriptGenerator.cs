using UnityEditor;
using UnityEngine;
using System.IO;

public static class ToolkitScriptGenerator
{
    private static string newScriptName = "SimpleGimmick";
    private static string targetFolder = "Assets";
    private static bool makeSingleton = false;

    public static void DrawMenu()
    {
        EditorGUILayout.HelpBox("名前と保存先フォルダを指定してシンプルな空のMonoBehaviourを作成します。", MessageType.Info);

        newScriptName = EditorGUILayout.TextField("Script Name", newScriptName);
        newScriptName = System.Text.RegularExpressions.Regex.Replace(newScriptName, @"[^a-zA-Z0-9_]", "");

        makeSingleton = EditorGUILayout.Toggle("Make Singleton", makeSingleton);

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

        string codeTemplate =
$@"using UnityEngine;
public class {newScriptName} : MonoBehaviour
{{
{singletonField}{singletonAwake}    void Start()
    {{
        
    }}
    void Update()
    {{
        
    }}
}}
";

        File.WriteAllText(fullPath, codeTemplate, System.Text.Encoding.UTF8);
        AssetDatabase.Refresh();
        Object obj = AssetDatabase.LoadAssetAtPath<Object>(fullPath);
        EditorGUIUtility.PingObject(obj);
        Selection.activeObject = obj;
        Debug.Log($"[Toolkit] スクリプトを生成しました: {fullPath}");
    }
}