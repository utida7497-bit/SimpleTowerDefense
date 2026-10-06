using UnityEditor;
using UnityEngine;
using System.Reflection;
using System.Collections.Generic;

public static class ToolkitScriptChecker
{
    // 各コンポーネントごとのロック状態とターゲットオブジェクトを管理するセーブデータ
    private static List<bool> checkerLocks = new List<bool>();
    private static List<GameObject> lockedObjects = new List<GameObject>();

    // スクリプトチェッカーのトグル（折りたたみ）開閉状態を記憶する辞書
    private static Dictionary<string, bool> foldoutStates = new Dictionary<string, bool>();

    // 🌟 Toolkit.csの大元画面から呼ばれる、描画コントロール処理
    public static void DrawMenu(int compIndex, EditorWindow window)
    {
        // データの器が足りない場合は自動で安全に増設
        while (checkerLocks.Count <= compIndex) checkerLocks.Add(false);
        while (lockedObjects.Count <= compIndex) lockedObjects.Add(null);

        // 🔒 このチェッカーがロック状態なら固定されたオブジェクトを、そうでなければ現在選択中のオブジェクトを狙い撃ち
        bool isLocked = checkerLocks[compIndex];
        GameObject activeObj = isLocked ? lockedObjects[compIndex] : Selection.activeGameObject;

        if (activeObj == null)
        {
            EditorGUILayout.HelpBox("オブジェクトを選択するか、右上のLockでピン止めして見張ることができます。", MessageType.None);
            return;
        }

        GUILayout.Label($"🎯 監視対象: {activeObj.name}", EditorStyles.boldLabel);
        Component[] components = activeObj.GetComponents<Component>();
        bool hasScript = false;
        GUIStyle richHeaderStyle = new GUIStyle(EditorStyles.label) { richText = true, fontStyle = FontStyle.Bold, fontSize = 12 };

        foreach (var comp in components)
        {
            if (comp == null || !(comp is MonoBehaviour script)) continue;
            hasScript = true; System.Type type = script.GetType();
            string foldoutKey = activeObj.GetInstanceID() + "_" + type.Name + "_" + compIndex;

            // 🌟【全展開】始めて表示されたスクリプトは、手数をゼロにするため最初から「開いた状態」で初期化
            if (!foldoutStates.ContainsKey(foldoutKey)) foldoutStates[foldoutKey] = true;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    foldoutStates[foldoutKey] = EditorGUILayout.Foldout(foldoutStates[foldoutKey], $"📄 {type.Name}", true, EditorStyles.foldoutHeader);
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("Ping", EditorStyles.miniButton, GUILayout.Width(45)))
                    {
                        var monoScript = MonoScript.FromMonoBehaviour(script);
                        if (monoScript != null) EditorGUIUtility.PingObject(monoScript);
                    }
                }

                if (foldoutStates[foldoutKey])
                {
                    GUILayout.Space(4);
                    SerializedObject serializedScript = new SerializedObject(script);
                    serializedScript.Update();
                    SerializedProperty prop = serializedScript.GetIterator();

                    if (prop.NextVisible(true))
                    {
                        do
                        {
                            if (prop.name == "m_Script") continue;
                            FieldInfo fieldInfo = type.GetField(prop.name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                            if (fieldInfo != null)
                            {
                                HeaderAttribute headerAttr = System.Attribute.GetCustomAttribute(fieldInfo, typeof(HeaderAttribute)) as HeaderAttribute;
                                if (headerAttr != null && (headerAttr.header.Contains("<color=") || headerAttr.header.Contains("</color>")))
                                {
                                    // 🌟【文字化け完全消滅】
                                    // 1. 吸い出した色タグ付きテキストを、Toolkit側で本物のリッチテキストとして上に綺麗に描画
                                    GUILayout.Space(6);
                                    EditorGUILayout.LabelField(headerAttr.header, richHeaderStyle);

                                    // 2. 下に変数の型に応じた純粋な入力ボックスだけを配置し、生タグ二重描画を100%物理的にシャットアウト
                                    using (new EditorGUILayout.HorizontalScope())
                                    {
                                        GUILayout.Label(prop.displayName, GUILayout.Width(EditorGUIUtility.labelWidth));
                                        if (prop.propertyType == SerializedPropertyType.Integer) prop.intValue = EditorGUILayout.IntField(prop.intValue);
                                        else if (prop.propertyType == SerializedPropertyType.Float) prop.floatValue = EditorGUILayout.FloatField(prop.floatValue);
                                        else if (prop.propertyType == SerializedPropertyType.Boolean) prop.boolValue = EditorGUILayout.Toggle(prop.boolValue);
                                        else if (prop.propertyType == SerializedPropertyType.String) prop.stringValue = EditorGUILayout.TextField(prop.stringValue);
                                        else EditorGUILayout.PropertyField(prop, GUIContent.none, true);
                                    }
                                    continue;
                                }
                            }
                            EditorGUILayout.PropertyField(prop, true);
                        } while (prop.NextVisible(false));
                    }
                    serializedScript.ApplyModifiedProperties();
                }
            }
            GUILayout.Space(4);
        }
        if (!hasScript) GUILayout.Label("（カスタムスクリプトはアタッチされていません）", EditorStyles.miniLabel);
    }

    // 🌟【データ同期】大元のToolkitがドラッグ並び替えされた時、このチェッカーのセーブデータも一緒にスワップ
    public static void OnComponentSwapped(int oldIndex, int newIndex)
    {
        if (oldIndex >= checkerLocks.Count || newIndex >= checkerLocks.Count) return;
        bool tempLock = checkerLocks[oldIndex]; checkerLocks[oldIndex] = checkerLocks[newIndex]; checkerLocks[newIndex] = tempLock;
        GameObject tempObj = lockedObjects[oldIndex]; lockedObjects[oldIndex] = lockedObjects[newIndex]; lockedObjects[newIndex] = tempObj;
    }

    // 🌟【データ同期】コンポーネントがRemove削除された時、このチェッカーのセーブデータも消去
    public static void OnComponentRemoved(int index)
    {
        if (index < checkerLocks.Count)
        {
            checkerLocks.RemoveAt(index);
            lockedObjects.RemoveAt(index);
        }
    }
    public static bool IsLocked(int compIndex)
{
    while (checkerLocks.Count <= compIndex) checkerLocks.Add(false);
    return checkerLocks[compIndex];
}

public static void SetLocked(int compIndex, bool locked)
{
    while (checkerLocks.Count <= compIndex) checkerLocks.Add(false);
    while (lockedObjects.Count <= compIndex) lockedObjects.Add(null);
    checkerLocks[compIndex] = locked;
    if (locked) lockedObjects[compIndex] = Selection.activeGameObject;
}
}
