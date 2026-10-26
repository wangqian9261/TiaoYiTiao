using UnityEditor;
using UnityEngine;

/// <summary>
/// 编辑器试玩引导：
/// - 试玩请求经 SessionState 传递选中关卡（PackedLevelData JSON），进入现有主场景，
///   主页打开后免体力自动直入跳一跳——不生成、不保存任何临时正式关卡资产。
/// - 退出 Play 时清空试玩草稿，避免之后手动开局误入草稿关卡。
/// </summary>
[InitializeOnLoad]
public static class LevelPlaytestBootstrap
{
    static LevelPlaytestBootstrap()
    {
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    private static void OnPlayModeChanged(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.ExitingEditMode)
        {
            LevelRuntime.ClearPlaytest();
            Debug.Log("[LevelPlaytest] 退出 Play，已清空试玩草稿");
        }
    }
}
