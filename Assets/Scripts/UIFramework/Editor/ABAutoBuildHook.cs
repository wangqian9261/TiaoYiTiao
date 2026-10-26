using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// 打包前自动重建 AB（无需手动记得跑 UIFramework/3）：
/// - BuildPlayerWindow 钩子：点 Unity Build 按钮时，在构建管线启动前确保 AB 新鲜；
/// - ABPreprocessBuildHook（IPreprocessBuildWithReport）：任何 BuildPipeline.BuildPlayer 调用方的
///   前置钩子——微信面板「微信小游戏/转换小游戏」内部即走 BuildTarget.WeixinMiniGame 的
///   BuildPlayer，同样会被覆盖；
/// - 新鲜判定（全部满足才跳过）：marker 存在且目标平台一致 + StreamingAssets/ab 输出齐全
///   + GameRes 无资产晚于 marker + TJGenerators/History（外部模型/贴图源）无改动晚于 marker。
///   两类钩子在标准 Build 按钮下会先后各触发一次，第二次因已新鲜而秒过，无重复构建开销。
/// </summary>
[InitializeOnLoad]
public static class ABAutoBuildHook
{
    private const string MarkerFileName = "ABBuildTarget.marker"; // 工程根（与 ABBuilder/预检共用）
    private const string AbOutputDir = "Assets/StreamingAssets/ab";
    private const string ExternalSrcDir = "Assets/TJGenerators/History"; // AI 模型/贴图源（AB 外部依赖来源）
    private const float TouchGraceSeconds = 3f; // marker 自身写入与资产落盘的时钟容差

    static ABAutoBuildHook()
    {
        // Tuanjie 的 RegisterBuildPlayerHandler 会整体替换 Build 按钮默认行为（无 DefaultBuild 可回退），
        // 因此此处接管：先确保 AB 新鲜，再自己执行标准 BuildPlayer 并弹结果。
        BuildPlayerWindow.RegisterBuildPlayerHandler(OnBuildPlayer);
    }

    /// <summary>Build 按钮接管：确保 AB 新鲜 → 标准构建 → 结果提示</summary>
    private static void OnBuildPlayer(BuildPlayerOptions options)
    {
        EnsureBundlesFresh("Unity Build 按钮");
        var report = BuildPipeline.BuildPlayer(options);
        var summary = report.summary;
        if (summary.result == BuildResult.Succeeded)
            Debug.Log($"[ABAuto] 构建成功：{summary.totalSize / 1024 / 1024}MB，用时 {summary.totalTime.TotalSeconds:F0}s");
        else
            Debug.LogError($"[ABAuto] [ERROR] 构建未成功（{summary.result}），详见 Console");
        EditorUtility.DisplayDialog("Build",
            summary.result == BuildResult.Succeeded
                ? $"构建成功\n大小 {summary.totalSize / 1024 / 1024}MB · 用时 {summary.totalTime.TotalSeconds:F0}s"
                : $"构建未成功（{summary.result}），详见 Console",
            "知道了");
    }

    /// <summary>
    /// 确保 AB 与当前平台/资产状态一致；不新鲜则自动重跑 UIFramework/3 构建。
    /// 可手动调用/测试。返回 true=本次发生了重建。
    /// </summary>
    public static bool EnsureBundlesFresh(string context = null)
    {
        string active = EditorUserBuildSettings.activeBuildTarget.ToString();
        if (!IsStale(active, out string reason))
        {
            Debug.Log($"[ABAuto] AB 已新鲜（{active}），跳过重建{(string.IsNullOrEmpty(context) ? "" : $" @ {context}")}");
            return false;
        }

        Debug.Log($"[ABAuto] 检测到 AB 过期（{reason}）——自动重建 @ {active}{(string.IsNullOrEmpty(context) ? "" : $"，触发方 {context}")}");
        UIFrameworkABBuilder.BuildAllBundles();
        if (!IsStale(active, out string remain))
            Debug.Log("[ABAuto] 自动重建完成，AB 已与当前平台/资产同步");
        else
            Debug.LogError($"[ABAuto] [ERROR] 自动重建后仍不新鲜：{remain}");
        return true;
    }

