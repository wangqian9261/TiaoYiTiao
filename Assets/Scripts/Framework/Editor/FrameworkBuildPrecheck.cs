using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 打包前检查（微信小游戏转换/正式构建前执行）：
/// 1. AB 目标标记校验（ABBuildTarget.marker：目标平台一致性 + 是否早于 GameRes 最新资产）；
/// 2. 大资源扫描（贴图>1MB / 音频>500KB）；
/// 3. AB 体积报告（单包 >4MB 警告；总量目标 &lt;15MB、硬上限 20MB——包内模式 wasm+data 总限 ~29MB）；
/// 4. MiniGameConfig 一致性（CDN 模式必须有 CDN；DeleteStreamingAssets 建议=1）。
/// 本工具只做前置校验，转换流程仍由 WX SDK 面板执行。
/// </summary>
public static class FrameworkBuildPrecheck
{
    private const long TextureWarnBytes = 1024 * 1024;   // 1MB
    private const long AudioWarnBytes = 512 * 1024;      // 500KB
    private const long BundleWarnBytes = 4L * 1024 * 1024;      // 单包 4MB
    private const long AbTotalTargetBytes = 15L * 1024 * 1024;  // 总量目标 15MB
    private const long AbTotalHardBytes = 20L * 1024 * 1024;   // 总量硬上限 20MB

    private static string MarkerPath => Path.Combine(
        Directory.GetParent(Application.dataPath).FullName, "ABBuildTarget.marker");

    [MenuItem("Framework/2. Pre-Build Check")]
    public static void Run()
    {
        string report = BuildReport();
        Debug.Log(report);
        if (report.Contains("[ERROR]"))
            EditorUtility.DisplayDialog("打包前检查", "发现阻断问题，详见 Console（含 [ERROR] 的行）。", "知道了");
        else
            EditorUtility.DisplayDialog("打包前检查", "检查完成，无阻断性问题，详见 Console。", "知道了");
    }

    /// <summary>生成检查报告（可被脚本调用做断言）</summary>
    public static string BuildReport()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("========== Framework 打包前检查 ==========");

        // ---------- 1. AB 目标标记 ----------
        sb.AppendLine("[AB目标]");
        var active = EditorUserBuildSettings.activeBuildTarget;
        sb.AppendLine($"  当前激活构建平台: {active}");
        if (!File.Exists(MarkerPath))
        {
            sb.AppendLine("  [ERROR] 未找到 ABBuildTarget.marker —— 请先执行 UIFramework/3. Build AssetBundles");
        }
        else
        {
            string[] parts = File.ReadAllText(MarkerPath).Split('|');
            string markerTarget = parts[0];
            string markerTimeStr = parts.Length > 2 ? parts[2] : "";
            DateTime.TryParse(markerTimeStr, out var markerTime);
            sb.AppendLine($"  最近AB构建: {markerTarget} @ {markerTimeStr}");
            if (markerTarget != active.ToString())
                sb.AppendLine($"  [ERROR] AB 目标({markerTarget}) 与当前平台({active}) 不一致 —— 请在本平台重跑菜单 UIFramework/3");
            else
                sb.AppendLine("  [OK] AB 目标与当前平台一致");

            var newest = NewestGameResAssetTime();
            if (newest.HasValue && markerTime != default && newest.Value > markerTime)
                sb.AppendLine($"  [WARN] GameRes 有资产晚于 AB 构建（{newest.Value:yyyy-MM-dd HH:mm}）—— 建议重跑菜单3");
            else
                sb.AppendLine("  [OK] GameRes 资产未晚于最近 AB 构建");
        }

