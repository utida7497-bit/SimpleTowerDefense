using UnityEditor;
using UnityEngine;
using System.Collections.Generic;

public class Toolkit : EditorWindow
{
    private Vector2 scrollPosition;

    // 🌟 インスペクターのように直接掴んで並び替えできるコンポーネントのリスト
    [SerializeField]
    public List<string> activeComponents = new List<string>() { "ScriptGenerator", "WindowPrintShot", "AssetLauncher" };

    [SerializeField] private List<bool> componentLocks = new List<bool>() { false, false, false };
    [SerializeField] private List<GameObject> lockedObjects = new List<GameObject>() { null, null, null };

    // 操作画面の箱を直接掴んでヌルヌル動かすためのドラッグセッションデータ
    private int draggingIndex = -1;

    // 各コンポーネントの画面上の正確な絶対座標（高さ）を記憶する辞書
    private Dictionary<int, Rect> currentComponentRects = new Dictionary<int, Rect>();

    [MenuItem("Tools/Toolkit &t")]
    public static void ShowWindow()
    {
        GetWindow<Toolkit>("Toolkit");
    }

    private void OnEnable()
    {
        Selection.selectionChanged += OnSelectionChanged;
    }

    private void OnDisable()
    {
        Selection.selectionChanged -= OnSelectionChanged;
    }

    private void OnSelectionChanged()
    {
        Repaint();
    }

    private void OnGUI()
    {
        while (componentLocks.Count < activeComponents.Count) componentLocks.Add(false);
        while (lockedObjects.Count < activeComponents.Count) lockedObjects.Add(null);

        Event e = Event.current;
        if (e.type == EventType.MouseUp)
        {
            draggingIndex = -1;
        }

        using (var scroll = new EditorGUILayout.ScrollViewScope(scrollPosition))
        {
            scrollPosition = scroll.scrollPosition;

            GUILayout.Label("🛠️ Component Console (操作画面のヘッダーを掴んで直接その場で並び替え！)", EditorStyles.boldLabel);
            GUILayout.Space(5);

            for (int i = 0; i < activeComponents.Count; i++)
            {
                DrawComponentWithDirectDrag(activeComponents[i], i, e);
            }

            GUILayout.Space(15);
            Rect addButtonRect = GUILayoutUtility.GetRect(new GUIContent("Add Component"), EditorStyles.toolbarButton);
            addButtonRect.x += (addButtonRect.width - 200f) / 2f;
            addButtonRect.width = 200f;

            if (GUI.Button(addButtonRect, "Add Component", EditorStyles.toolbarButton))
            {
                ToolkitPopup.Open(addButtonRect, (newCompName) =>
                {
                    activeComponents.Add(newCompName);
                    componentLocks.Add(false);
                    lockedObjects.Add(null);
                    Repaint();
                });
            }
            GUILayout.Space(10);
        }
    }

