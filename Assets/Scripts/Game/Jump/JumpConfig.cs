using UnityEngine;

/// <summary>
/// 跳一跳调参中心：操作手感 / 难度曲线 / 得分 / 镜头 / 资源Key / 配色。
/// 所有可调常量集中在此，试玩调参只改这里。
/// 难度设计目标：失误率随块数平滑上升，单局 30s~2min（中位 15~25 跳）；
/// 最远可跳距离恒定，任何时刻正确蓄力都必然能跳到下一块（不突然暴死）。
/// </summary>
public static class JumpConfig
{
    // ---------- 操作手感 ----------
    /// <summary>One complete low-to-high sweep; repeats without holding at maximum.</summary>
    public const float ChargeCycleSeconds = 1.35f;
    public const float ChargeCancelSeconds = 30f;
    // Compatibility for baked level first-cycle timing; NOT a saturation limit.
    public const float MaxChargeSeconds = ChargeCycleSeconds;
    public const float MinJumpDist = 1.6f;      // 零蓄力跳跃距离
    public const float MaxJumpDist = 7.0f;      // 满蓄力跳跃距离（难度上限兜底）
    public const float AirTimeBase = 0.42f;     // 滞空基础秒
    public const float AirTimePerPower = 0.10f; // 滞空随蓄力增加
    public const float JumpHeightBase = 1.8f;   // 抛物线峰高 = Base + dist * PerDist
    public const float JumpHeightPerDist = 0.18f;

    // ---------- 难度曲线（blockIndex 从 0 起） ----------
    public const float BlockSizeStart = 2.2f;
    public const float BlockSizeMin = 1.15f;
    public const float BlockSizeShrinkPerBlock = 0.026f; // 约 40 块后到最小
    public const float GapMinStart = 1.2f;               // 间隙（边缘到边缘）波动区间
    public const float GapMinEnd = 1.8f;
    public const float GapMaxStart = 2.2f;
    public const float GapMaxEnd = 4.2f;
    public const float DifficultyRampBlocks = 42f;       // 间隙波动拉满所需块数
    /// <summary>块心距钳制上界（略小于 MaxJumpDist，保证满蓄力必达）</summary>
    public const float MaxCenterDist = 6.4f;

    public static float GetBlockSize(int blockIndex)
        => Mathf.Max(BlockSizeMin, BlockSizeStart - BlockSizeShrinkPerBlock * blockIndex);

    /// <summary>本次跳跃的随机间隙（难度随跳跃序号平滑拉大）</summary>
    public static float GetGap(int jumpIndex)
    {
        float t = Mathf.Clamp01(jumpIndex / DifficultyRampBlocks);
        float min = Mathf.Lerp(GapMinStart, GapMinEnd, t);
        float max = Mathf.Lerp(GapMaxStart, GapMaxEnd, t);
        return RandomUtil.Range(min, max);
    }

    // ---------- 得分 ----------
    public const int BaseLandScore = 1;
    /// <summary>完美判定：距块心 ≤ 半宽 × 此比例</summary>
    public const float PerfectRadiusScale = 0.30f;
    /// <summary>完美连击加分 2/4/8/16/32…（封顶）</summary>
    public const int PerfectComboCap = 32;
    /// <summary>惊险边缘：|偏移| > 半宽 × 此比例 触发摇晃（仍存活，容错演出）</summary>
    public const float WobbleStartScale = 0.75f;

    // ---------- 关卡 ----------
    public const int LevelBlocksBase = 8;      // 第 1 关需前进的块数
    public const int LevelBlocksStep = 2;      // 每关目标 +2 块
    public const int LevelBlocksMax = 24;      // 目标封顶
    public const int LevelDifficultyStep = 4;  // 每关难度起始偏移（借用既有难度曲线）
    public const int LevelDifficultyMax = 36;  // 起始偏移封顶（曲线已近极限）
    /// <summary>终点块颜色（Elbaz 金冠色，区别于六色调色板与彩蛋红粉）</summary>
    public static readonly Color GoalBlockColor = new Color(1f, 0.84f, 0.38f);

    public static readonly string[] LevelThemeNames =
    {
        "梦境剧场", "粉雾花园", "糖果游行", "云朵马戏团", "蕾丝沙龙",
        "蝴蝶结舞会", "珍珠歌剧院", "香水之夜", "缎带天桥", "终幕嘉年华",
    };

    /// <summary>第 level 关需前进的块数（终点块 Index，0 起计）</summary>
    public static int GetLevelTargetBlocks(int level)
        => Mathf.Min(LevelBlocksBase + (level - 1) * LevelBlocksStep, LevelBlocksMax);

