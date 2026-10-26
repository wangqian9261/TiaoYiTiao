/// <summary>
/// 五档难度参数表（策划案§3，基准方块边长 1.60 米、基准边缘间距 0.65 米）：
/// 轴心距 = 0.65 × U(间距倍率) × 系数 + 半边长和，生成时钳制进 150–1250ms 可达区间。
/// 转向率 = 本次跳跃切换行进轴的概率（低档长直道好瞄，高档频繁转向）。
/// 特殊格数量：specialCount = round((gridCount-2) × 密度)。
/// </summary>
public static class DifficultyProfiles
{
    public const float BaseTileSize = 1.60f;  // 基准方块边长
    public const float BaseEdgeGap = 0.65f;   // 基准边缘间距

    public static readonly DifficultyProfile[] All =
    {
        new DifficultyProfile
        {
            Tier = DifficultyTier.Easy, DisplayName = "简单",
            Coefficient = 0.80f, SizePct = 1.00f, GapMulLo = 0.80f, GapMulHi = 1.00f,
            TurnRate = 0.25f, SpecialDensity = 0.06f, ClearCoinMult = 1.0f,
        },
        new DifficultyProfile
        {
            Tier = DifficultyTier.Normal, DisplayName = "普通",
            Coefficient = 1.00f, SizePct = 0.92f, GapMulLo = 1.00f, GapMulHi = 1.25f,
            TurnRate = 0.35f, SpecialDensity = 0.08f, ClearCoinMult = 1.2f,
        },
        new DifficultyProfile
        {
            Tier = DifficultyTier.Hard, DisplayName = "困难",
            Coefficient = 1.25f, SizePct = 0.82f, GapMulLo = 1.15f, GapMulHi = 1.50f,
            TurnRate = 0.45f, SpecialDensity = 0.10f, ClearCoinMult = 1.5f,
        },
        new DifficultyProfile
        {
            Tier = DifficultyTier.Expert, DisplayName = "专家",
            Coefficient = 1.55f, SizePct = 0.72f, GapMulLo = 1.35f, GapMulHi = 1.80f,
            TurnRate = 0.55f, SpecialDensity = 0.12f, ClearCoinMult = 2.0f,
        },
        new DifficultyProfile
        {
            Tier = DifficultyTier.Master, DisplayName = "大师",
            Coefficient = 1.90f, SizePct = 0.65f, GapMulLo = 1.55f, GapMulHi = 2.00f,
            TurnRate = 0.65f, SpecialDensity = 0.14f, ClearCoinMult = 2.5f,
        },
    };

    public static SpecialTileRule[] DefaultRules(DifficultyTier tier)
    {
        int t = (int)tier;
        int[][] types = { new[] {100,0,0}, new[] {70,30,0}, new[] {45,35,20}, new[] {35,40,25}, new[] {30,45,25} };
        int[][] weights = { new[] {70,30,0,0,0}, new[] {20,60,20,0,0}, new[] {0,20,60,20,0}, new[] {0,0,20,50,30}, new[] {0,0,0,35,65} };
        var rules = new SpecialTileRule[3];
        for (int k = 0; k < 3; k++)
        {
            var levels = new System.Collections.Generic.List<EffectLevelWeight>();
            for (int id = 1; id <= 5; id++)
            {
                int w = k == 2 ? (t >= 2 && (id == t - 1 || id == t) ? 50 : 0) : weights[t][id - 1];
                levels.Add(new EffectLevelWeight(id, w));
            }
            rules[k] = new SpecialTileRule { Kind = (TileKind)(k + 2), Weight = types[t][k], Levels = levels.ToArray() };
        }
        return rules;
    }

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
