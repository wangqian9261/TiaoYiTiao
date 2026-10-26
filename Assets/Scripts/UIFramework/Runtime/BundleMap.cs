using System.Collections.Generic;

/// <summary>
/// key→AB包名/资产地址 的单一映射来源：ABBuilder（构建侧）与 BundleLoader（运行侧）共用，
/// 两端不再各写一份约定（旧实现在两个文件里各硬编码一次，极易漂移）。
/// 包粒度=模块对齐（微信小游戏按需下载的关键）：
/// - 首屏 UI 各屏独立（texture_home/loading/levelui/wheel，开哪屏拉哪包）
/// - 跳一跳 3D 资产按用途拆（jump_block/character/items/traps）
/// - 音频按用途拆（bgm 首屏即播 / sfx 战斗懒载）
/// 兜底规则：取 key 第一段小写；无分隔符 → "root"。
/// </summary>
public static class BundleMap
{
    /// <summary>内容根（原 Assets/Resources，已迁出消除 webgl.data 双份；AB 地址与编辑器模拟加载共用此前缀）</summary>
    public const string ResRoot = "Assets/GameRes";

    /// <summary>key 前缀 → 包名（匹配取最长前缀；包名全小写）</summary>
    private static readonly (string Prefix, string Bundle)[] Rules =
    {
        ("Jump/Block/",      "jump_block"),
        ("Jump/Character/",  "jump_character"),
        ("Jump/Items/",      "jump_items"),
        ("Jump/Traps/",      "jump_traps"),
        ("Texture/Home/",    "texture_home"),
        ("Texture/Loading/", "texture_loading"),
        ("Texture/LevelUI/", "texture_levelui"),
        ("Texture/Wheel/",   "texture_wheel"),
        ("Audio/bgm_",       "audio_bgm"),
        ("Audio/",           "audio_sfx"),
        ("Prefabs/",         "prefabs"),
        ("Config/",           "config"),
    };

    /// <summary>业务 key → AB 包名（与 ABBuilder 分包结果严格一致）</summary>
    public static string KeyToBundleName(string assetKey)
    {
        if (string.IsNullOrEmpty(assetKey))
            return "root";
        string best = null;
        int bestLen = -1;
        foreach (var rule in Rules)
        {
            if (assetKey.Length > rule.Prefix.Length
                && assetKey.StartsWith(rule.Prefix)
                && rule.Prefix.Length > bestLen)
            {
                best = rule.Bundle;
                bestLen = rule.Prefix.Length;
            }
        }
        if (best != null)
            return best;

        int slash = assetKey.IndexOf('/');
        return slash <= 0 ? "root" : assetKey.Substring(0, slash).ToLowerInvariant();
    }

    /// <summary>业务 key → AB 内资产地址（不含扩展名的工程路径；与 ABBuilder addressableName 严格一致）</summary>
    public static string KeyToAssetAddress(string assetKey)
    {
        return ResRoot + "/" + assetKey;
    }

    /// <summary>首屏组：boot 流程必然要拉的包（加载页/主页/配置/首屏 BGM）</summary>
    public static readonly string[] BootGroup =
    {
        "prefabs", "texture_loading", "texture_home", "audio_bgm", "config",
    };

    /// <summary>战斗组：进入跳一跳对局需要的包（主页停留期间后台闲时预热）</summary>
    public static readonly string[] BattleGroup =
    {
        "jump_block", "jump_character", "jump_items", "jump_traps",
        "texture_levelui", "texture_wheel", "audio_sfx",
    };
}