        // ---------- 2. 大资源扫描 ----------
        sb.AppendLine("[大资源扫描]");
        var bigItems = new List<(string path, long size, string type)>();
        foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets" }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (File.Exists(path))
            {
                long size = new FileInfo(path).Length;
                if (size > TextureWarnBytes) bigItems.Add((path, size, "Texture"));
            }
        }
        foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { "Assets" }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (File.Exists(path))
            {
                long size = new FileInfo(path).Length;
                if (size > AudioWarnBytes) bigItems.Add((path, size, "Audio"));
            }
        }

        if (bigItems.Count == 0)
        {
            sb.AppendLine("  [OK] 未发现超限贴图/音频");
        }
        else
        {
            int texCount = bigItems.Count(x => x.type == "Texture");
            int audioCount = bigItems.Count(x => x.type == "Audio");
            sb.AppendLine($"  [WARN] 大贴图 {texCount} 个（>1MB）、大音频 {audioCount} 个（>500KB），Top10：");
            foreach (var item in bigItems.OrderByDescending(x => x.size).Take(10))
                sb.AppendLine($"    {item.size / 1024f:F0}KB  [{item.type}] {item.path}");
        }

        // ---------- 3. AB 体积（微信包内模式 wasm+data 总限 ~29MB） ----------
        sb.AppendLine("[AB体积]");
        string abDir = Path.Combine(Application.streamingAssetsPath, "ab");
        if (!Directory.Exists(abDir))
        {
            sb.AppendLine("  [WARN] 未找到 Assets/StreamingAssets/ab —— 请先执行 UIFramework/3. Build AssetBundles");
        }
        else
        {
            var bundleFiles = Directory.GetFiles(abDir)
                .Where(f => !f.EndsWith(".manifest") && !f.EndsWith(".meta"))
                .Select(f => (name: Path.GetFileName(f), size: new FileInfo(f).Length))
                .OrderByDescending(x => x.size)
                .ToList();
            long total = bundleFiles.Sum(x => x.size);
            long boot = bundleFiles.Where(x => BundleMap.BootGroup.Contains(x.name)).Sum(x => x.size);
            long battle = bundleFiles.Where(x => BundleMap.BattleGroup.Contains(x.name)).Sum(x => x.size);

            foreach (var f in bundleFiles.Where(x => x.size > BundleWarnBytes))
                sb.AppendLine($"  [WARN] 单包超 4MB: {f.name}（{f.size / 1024.0:F0}KB）");
            if (bundleFiles.All(x => x.size <= BundleWarnBytes))
                sb.AppendLine("  [OK] 所有单包 ≤ 4MB");

            sb.AppendLine($"  AB 总量 {total / 1024.0:F0}KB（Boot组 {boot / 1024.0:F0}KB / Battle组 {battle / 1024.0:F0}KB / 共 {bundleFiles.Count} 包）");
            if (total > AbTotalHardBytes)
                sb.AppendLine("  [ERROR] AB 总量超硬上限 20MB —— 包内模式必然超 29MB 分包限制，请治理体积或切 CDN");
            else if (total > AbTotalTargetBytes)
                sb.AppendLine("  [WARN] AB 总量超目标 15MB —— 包内模式可用但偏重，建议治理");
            else
                sb.AppendLine("  [OK] AB 总量 ≤ 15MB 目标");
        }

        // ---------- 4. MiniGameConfig 一致性 ----------
        sb.AppendLine("[MiniGameConfig]");
        var configObj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>("Assets/WX-WASM-SDK-V2/Editor/MiniGameConfig.asset");
        if (configObj == null)
        {
            sb.AppendLine("  [WARN] 未找到 MiniGameConfig.asset（微信 SDK 未配置？）");
        }
        else
        {
            var so = new SerializedObject(configObj);
            var projectConf = so.FindProperty("ProjectConf");
            int assetLoadType = projectConf != null ? (projectConf.FindPropertyRelative("assetLoadType")?.intValue ?? -1) : -1;
            string cdn = projectConf?.FindPropertyRelative("CDN")?.stringValue ?? "";

            if (assetLoadType == 0 && string.IsNullOrEmpty(cdn))
                sb.AppendLine("  [ERROR] assetLoadType=0（CDN模式）但 CDN 为空 —— 数据/AB 无远程来源；请配置 CDN 或改回包内(1)");
            else if (assetLoadType == 0)
                sb.AppendLine($"  [OK] CDN 模式，CDN={cdn}（记得把转换产物上传到该 CDN；按包懒下载）");
            else
                sb.AppendLine("  [OK] 包内模式（assetLoadType=1）：AB 与数据文件进 data-package 分包（结构已 CDN 就绪：改 0+填 CDN+上传产物即可切换）");

            bool deleteSa = so.FindProperty("CompileOptions")?.FindPropertyRelative("DeleteStreamingAssets")?.boolValue ?? false;
            if (!deleteSa)
                sb.AppendLine("  [WARN] DeleteStreamingAssets=0：转换后 StreamingAssets 原文件不清理，包体可能偏大");
            else
                sb.AppendLine("  [OK] DeleteStreamingAssets=1（转换后清理原始 StreamingAssets）");
        }

        sb.AppendLine("提示：微信小游戏转换仍由 WX SDK 面板执行，本工具仅做前置校验。");
        sb.AppendLine("========== 检查结束 ==========");
        return sb.ToString();
    }

    private static DateTime? NewestGameResAssetTime()
    {
        const string resRoot = "Assets/GameRes";
        if (!Directory.Exists(resRoot)) return null;
        DateTime? newest = null;
        foreach (var file in Directory.GetFiles(resRoot, "*", SearchOption.AllDirectories))
        {
            if (file.EndsWith(".meta") || file.EndsWith(".DS_Store")) continue;
            var time = File.GetLastWriteTime(file);
            if (newest == null || time > newest) newest = time;
        }
        return newest;
    }
}
