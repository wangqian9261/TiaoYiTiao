using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// UIFramework 的 AB 构建工具（v2 细拆版）：
/// 把内容根（BundleMap.ResRoot = Assets/GameRes）下资产按 BundleMap 模块规则分包构建到
/// Assets/StreamingAssets/ab；根外依赖（TJGenerators 模型等）按"首个引用者"收编进对应包，
/// 跨包复用的依赖统一移入 shared_external 包（防 Unity 隐式重复打包）。
/// 构建前清空输出目录（旧包名产物必须删净）；构建后产出体积报告（控制台 + 工程根 BuildReports）。
/// 微信转换构建时由 WX SDK 自动转 data 文件（MiniGameConfig 配 assetLoadType=0 + CDN 时远程
/// 懒加载、SDK 自带磁盘缓存；assetLoadType=1 时进 data-package 分包，wasm+data 总限 ~29MB）。
/// 注意：AB 必须与"当前激活构建平台"一致 ——
/// - 编辑器回归测试：请在 Standalone 平台下执行本菜单；
/// - 打微信小游戏前：切换到 WeixinMiniGame 平台后必须重新执行本菜单（覆盖旧AB）。
/// </summary>
public static class UIFrameworkABBuilder
{
    private const string OutputDir = "Assets/StreamingAssets/ab";
    private const string SharedBundleName = "shared_external";
    private const string ReportDir = "BuildReports";
    private const long BundleWarnBytes = 4L * 1024 * 1024;   // 单包警告阈值 4MB

    [MenuItem("UIFramework/3. Build AssetBundles")]
    public static void BuildAllBundles()
    {
        // 生产路径：按当前激活平台构建（打微信小游戏前须切到 MiniGame 平台执行）
        BuildAllBundles(EditorUserBuildSettings.activeBuildTarget, OutputDir);
    }

    /// <summary>显式目标与输出目录的构建入口（编辑器回归可用 Standalone 目标构建到项目外）</summary>
    public static void BuildAllBundles(BuildTarget target, string outputDir)
    {
        string resRoot = BundleMap.ResRoot;
        if (!Directory.Exists(resRoot))
        {
            Debug.LogError($"[ABBuilder] 不存在 {resRoot}（请执行菜单 UIFramework/4. Migrate Resources → GameRes）");
            return;
        }

        // ---------- 1. 内容根资产 → BundleMap 分包 ----------
        var bundleAssets = new Dictionary<string, List<string>>();   // bundleName -> assetPaths
        var addressables = new Dictionary<string, List<string>>();   // bundleName -> addressableNames（与 assetPaths 对齐）
        string prefix = resRoot + "/";
        foreach (var guid in AssetDatabase.FindAssets("", new[] { resRoot }))
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(assetPath) || assetPath.EndsWith(".meta"))
                continue;
            if (AssetDatabase.IsValidFolder(assetPath))
                continue;
            if (!assetPath.StartsWith(prefix))
                continue;

