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

    // ---------- 砖上物品（踩上后停留自动触发，无需操作） ----------
    public const float ItemStaySeconds = 1.5f;  // 停留触发秒数
    public const int ItemFirstIndex = 2;        // 前两块不刷（起手教学区）
    public const float ItemSpawnChance = 0.35f; // 每块携带物品的概率
    public const float ItemFloatHeight = 0.05f; // 物品贴合块顶摆放（极小离面缝隙，防陷云絮）
    public const float ItemVisualSize = 0.55f;  // 物品模型归一化高度
    // 携带物品时的类型权重（转盘最稀缺）
    public const int WeightCoinBag = 30;
    public const int WeightShard = 25;
    public const int WeightScoreBoost = 18;
    public const int WeightShield = 12;
    public const int WeightSprint = 10;
    public const int WeightWheel = 5;
    // 各物品效果数值
    public const int CoinBagFixed = 50;             // 金币袋：固定金币
    public const int CoinBagRandom = 30;           // + 随机 0..30
    public const int ShardAmount = 3;              // 碎片砖：固定碎片
    public const int ShardRandom = 2;              // + 随机 0..2
    public const float ScoreBoostPerStack = 0.20f; // 积分倍率每层 +20%
    public const float ScoreBoostMax = 2.00f;       // 倍率叠加上限（策划案：最多叠加到 2.0 倍）
    public const int ScoreBoostPerfectBonus = 3;   // 触发后本局完美落点额外分
    public const int SprintSkipBlocks = 3;         // 终点冲刺：跳过 3 块
    public const float SprintHopSeconds = 0.30f;   // 冲刺每跳滑行秒数
    public const float SprintHopHeight = 1.2f;     // 冲刺每跳弧高

    /// <summary>按权重随机挑选物品类型（不含 None）</summary>
    public static JumpItemKind RollItemKind()
    {
        int total = WeightCoinBag + WeightShard + WeightScoreBoost + WeightShield + WeightSprint + WeightWheel;
        int r = RandomUtil.Range(1, total + 1);
        if ((r -= WeightCoinBag) <= 0) return JumpItemKind.CoinBag;
        if ((r -= WeightShard) <= 0) return JumpItemKind.Shard;
        if ((r -= WeightScoreBoost) <= 0) return JumpItemKind.ScoreBoost;
        if ((r -= WeightShield) <= 0) return JumpItemKind.Shield;
        if ((r -= WeightSprint) <= 0) return JumpItemKind.Sprint;
        return JumpItemKind.Wheel;
    }

    /// <summary>物品显示名（HUD 提示/日志用）</summary>
    public static string GetItemName(JumpItemKind kind)
    {
        switch (kind)
        {
            case JumpItemKind.Wheel: return "幸运转盘";
            case JumpItemKind.ScoreBoost: return "积分增益";
            case JumpItemKind.CoinBag: return "金币袋";
            case JumpItemKind.Shard: return "皮肤碎片";
            case JumpItemKind.Shield: return "护盾保护";
            case JumpItemKind.Sprint: return "终点冲刺";
            default: return "";
        }
    }

    /// <summary>物品 3D 模型资源 Key（GameRes 模拟/AB 同构；AI 生成 ≤500 面）</summary>
    public static string GetItemResKey(JumpItemKind kind)
    {
        switch (kind)
        {
            case JumpItemKind.Wheel: return "Jump/Items/item_wheel";
            case JumpItemKind.ScoreBoost: return "Jump/Items/item_star";
            case JumpItemKind.CoinBag: return "Jump/Items/item_coinbag";
            case JumpItemKind.Shard: return "Jump/Items/item_shard";
            case JumpItemKind.Shield: return "Jump/Items/item_shield";
            case JumpItemKind.Sprint: return "Jump/Items/item_flag";
            default: return "";
        }
    }

    // ---------- 幸运转盘奖池（弹窗内 3 秒缓动落定；盘面 5 格与奖池一一对应） ----------
    public const int WheelSegments = 5;
    public const float WheelSpinSeconds = 3f;
    public static readonly string[] WheelLabels = { "金币 ×300", "金币 ×600", "碎片 ×8", "碎片 ×15", "双倍本局收益" };
    public static readonly int[] WheelCoins = { 300, 600, 0, 0, 0 };
    public static readonly int[] WheelShards = { 0, 0, 8, 15, 0 };
    public static readonly bool[] WheelDoubleRun = { false, false, false, false, true };
    public static readonly int[] WheelWeights = { 30, 15, 25, 15, 15 };

    // ---------- 陷阱砖（温和不阴间：全部带预警外观、可规避、无瞬间秒杀；落地即触发） ----------
    public const float TrapSpawnChance = 0.22f;  // 每块携带陷阱的概率（与物品互斥：先陷阱后物品）
    public const int TrapFirstIndex = 2;        // 起手区不刷
    public const int TrapMinLevel = 2;          // 第 1 关零陷阱（新手保护）
    public const int ReverseMinLevel = 5;      // 反向蓄力仅高阶关
    // 类型权重（幻影最克制）
    public const int TrapWeightSlippery = 24;
    public const int TrapWeightShake = 20;
    public const int TrapWeightSlowCharge = 18;
    public const int TrapWeightBurn = 14;
    public const int TrapWeightSeal = 11;
    public const int TrapWeightReverse = 8;
    public const int TrapWeightPhantom = 5;
    // 各陷阱参数
    public const float SlipperySlideRatio = 0.22f; // 打滑：滑移距离 = 块尺寸 × 此比例（限块内不出界）
    public const float SlipperySlideSeconds = 0.35f;
    public const int PhantomMaxPerLevel = 2;         // 幻影：一关最多 2 个
    public const float PhantomAlpha = 0.45f;         // 幻影：云台半透明度（主预警）
    public const int SlowChargeJumps = 3;          // 减速：接下来 3 次蓄力
    public const float SlowChargeFactor = 2.0f;      // 蓄力耗时 ×2（速度减半）
    public const float ShakeOffset = 0.55f;          // 震荡：下一块横移幅度（可达性保护内）
    public const float ShakeSlideSeconds = 0.3f;    // 横移滑行时长
    public const float BurnSeconds = 2.5f;          // 燃烧：倒计时秒数（充足反应时间）

    /// <summary>按权重随机挑选陷阱类型（不含 None；幻影/反向的关卡限制由调用方把关）</summary>
    public static JumpTrapKind RollTrapKind()
    {
        int total = TrapWeightSlippery + TrapWeightShake + TrapWeightSlowCharge
            + TrapWeightBurn + TrapWeightSeal + TrapWeightReverse + TrapWeightPhantom;
        int r = RandomUtil.Range(1, total + 1);
        if ((r -= TrapWeightSlippery) <= 0) return JumpTrapKind.Slippery;
        if ((r -= TrapWeightShake) <= 0) return JumpTrapKind.Shake;
        if ((r -= TrapWeightSlowCharge) <= 0) return JumpTrapKind.SlowCharge;
        if ((r -= TrapWeightBurn) <= 0) return JumpTrapKind.Burn;
        if ((r -= TrapWeightSeal) <= 0) return JumpTrapKind.Seal;
        if ((r -= TrapWeightReverse) <= 0) return JumpTrapKind.Reverse;
        return JumpTrapKind.Phantom;
    }

    /// <summary>陷阱显示名（HUD 提示/日志用）</summary>
    public static string GetTrapName(JumpTrapKind kind)
    {
        switch (kind)
        {
            case JumpTrapKind.Slippery: return "打滑砖";
            case JumpTrapKind.Phantom: return "幻影砖";
            case JumpTrapKind.SlowCharge: return "迷雾减速砖";
            case JumpTrapKind.Shake: return "震荡砖";
            case JumpTrapKind.Burn: return "燃烧砖";
            case JumpTrapKind.Reverse: return "反向蓄力砖";
            case JumpTrapKind.Seal: return "封印砖";
            default: return "";
        }
    }

    /// <summary>陷阱标记模型资源 Key（静置块顶正中心；AI 生成 ≤500 面）</summary>
    public static string GetTrapResKey(JumpTrapKind kind)
    {
        switch (kind)
        {
            case JumpTrapKind.Slippery: return "Jump/Traps/trap_slippery";
            case JumpTrapKind.Phantom: return "Jump/Traps/trap_phantom";
            case JumpTrapKind.SlowCharge: return "Jump/Traps/trap_slow";
            case JumpTrapKind.Shake: return "Jump/Traps/trap_shake";
            case JumpTrapKind.Burn: return "Jump/Traps/trap_burn";
            case JumpTrapKind.Reverse: return "Jump/Traps/trap_reverse";
            case JumpTrapKind.Seal: return "Jump/Traps/trap_seal";
            default: return "";
        }
    }

    /// <summary>陷阱云台染色（主预警：淡蓝磨砂/灰雾/红灼/紫反/黑雾/警示黄；幻影由透明度预警）</summary>
    public static Color GetTrapBlockColor(JumpTrapKind kind)
    {
        switch (kind)
        {
            case JumpTrapKind.Slippery: return new Color(0.72f, 0.90f, 0.99f);  // 淡蓝磨砂
            case JumpTrapKind.SlowCharge: return new Color(0.72f, 0.73f, 0.78f); // 灰迷雾
            case JumpTrapKind.Burn: return new Color(0.94f, 0.35f, 0.30f);    // 红灼烧
            case JumpTrapKind.Reverse: return new Color(0.60f, 0.40f, 0.92f);  // 紫反转
            case JumpTrapKind.Seal: return new Color(0.18f, 0.17f, 0.20f);     // 黑雾
            case JumpTrapKind.Shake: return new Color(0.96f, 0.88f, 0.52f);    // 警示黄
            default: return Color.white;                                       // 幻影：白色半透明预警
        }
    }

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

    // ---------- 有限关卡模式（关卡编辑器烘焙包驱动，替换原无限模式） ----------
    public const float LevelSprintSeconds = 1.2f;    // 冲刺演出总时长（越 3 格落第 4 格，不可打断）
    public const float LevelSprintHeight = 2.6f;      // 冲刺单弧高度
    public const float SlipperySlideDist = 0.12f;    // 打滑固定横移（策划案；方向由格子 effectSeed 决定）
    public const float PhantomCollapseSeconds = 1.5f; // 幻影：落地闪烁 1.5s 后塌陷（倒计时内跳走可规避）
    public const float PhantomBlinkAlphaMin = 0.12f;  // 幻影闪烁最低透明度
    public const float SealGhostAlpha = 0.5f;         // 封印：奖励图标轮廓化（隐藏颜色与发光）的幽灵透明度

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
    public const string WheelPanelKey = "Prefabs/PanelLuckyWheel"; // 幸运转盘弹窗（Popup 层）
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