    private void DrawComponentWithDirectDrag(string compName, int index, Event e)
    {
        string boxTitle = compName == "WindowPrintShot" ? "📸 Window Print Shot" :
          compName == "ScriptGenerator" ? "🔨 Script Generator" :
          compName == "AssetLauncher" ? "🚀 Asset Launcher" :
          compName == "ToolkitScriptFinder" ? "🔍 Script Finder" :
          compName == "TimeScale" ? "⏱ Time Scale" :
          compName == "TextManager" ? "🔤 UI Text Manager" :
          compName == "ObjectTracker" ? "📈 Object Tracker" : "🔍 Attached Script Checker";
        GUIStyle boxStyle = new GUIStyle(EditorStyles.helpBox);
        if (draggingIndex == index)
        {
            boxStyle.normal.background = Texture2D.grayTexture;
        }

        bool requiresSwap = false;
        int swapTargetIndex = -1;

        Rect fullBoxRect = EditorGUILayout.BeginVertical(boxStyle);
        {
            Rect headerRect = GUILayoutUtility.GetRect(new GUIContent(boxTitle), EditorStyles.toolbarButton);
            GUI.Box(headerRect, "", EditorStyles.toolbarButton);

            // 🌟【大発見の解決・ドラッグ横取り完全ガード壁】
            // ボタンが配置されている「右端の150pxの領域」にマウスがいる時は、ドラッグの開始を100%強制ブロックします！
            if (e.type == EventType.MouseDown && headerRect.Contains(e.mousePosition) && e.button == 0)
            {
                float buttonZoneLeftX = headerRect.x + headerRect.width - 150f;
                if (e.mousePosition.x < buttonZoneLeftX)
                {
                    draggingIndex = index;
                    e.Use();
                }
            }

            // マウス位置準拠スワップ
            if (draggingIndex == index && e.type == EventType.MouseDrag)
            {
                float mouseY = e.mousePosition.y;

                if (index > 0 && currentComponentRects.ContainsKey(index - 1))
                {
                    Rect upperRect = currentComponentRects[index - 1];
                    float upperCenterY = upperRect.y + (upperRect.height / 2f);
                    if (mouseY < upperCenterY)
                    {
                        requiresSwap = true;
                        swapTargetIndex = index - 1;
                        e.Use();
                    }
                }

                if (index < activeComponents.Count - 1 && currentComponentRects.ContainsKey(index + 1))
                {
                    Rect lowerRect = currentComponentRects[index + 1];
                    float lowerCenterY = lowerRect.y + (lowerRect.height / 2f);
                    if (mouseY > lowerCenterY)
                    {
                        requiresSwap = true;
                        swapTargetIndex = index + 1;
                        e.Use();
                    }
                }
            }

            float validWidth = headerRect.width > 100f ? headerRect.width : (position.width - 24f);
            float btnStartX = headerRect.x + validWidth - 145f;

            Rect labelRect = new Rect(headerRect.x + 5f, headerRect.y + 2f, Mathf.Max(50f, validWidth - 155f), headerRect.height);
            GUI.Label(labelRect, $"☰  {boxTitle}", EditorStyles.miniBoldLabel);

            if (compName == "AttachedScriptChecker")
            {
                bool isLocked = ToolkitScriptChecker.IsLocked(index);
                bool nextLock = GUI.Toggle(new Rect(btnStartX, headerRect.y + 2f, 65f, 16f), isLocked, isLocked ? "🔒 Locked" : "🔓 Lock", EditorStyles.miniButton);
                if (nextLock != isLocked) ToolkitScriptChecker.SetLocked(index, nextLock);
            }

            // 🌟【一撃必殺Remove】ドラッグの胃袋から信号を完全に救出したため、ポチッと押した瞬間に100%確実に即座に消去されます！
            if (GUI.Button(new Rect(headerRect.x + validWidth - 75f, headerRect.y + 2f, 70f, 16f), "Remove", EditorStyles.miniButton))
            {
                EditorApplication.delayCall += () =>
                {
                    activeComponents.RemoveAt(index);
                    componentLocks.RemoveAt(index);
                    lockedObjects.RemoveAt(index);
                    ToolkitAssetLauncher.OnComponentRemoved(index);
                    ToolkitScriptChecker.OnComponentRemoved(index);
                    Repaint();
                };
            }

            // 各メニューの描画
            GUILayout.Space(5);
            if (compName == "WindowPrintShot") ToolkitPrintShot.DrawMenu(this);
            else if (compName == "ScriptGenerator") ToolkitScriptGenerator.DrawMenu();
            else if (compName == "AssetLauncher") ToolkitAssetLauncher.DrawMenu(index, this);
            else if (compName == "AttachedScriptChecker") ToolkitScriptChecker.DrawMenu(index, this);
            else if (compName == "TimeScale") ToolkitTimeScale.DrawMenu();
            else if (compName == "TextManager") ToolkitTextManager.DrawMenu();
            else if (compName == "ObjectTracker") ToolkitObjectTracker.DrawMenu();
            else if (compName == "ToolkitScriptFinder") ToolkitScriptFinder.DrawMenu();   // ← 追加
            GUILayout.Space(5);
        }
        EditorGUILayout.EndVertical();

        if (requiresSwap && swapTargetIndex != -1)
        {
            SwapElements(index, swapTargetIndex);
            draggingIndex = swapTargetIndex;
        }

        if (e.type == EventType.Repaint)
        {
            currentComponentRects[index] = fullBoxRect;
        }
        GUILayout.Space(5);
    }
    private void SwapElements(int a, int b)
    {
        string tempComp = activeComponents[a]; activeComponents[a] = activeComponents[b]; activeComponents[b] = tempComp;
        bool tempLock = componentLocks[a]; componentLocks[a] = componentLocks[b]; componentLocks[b] = tempLock;
        GameObject tempObj = lockedObjects[a]; lockedObjects[a] = lockedObjects[b]; lockedObjects[b] = tempObj;
        ToolkitAssetLauncher.OnComponentSwapped(a, b);
        ToolkitScriptChecker.OnComponentSwapped(a, b);
    }
}