    // ---------- 新鲜判定 ----------

    private static bool IsStale(string activeTarget, out string reason)
    {
        string markerPath = Path.Combine(
            Directory.GetParent(Application.dataPath).FullName, MarkerFileName);
        if (!File.Exists(markerPath))
        {
            reason = "未找到 ABBuildTarget.marker（从未构建过 AB）";
            return true;
        }

        string[] parts = File.ReadAllText(markerPath).Split('|');
        string markerTarget = parts.Length > 0 ? parts[0] : "";
        string markerTimeStr = parts.Length > 2 ? parts[2] : "";
        System.DateTime.TryParse(markerTimeStr, out var markerTime);
        if (markerTarget != activeTarget)
        {
            reason = $"AB 目标({markerTarget}) 与当前平台({activeTarget}) 不一致";
            return true;
        }

        // 输出目录必须存在且至少有清单包 + 若干业务包（防手滑删空）
        if (!Directory.Exists(AbOutputDir)
            || !File.Exists(Path.Combine(AbOutputDir, "ab"))
            || Directory.GetFiles(AbOutputDir, "*", SearchOption.TopDirectoryOnly)
                .Count(f => !f.EndsWith(".meta") && !f.EndsWith(".manifest")) < 2)
        {
            reason = "StreamingAssets/ab 输出缺失";
            return true;
        }

        // 改动检测窗口 = (marker, now]。
        // 上界=当前墙钟：未来时间戳的文件（时钟偏差/下载件 mtime 异常，如 Safari 下载的 SDK 资产）
        // 视为"已被本次构建覆盖"——否则 marker 永远追不上它，会造成每次打包都无谓重建。
        var now = System.DateTime.Now.AddSeconds(TouchGraceSeconds);

        // GameRes 资产改动（含 .meta 之外的实体文件）
        var newestRes = NewestMtimeUnder(BundleMap.ResRoot);
        if (newestRes.HasValue && markerTime != default
            && newestRes.Value > markerTime.AddSeconds(TouchGraceSeconds)
            && newestRes.Value <= now)
        {
            reason = $"GameRes 有资产晚于 AB 构建（{newestRes.Value:yyyy-MM-dd HH:mm}）";
            return true;
        }

        // 外部模型/贴图源改动（TJGenerators History 是 AB 隐式依赖的源头，不在 GameRes 的 mtime 扫描内）
        var newestExt = NewestMtimeUnder(ExternalSrcDir);
        if (newestExt.HasValue && markerTime != default
            && newestExt.Value > markerTime.AddSeconds(TouchGraceSeconds)
            && newestExt.Value <= now)
        {
            reason = $"外部模型源有改动晚于 AB 构建（{newestExt.Value:yyyy-MM-dd HH:mm}）";
            return true;
        }

        reason = null;
        return false;
    }

    private static System.DateTime? NewestMtimeUnder(string assetDir)
    {
        if (!Directory.Exists(assetDir))
            return null;
        System.DateTime? newest = null;
        foreach (var file in Directory.GetFiles(assetDir, "*", SearchOption.AllDirectories))
        {
            if (file.EndsWith(".meta") || file.EndsWith(".DS_Store"))
                continue;
            var time = File.GetLastWriteTime(file);
            if (newest == null || time > newest)
                newest = time;
        }
        return newest;
    }
}

/// <summary>
/// BuildPipeline.BuildPlayer 前置钩子（Unity 会自行实例化）：覆盖不经 Build 按钮的直调方，
/// 典型=微信面板「微信小游戏/转换小游戏」（内部 BuildTarget.WeixinMiniGame 的 BuildPlayer）。
/// </summary>
public class ABPreprocessBuildHook : IPreprocessBuildWithReport
{
    public int callbackOrder => -100; // 尽量先于其它预处理

    public void OnPreprocessBuild(BuildReport report)
    {
        ABAutoBuildHook.EnsureBundlesFresh($"BuildPlayer({report.summary.platform})");
    }
}
