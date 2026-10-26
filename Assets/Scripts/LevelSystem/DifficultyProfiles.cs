/// <summary>
/// 五档难度参数表（策划案§3，基准方块边长 1.60 米、基准边缘间距 0.65 米）：
/// 轴心距 = 0.65 × U(间距倍率) × 系数 + 半边长和，生成时钳制进 150–1250ms 可达区间。
/// 转向率 = 本次跳跃切换行进轴的概率（低档长直道好瞄，高档频繁转向）。
/// 特殊砖数量：rewardCount/trapCount = round((gridCount-2) × 密度)。
/// </summary>
public static class DifficultyProfiles
{
    public const float BaseTileSize = 1.60f;  // 基准方块边长
    public const float BaseEdgeGap = 0.65f;   // 基准边缘间距

    // TrapWeights 与 TrapKind 1..7 对位：打滑/幻影/减速/震荡/燃烧/反向/封印
    public static readonly DifficultyProfile[] All =
    {
        new DifficultyProfile
        {
            Tier = DifficultyTier.Easy, DisplayName = "简单",
            Coefficient = 0.80f, SizePct = 1.00f, GapMulLo = 0.80f, GapMulHi = 1.00f,
            TurnRate = 0.25f, RewardDensity = 0.12f, TrapDensity = 0.03f, ClearCoinMult = 1.0f,
            TrapWeights = new[] { 100, 0, 0, 0, 0, 0, 0 },
        },
        new DifficultyProfile
        {
            Tier = DifficultyTier.Normal, DisplayName = "普通",
            Coefficient = 1.00f, SizePct = 0.92f, GapMulLo = 1.00f, GapMulHi = 1.25f,
            TurnRate = 0.35f, RewardDensity = 0.11f, TrapDensity = 0.05f, ClearCoinMult = 1.2f,
            TrapWeights = new[] { 60, 0, 40, 0, 0, 0, 0 },
        },
        new DifficultyProfile
        {
            Tier = DifficultyTier.Hard, DisplayName = "困难",
            Coefficient = 1.25f, SizePct = 0.82f, GapMulLo = 1.15f, GapMulHi = 1.50f,
            TurnRate = 0.45f, RewardDensity = 0.10f, TrapDensity = 0.07f, ClearCoinMult = 1.5f,
            TrapWeights = new[] { 30, 0, 30, 20, 20, 0, 0 },
        },
        new DifficultyProfile
        {
            Tier = DifficultyTier.Expert, DisplayName = "专家",
            Coefficient = 1.55f, SizePct = 0.72f, GapMulLo = 1.35f, GapMulHi = 1.80f,
            TurnRate = 0.55f, RewardDensity = 0.09f, TrapDensity = 0.09f, ClearCoinMult = 2.0f,
            TrapWeights = new[] { 25, 15, 25, 20, 15, 0, 0 },
        },
        new DifficultyProfile
        {
            Tier = DifficultyTier.Master, DisplayName = "大师",
            Coefficient = 1.90f, SizePct = 0.65f, GapMulLo = 1.55f, GapMulHi = 2.00f,
            TurnRate = 0.65f, RewardDensity = 0.08f, TrapDensity = 0.11f, ClearCoinMult = 2.5f,
            TrapWeights = new[] { 15, 15, 15, 15, 15, 15, 10 },
        },
    };

    public static DifficultyProfile Get(DifficultyTier tier) => All[(int)tier];

    /// <summary>模拟目标首尝通关率（编辑器误差模拟的达标线；低于目标给警告）</summary>
    public static float TargetFirstTryRate(DifficultyTier tier)
    {
        switch (tier)
        {
            case DifficultyTier.Easy: return 0.75f;
            case DifficultyTier.Normal: return 0.60f;
            case DifficultyTier.Hard: return 0.45f;
            case DifficultyTier.Expert: return 0.30f;
            default: return 0.20f;
        }
    }
}