            string key = Path.ChangeExtension(assetPath.Substring(prefix.Length), null);
            AddAsset(bundleAssets, addressables, BundleMap.KeyToBundleName(key), assetPath, key);
        }
        if (bundleAssets.Count == 0)
        {
            Debug.LogWarning($"[ABBuilder] {resRoot} 下没有可打包资产");
            return;
        }

        // ---------- 2. 根外依赖收编（防隐式重复打包） ----------
        // 依赖归属首个引用者所在包（内容根资产按路径排序，结果确定性）；被 ≥2 个包引用的依赖
        // 改入 shared_external 并警告（实际入包在下方统一执行）。WX SDK / 引擎内置（Library 路径）不收编。
        var depOwner = new Dictionary<string, string>();             // depPath -> bundleName
        foreach (var assetPath in bundleAssets.SelectMany(kv => kv.Value).OrderBy(p => p, StringComparer.Ordinal))
        {
            string currentBundle = BundleMap.KeyToBundleName(
                Path.ChangeExtension(assetPath.Substring(prefix.Length), null));
            foreach (var dep in AssetDatabase.GetDependencies(assetPath, true))
            {
                if (string.IsNullOrEmpty(dep) || ShouldSkipDep(dep, prefix))
                    continue;
                if (!depOwner.TryGetValue(dep, out string owner))
                {
                    depOwner[dep] = currentBundle;
                }
                else if (owner != SharedBundleName && owner != currentBundle)
                {
                    depOwner[dep] = SharedBundleName;
                    Debug.LogWarning($"[ABBuilder] 外部依赖被多包引用，收编入 {SharedBundleName}: {dep}");
                }
            }
        }
        foreach (var kv in depOwner.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            if (!bundleAssets.TryGetValue(kv.Value, out var assets))
            {
                bundleAssets[kv.Value] = assets = new List<string>();
                addressables[kv.Value] = new List<string>();
            }
            if (assets.Contains(kv.Key))
                continue;
            assets.Add(kv.Key);
            // 外部依赖不参与运行时按 key 寻址（BundleLoader 只按内容根地址加载），
            // addressableName 给工程路径去扩展名即可（保持 assetNames/addressableNames 对齐）
            addressables[kv.Value].Add(Path.ChangeExtension(kv.Key, null));
        }

        // ---------- 3. 清空输出目录（旧包名产物清理） ----------
        if (!Directory.Exists(outputDir))
            Directory.CreateDirectory(outputDir);
        foreach (var file in Directory.GetFiles(outputDir))
        {
            if (file.EndsWith(".meta"))
                continue;
            File.Delete(file);
            string meta = file + ".meta";
            if (File.Exists(meta))
                File.Delete(meta);
        }

        // ---------- 4. 构建 ----------
        var builds = new List<AssetBundleBuild>();
        foreach (var kv in bundleAssets.OrderBy(b => b.Key, StringComparer.Ordinal))
        {
            builds.Add(new AssetBundleBuild
            {
                assetBundleName = kv.Key,
                assetNames = kv.Value.ToArray(),
                addressableNames = addressables[kv.Key].ToArray(),
            });
        }

        var manifest = BuildPipeline.BuildAssetBundles(
            outputDir,
            builds.ToArray(),
            BuildAssetBundleOptions.ChunkBasedCompression,
            target);

        if (outputDir.StartsWith("Assets"))
            AssetDatabase.Refresh();
        if (manifest == null)
        {
            Debug.LogError("[ABBuilder] AB 构建失败");
            return;
        }

        // ---------- 5. 构建标记（供打包前检查校验目标平台与时效） ----------
        string markerPath = Path.Combine(
            Directory.GetParent(Application.dataPath).FullName, "ABBuildTarget.marker");
        File.WriteAllText(markerPath,
            target + "|" + System.DateTime.UtcNow.Ticks + "|" + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

        // ---------- 6. 体积报告 ----------
        string report = WriteSizeReport(target, outputDir, bundleAssets);
        Debug.Log(report);
    }

    private static bool ShouldSkipDep(string dep, string contentPrefix)
    {
        // 只收编 Assets 下、内容根外、非 WX SDK 的可打包工程资产（引擎内置资源为 Library 路径，天然排除）
        if (!dep.StartsWith("Assets/")
            || dep.StartsWith(contentPrefix)
            || dep.StartsWith("Assets/WX-WASM-SDK-V2")
            || dep.EndsWith(".meta")
            || AssetDatabase.IsValidFolder(dep))
            return true;

        // 脚本/插件不可进 AB：prefab/ScriptableObject 的 MonoScript 依赖会出现在 GetDependencies 结果里，
        // 显式列进 AssetBundleBuild 会被 BuildPipeline 拒绝（prefab 运行时按 GUID 解析脚本，无需打包）
        if (dep.EndsWith(".cs") || dep.EndsWith(".dll"))
            return true;
        var type = AssetDatabase.GetMainAssetTypeAtPath(dep);
        if (type == null || type.Name == "MonoScript")
            return true;

        return false;
    }

    private static void AddAsset(Dictionary<string, List<string>> bundleAssets,
        Dictionary<string, List<string>> addressables, string bundle, string assetPath, string relativeKey)
    {
        if (!bundleAssets.TryGetValue(bundle, out var assets))
        {
            bundleAssets[bundle] = assets = new List<string>();
            addressables[bundle] = new List<string>();
        }
        if (assets.Contains(assetPath))
            return;
        assets.Add(assetPath);
        // addressableName 与 BundleMap.KeyToAssetAddress 对齐：内容根 + 业务 key（不含扩展名）
        addressables[bundle].Add(BundleMap.ResRoot + "/" + relativeKey);
    }

    /// <summary>体积报告：控制台文本 + BuildReports/ab-report-<target>.json（工程根，不入包）</summary>
    private static string WriteSizeReport(BuildTarget target, string outputDir,
        Dictionary<string, List<string>> bundleAssets)
    {
        var entries = new List<(string bundle, long size, int count)>();
        long total = 0;
        foreach (var kv in bundleAssets)
        {
            string path = Path.Combine(outputDir, kv.Key);
            long size = File.Exists(path) ? new FileInfo(path).Length : 0;
            entries.Add((kv.Key, size, kv.Value.Count));
            total += size;
        }
        entries.Sort((a, b) => b.size.CompareTo(a.size));

        long boot = SumGroup(entries, BundleMap.BootGroup);
        long battle = SumGroup(entries, BundleMap.BattleGroup);

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"[ABBuilder] 构建完成，目标平台: {target}，共 {entries.Count} 包 / 总计 {total / 1024.0:F0}KB");
        foreach (var e in entries)
        {
            string flag = e.size > BundleWarnBytes ? "  [WARN>4MB]" : "";
            sb.AppendLine($"  - {e.bundle}: {e.count} assets, {e.size / 1024.0:F0}KB{flag}");
        }
        sb.AppendLine($"  Boot组 ≈ {boot / 1024.0:F0}KB；Battle组 ≈ {battle / 1024.0:F0}KB");

        try
        {
            string reportDir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, ReportDir);
            if (!Directory.Exists(reportDir))
                Directory.CreateDirectory(reportDir);
            var report = new AbBuildReport
            {
                target = target.ToString(),
                builtAtUtc = DateTime.UtcNow.ToString("o"),
                totalBytes = total,
                bundleCount = entries.Count,
                bootGroupBytes = boot,
                battleGroupBytes = battle,
                bundles = entries.Select(e => new BundleReportEntry
                {
                    bundle = e.bundle, sizeBytes = e.size, assetCount = e.count,
                }).ToArray(),
            };
            string json = JsonUtility.ToJson(report, true);
            string jsonPath = Path.Combine(reportDir, $"ab-report-{target}.json");
            File.WriteAllText(jsonPath, json);
            sb.AppendLine($"  报告: {jsonPath}");
        }
        catch (Exception e)
        {
            Debug.LogException(e); // 报告失败不影响构建产物
        }
        return sb.ToString();
    }

    private static long SumGroup(List<(string bundle, long size, int count)> entries, string[] group)
        => entries.Where(e => group.Contains(e.bundle)).Sum(e => e.size);

    [Serializable]
    private class BundleReportEntry
    {
        public string bundle;
        public long sizeBytes;
        public int assetCount;
    }

    [Serializable]
    private class AbBuildReport
    {
        public string target;
        public string builtAtUtc;
        public long totalBytes;
        public int bundleCount;
        public long bootGroupBytes;
        public long battleGroupBytes;
        public BundleReportEntry[] bundles;
    }
}
