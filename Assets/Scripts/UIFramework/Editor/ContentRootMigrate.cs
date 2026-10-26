using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 内容根迁移工具（一次性，幂等）：Assets/Resources → Assets/GameRes。
/// 目的：消除 Resources 资产同时进 webgl.data（首启固定下载）与 AB 的双份体积。
/// 整目录单次 MoveAsset，GUID 全保留——场景/prefab 内部引用不受影响；
/// 编辑器运行时加载改走 EditorAssetLoader（AssetDatabase 模拟），真机走 AB 不变。
/// 回滚：本菜单不可逆向；如需回退执行反向 MoveAsset（GameRes → Resources）即可，同样保 GUID。
/// </summary>
public static class ContentRootMigrate
{
    private const string SourceRoot = "Assets/Resources";
    private const string TargetRoot = "Assets/GameRes";

    [MenuItem("UIFramework/4. Migrate Resources → GameRes")]
    public static void Run()
    {
        if (AssetDatabase.IsValidFolder(TargetRoot))
        {
            Debug.Log($"[ContentRootMigrate] {TargetRoot} 已存在，无需迁移（幂等跳过）");
            return;
        }
        if (!AssetDatabase.IsValidFolder(SourceRoot))
        {
            Debug.LogError($"[ContentRootMigrate] 不存在 {SourceRoot}，无法迁移");
            return;
        }

        int before = CountAssets(SourceRoot);

        string err = AssetDatabase.MoveAsset(SourceRoot, TargetRoot);
        if (!string.IsNullOrEmpty(err))
        {
            Debug.LogError($"[ContentRootMigrate] 迁移失败: {err}（请检查 GameRes 是否被占用/重名）");
            return;
        }
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        int after = CountAssets(TargetRoot);
        Debug.Log($"[ContentRootMigrate] 迁移完成：{SourceRoot} → {TargetRoot}（资产 {before} → {after}，GUID 保留）");
        if (before != after)
            Debug.LogError($"[ContentRootMigrate] [ERROR] 迁移前后资产数不一致：{before} → {after}，请人工核查");
    }

    private static int CountAssets(string root)
    {
        return AssetDatabase.FindAssets("", new[] { root })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(p => !string.IsNullOrEmpty(p) && !p.EndsWith(".meta") && !AssetDatabase.IsValidFolder(p))
            .Count();
    }
}
