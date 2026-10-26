using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// AB 外部依赖体积治理（幂等）：内容根 GameRes 之外的 Texture2D（如 TJGenerators 生成的
/// 模型 PBR 贴图）会被 ABBuilder 收编为隐式依赖打进 AB，而默认导入设置
/// （maxSize 2048 + mipmap）会把包体撑爆微信小游戏 ~29MB 分包限制。
/// 本工具把这类贴图统一压到 maxSize=512、关 mipmap——模型屏占极小（归一化 0.55 高），
/// 512² 视觉无损；未达标的贴图跳过（幂等）。未来再用 AI 生成导入新模型后可重跑。
/// 注意：GameRes 内的 UI 切片贴图不经过本工具（已按屏手工调好导入参数）。
/// </summary>
public static class FrameworkAssetGovernance
{
    private const int MaxSize = 512;

    [MenuItem("Framework/4. Optimize AB External Textures")]
    public static void Run()
    {
        string report = BuildReport(out int changed);
        Debug.Log(report);
        EditorUtility.DisplayDialog("AB 外部贴图治理",
            changed > 0 ? $"已优化 {changed} 张贴图（512 / 关 mipmap），详见 Console。"
                        : "全部达标，无需改动（512 / 无 mipmap）。", "知道了");
    }

    /// <summary>生成治理报告（可被脚本调用做断言；changed=本次实际压缩张数）</summary>
    public static string BuildReport(out int changed)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("========== AB 外部贴图治理 ==========");

        var seen = new HashSet<string>();
        int total = 0; changed = 0; int ok = 0;

        foreach (string texPath in CollectExternalTexturePaths(seen))
        {
            total++;
            var importer = AssetImporter.GetAtPath(texPath) as TextureImporter;
            if (importer == null)
                continue;

            if (importer.maxTextureSize <= MaxSize && !importer.mipmapEnabled)
            {
                ok++;
                continue;
            }

            importer.maxTextureSize = MaxSize;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();
            changed++;
            sb.AppendLine($"  已压缩: {texPath}");
        }

        sb.AppendLine($"外部贴图共 {total} 张：达标 {ok}，本次压缩 {changed}");
        sb.AppendLine("========== 治理结束 ==========");
        return sb.ToString();
    }

    /// <summary>收集 GameRes 全部资产引用到、且位于内容根之外的 Texture2D 工程路径（确定性排序）</summary>
    private static List<string> CollectExternalTexturePaths(HashSet<string> seen)
    {
        string root = BundleMap.ResRoot;
        var results = new List<string>();
        if (!AssetDatabase.IsValidFolder(root))
            return results;

        string prefix = root + "/";
        foreach (var guid in AssetDatabase.FindAssets("", new[] { root }))
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(assetPath) || assetPath.EndsWith(".meta") || AssetDatabase.IsValidFolder(assetPath))
                continue;
            if (!assetPath.StartsWith(prefix))
                continue;

            foreach (var dep in AssetDatabase.GetDependencies(assetPath, true))
            {
                if (seen.Contains(dep) || !dep.StartsWith("Assets/") || dep.StartsWith(prefix))
                    continue;
                if (AssetImporter.GetAtPath(dep) as TextureImporter == null)
                    continue;
                seen.Add(dep);
                results.Add(dep);
            }
        }
        results.Sort();
        return results;
    }
}