    /// <summary>第 level 关难度起始偏移：高关开局块更小/缝更宽（复用既有曲线，零新增难度公式）</summary>
    public static int GetLevelStartIndex(int level)
        => Mathf.Min((level - 1) * LevelDifficultyStep, LevelDifficultyMax);

    /// <summary>关卡显示名（主题名 10 个循环，关号持续递增）</summary>
    public static string GetLevelName(int level)
        => $"第 {level} 关 · {LevelThemeNames[(level - 1) % LevelThemeNames.Length]}";

    // ---------- 镜头 ----------
    /// <summary>斜上方 30° 俯角：offset.y / horizontal = tan30°（拉近后角色更醒目）</summary>
    public static readonly Vector3 CamOffset = new Vector3(-6.4f, 5.25f, -6.4f); // 距离10.5，恰为30°
    /// <summary>注视点沿路径对角线前移量：角色落于屏幕约 0.35 高（位置与注视点同步前移，旋转恒定不变式保持；实测 1.6→0.39、块距噪声±0.04，2.0 居中微调）</summary>
    public const float CamLookAhead = 2.0f;
    public static readonly Vector3 CamLookAheadDir = new Vector3(1f, 0f, 1f).normalized; // 之字路径平均前进向
    public const float CamFollowLerp = 4.5f;   // 1-exp(-k·dt) 指数平滑（位置与注视点同频）
    public const float CamLandDip = 0.08f;     // 落地镜头微沉幅度
    public const float CamLandDipTime = 0.28f; // 微沉正弦脉冲总时长（平滑起落）
    public const float CamShakeDeath = 0.35f;  // 死亡镜头抖动
    public const float CamShakeTime = 0.5f;

    // ---------- 角色 ----------
    public const float CharacterHeight = 0.9f; // 模型归一化高度（放大版：0.55→0.75→0.9）
    public const float ChargeSquashMinY = 0.55f; // 满蓄力纵向压缩
    public const float ChargeWidenXZ = 0.18f;     // 满蓄力横向增宽

    // ---------- 世界（浮岛） ----------
    public const float BlockTopThickness = 0.55f;   // 浮岛顶板厚度（薄）
    public const float BlockUnderThickness = 0.4f; // 收分底座厚度（倒梯形浮岛剪影）
    public const float BlockUnderInset = 0.72f;    // 底座相对顶板的缩进比例
    public const float DeathFallY = -9f;
    public static readonly Color FogColor = new Color(0.95f, 0.86f, 0.93f); // 粉紫晨雾（呼应品牌天空盒）
    public const float FogDensity = 0.022f;

    // ---------- 资源Key（GameRes 模拟/AB 同构） ----------
    public const string CharacterResKey = "Jump/Character/jump_character";
    public const string BlockPoolKey = "jump_block";
    public const string BlockPrefabResKey = "Jump/Block/jump_block";
    public const string HudPanelKey = "Prefabs/UIBattleView"; // 关卡 HUD（v3，替换旧 Panel_JumpHud）
    public const string ResultPanelKey = "Prefabs/Panel_JumpResult";
    public const string HomePanelKey = "Prefabs/UIHomeView"; // 正式主页（原 Panel_Home 已退役为演示资产）
    public const string BagPanelKey = "Prefabs/PanelBag";          // 背包弹窗（Popup 层）

    // ---------- 音效 ----------
    public const string SfxLand = "Audio/jump_land";
    public const string SfxFly = "Audio/jump_fly";
    public const string SfxPerfect = "Audio/jump_perfect";
    public const string SfxBonus = "Audio/jump_bonus"; // 物品触发/转盘/通关共用欢快音
    public const string SfxFall = "Audio/jump_fall";
    public const string BgmKey = "Audio/bgm_jump";
    public const string HomeBgmKey = "Audio/bgm_loop";

    // ---------- 配色（Laurence & Chico × Alber Elbaz 品牌风：撞色戏剧感） ----------
    public static readonly Color[] BlockPalette =
    {
        new Color(1.00f, 0.09f, 0.53f), // 艳粉 Fuchsia（主色）
        new Color(1.00f, 0.95f, 0.87f), // 奶油白 Cream
        new Color(0.85f, 0.72f, 0.97f), // 薰衣草紫 Lavender
        new Color(0.90f, 0.88f, 0.85f), // 珍珠灰 Pearl
        new Color(0.99f, 0.76f, 0.84f), // 樱贝粉 Blossom
        new Color(0.15f, 0.13f, 0.17f), // 黑色点缀（L&C 黑格纹）
    };
    public static readonly Color CharacterColor = new Color(0.25f, 0.27f, 0.35f);  // 兜底棋子色（AI女孩模型缺席时）
}
