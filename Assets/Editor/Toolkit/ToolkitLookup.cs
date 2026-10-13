using UnityEditor;
using UnityEngine;

public static class ToolkitLookup
{
    public static int mode = 0;
    private static readonly string[] modes = { "オブジェクト → スクリプト", "スクリプト → オブジェクト" };

    public static void DrawMenu(int compIndex, EditorWindow window)
    {
        mode = GUILayout.Toolbar(mode, modes);
        GUILayout.Space(5);

        if (mode == 0) ToolkitScriptChecker.DrawMenu(compIndex, window);
        else ToolkitScriptFinder.DrawMenu();
    }
}