using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 程序化关卡生成器（策划案§6）：
/// 1. PCG32 以 levelId + seed + generatorVersion 初始化，同输入永远同布局。
/// 2. 生成顺序：起点/普通路线/终点 → 按整关难度生成尺寸、间距与 X/Z 转向 →
///    蓄力可达钳制与最近格子重叠防护 → 推导奖励/陷阱数量 → 按权重抽取类型 →
///    优先放置冲刺、幻影、反向、封印等高约束类型 → 放置普通奖励与陷阱 →
///    校验器独立复查间隔/禁区/上限/效果冲突。
/// 3. 最多尝试 100 次；仍失败返回 null（关卡不可发布，绝不静默删减特殊砖）。
/// 4. 最终路线烘焙为整数毫米坐标数组并计算 layoutHash。
/// 几何口径：理想落点链 stands[j+1] = stands[j] + axis×D[j]；瓦片实际位置 = 理想落点 +
/// 横向震荡偏移。引擎落点判定只看行进轴投影，因此震荡偏移不影响本跳轴距——
/// "震荡仍满足可达性校验"由构造保证，校验器仍独立复核。
/// </summary>
public class LevelGenerator : ILevelGenerator
{
    public const string GeneratorVersion = "1.0.0";
    public const int MaxAttempts = 100;

    private readonly GlobalRewardConfig _rewardConfig;
    private readonly GlobalTrapConfig _trapConfig;

    public LevelGenerator(GlobalRewardConfig rewardConfig = null, GlobalTrapConfig trapConfig = null)
    {
        _rewardConfig = rewardConfig ?? GlobalRewardConfig.Default;
        _trapConfig = trapConfig ?? GlobalTrapConfig.Default;
    }

    public GeneratedLevel Generate(LevelDefinition definition, DifficultyProfile profile)
    {
        ulong seed = Pcg32.StreamSeed(definition.LevelId, definition.Seed, GeneratorVersion);
        var root = new Pcg32(seed);
        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            // 每次尝试一个独立子流；子流序号参与混合 → 尝试序列本身确定
            var rng = new Pcg32(root.NextULong(), (ulong)attempt * 2 + 1);
            var level = TryGenerate(definition, profile, rng);
            if (level != null) return level;
        }
        return null;
    }

    // ---------- 生成上下文 ----------

    private sealed class GenContext
    {
        public int N;
        public TileKind[] Kinds;
        public RewardKind[] Rewards;
        public TrapKind[] Traps;
        public bool[] Occupied;          // 已分配特殊砖或被冲刺保留为普通安全格
        public List<int> PlacedRewards = new List<int>();
        public List<int> PlacedTraps = new List<int>();
    }

    // ---------- 单次尝试 ----------

    private GeneratedLevel TryGenerate(LevelDefinition def, DifficultyProfile prof, Pcg32 rng)
    {
        int n = def.GridCount;
        if (n < 20 || n > 60) return null;
        if (prof == null || prof.TrapWeights == null || prof.TrapWeights.Length < 7) return null;

        // ---- 1. 路线：尺寸 / 轴 / 间距 ----
        var sizes = new float[n];
        for (int i = 0; i < n; i++)
            sizes[i] = DifficultyProfiles.BaseTileSize * prof.SizePct * rng.Range(0.95f, 1.05f);

        var stands = new Vector2[n];         // 理想落点链（跳一跳引擎的行进轴落线）
        var axes = new TileAxis[n];           // axes[i] = 跳入格 i 的行进轴（axes[0] 无意义）
        var jumpDist = new float[n - 1];      // 每跳轴心距（= 蓄力换算基准）

        TileAxis cur = rng.NextDouble() < 0.5 ? TileAxis.X : TileAxis.Z;
        for (int j = 0; j < n - 1; j++)
        {
            if (j > 0 && rng.NextDouble() < prof.TurnRate)
                cur = cur == TileAxis.X ? TileAxis.Z : TileAxis.X; // 转向：切换行进轴
            axes[j + 1] = cur;

            float gapEdge = DifficultyProfiles.BaseEdgeGap
                           * rng.Range(prof.GapMulLo, prof.GapMulHi)
                           * prof.Coefficient;
            float centerDist = gapEdge + (sizes[j] + sizes[j + 1]) * 0.5f;
            jumpDist[j] = LevelMath.ClampReach(centerDist); // 150–1250ms 可达区间内联保证
            stands[j + 1] = stands[j] + Dir(cur) * jumpDist[j];
        }

        // ---- 2. 数量推导与类型抽取 ----
        int rewardCount = Mathf.RoundToInt((n - 2) * prof.RewardDensity);
        int trapCount = Mathf.RoundToInt((n - 2) * prof.TrapDensity);
        var rewardKinds = DrawRewardKinds(rng, rewardCount);
        var trapKinds = DrawTrapKinds(rng, trapCount, prof.TrapWeights);
        if (rewardKinds == null || trapKinds == null) return null;

        // ---- 3. 类型分配（高约束优先） ----
        var ctx = new GenContext
        {
            N = n,
            Kinds = new TileKind[n],
            Rewards = new RewardKind[n],
            Traps = new TrapKind[n],
            Occupied = new bool[n],
        };
        for (int i = 0; i < n; i++) ctx.Kinds[i] = TileKind.Normal;
        ctx.Kinds[0] = TileKind.Start;
        ctx.Kinds[n - 1] = TileKind.End;
        ctx.Occupied[0] = true;
        ctx.Occupied[n - 1] = true;

        // 3.1 冲刺（奖励；终点前 5 格禁放；后 4 格必须全部为普通安全格）
        if (!PlaceRewardKind(rng, rewardKinds, RewardKind.Sprint, ctx, TryPlaceSprint)) return null;
        // 3.2 幻影 / 反向 / 封印（高约束陷阱）
        if (!PlaceTrapKind(rng, trapKinds, TrapKind.Phantom, ctx)) return null;
        if (!PlaceTrapKind(rng, trapKinds, TrapKind.Reverse, ctx)) return null;
        if (!PlaceTrapKind(rng, trapKinds, TrapKind.Seal, ctx)) return null;
        // 3.3 普通陷阱：震荡 / 燃烧 / 打滑 / 减速（减速须避开反向格覆盖窗）
        if (!PlaceTrapKind(rng, trapKinds, TrapKind.Shake, ctx)) return null;
        if (!PlaceTrapKind(rng, trapKinds, TrapKind.Burn, ctx)) return null;
        if (!PlaceTrapKind(rng, trapKinds, TrapKind.Slippery, ctx)) return null;
        if (!PlaceTrapKind(rng, trapKinds, TrapKind.SlowCharge, ctx)) return null;
        // 3.4 普通奖励：转盘 / 碎片 / 护盾 / 积分增益 / 金币
        if (!PlaceRewardKind(rng, rewardKinds, RewardKind.LuckyWheel, ctx, TryPlaceReward)) return null;
        if (!PlaceRewardKind(rng, rewardKinds, RewardKind.SkinShard, ctx, TryPlaceReward)) return null;
        if (!PlaceRewardKind(rng, rewardKinds, RewardKind.Shield, ctx, TryPlaceReward)) return null;
        if (!PlaceRewardKind(rng, rewardKinds, RewardKind.ScoreBoost, ctx, TryPlaceReward)) return null;
        if (!PlaceRewardKind(rng, rewardKinds, RewardKind.Coin, ctx, TryPlaceReward)) return null;

        // ---- 4. 瓦片组装（位置 = 理想落点 + 震荡横移；效果种子） ----
        var tiles = new List<GeneratedTile>(n);
        for (int i = 0; i < n; i++)
        {
            tiles.Add(new GeneratedTile
            {
                Index = i,
                Kind = ctx.Kinds[i],
                ArrivalAxis = i == 0 ? TileAxis.X : axes[i],
                X = stands[i].x,
                Z = stands[i].y,
                Size = sizes[i],
                Reward = ctx.Rewards[i],
                Trap = ctx.Traps[i],
                EffectSeed = (ctx.Kinds[i] == TileKind.Reward || ctx.Kinds[i] == TileKind.Trap)
                    ? rng.NextUInt() : 0u,
            });
        }
        // 震荡横移：上一格为震荡陷阱时，本格相对理想落点横向偏移 0.15–0.25m（方向由种子决定）
        for (int i = 0; i < n; i++)
        {
            if (ctx.Traps[i] != TrapKind.Shake) continue;
            float magnitude = rng.Range(_trapConfig.ShakeOffsetMin, _trapConfig.ShakeOffsetMax);
            float sign = rng.NextDouble() < 0.5 ? -1f : 1f;
            float offset = magnitude * sign;
            int next = i + 1;
            var lateral = Dir(Other(axes[next]));
            var t = tiles[next];
            t.X += lateral.x * offset;
            t.Z += lateral.y * offset;
            t.ShakeOffset = offset;
        }

        // ---- 5. 估算通关时长（60–120s 之外编辑器警告） ----
        float seconds = 0f;
        for (int j = 0; j < n - 1; j++)
        {
            seconds += LevelMath.ChargeMsAtDist(jumpDist[j]) * 0.001f
                     + LevelMath.AirTimeAtDist(jumpDist[j]) + 0.35f;
        }
        bool hasWheel = false; bool hasSprint = false;
        for (int i = 0; i < n; i++)
        {
            if (ctx.Rewards[i] == RewardKind.LuckyWheel) hasWheel = true;
            if (ctx.Rewards[i] == RewardKind.Sprint) hasSprint = true;
        }
        if (hasWheel) seconds += 3.5f; // 转盘演出 + 领奖窗口
        if (hasSprint)
        {
            float avg = seconds / (n - 1);
            seconds += JumpConfig.LevelSprintSeconds - avg * 4f; // 一次冲刺替代 4 跳
        }

        // ---- 6. 毫米烘焙 + layoutHash ----
        var level = new GeneratedLevel
        {
            Definition = CloneDefinition(def),
            GeneratorVersion = GeneratorVersion,
            TileCount = n,
            Tiles = tiles,
            PredictedClearSeconds = seconds,
        };
        foreach (var t in tiles)
        {
            // 烘焙口径：先舍入到毫米再回填，运行时与 layoutHash 共用同一数值
            t.X = LevelMath.FromMm(LevelMath.ToMm(t.X));
            t.Z = LevelMath.FromMm(LevelMath.ToMm(t.Z));
            t.Size = LevelMath.FromMm(LevelMath.ToMm(t.Size));
            t.ShakeOffset = LevelMath.FromMm(LevelMath.ToMm(t.ShakeOffset));
        }
        level.LayoutHash = LevelPackIo.ComputeLayoutHash(level);
        return level;
    }

    // ---------- 类型抽取 ----------

    /// <summary>奖励类型序列：固定权重 + 单关上限，上限后重新归一化；无法选择时降级为金币砖</summary>
    private List<RewardKind> DrawRewardKinds(Pcg32 rng, int count)
    {
        var result = new List<RewardKind>(count);
        int wheel = 0, shard = 0, shield = 0, sprint = 0;
        for (int k = 0; k < count; k++)
        {
            var w = new int[6]; // 对位 RewardKind 1..6：转盘/增益/金币/碎片/护盾/冲刺
            w[0] = _rewardConfig.WeightLuckyWheel;
            w[1] = _rewardConfig.WeightScoreBoost;
            w[2] = _rewardConfig.WeightCoin;
            w[3] = _rewardConfig.WeightSkinShard;
            w[4] = _rewardConfig.WeightShield;
            w[5] = _rewardConfig.WeightSprint;
            if (wheel >= _rewardConfig.CapLuckyWheel) w[0] = 0;
            if (shard >= _rewardConfig.CapSkinShard) w[3] = 0;
            if (shield >= _rewardConfig.CapShield) w[4] = 0;
            if (sprint >= _rewardConfig.CapSprint) w[5] = 0;

            int idx = rng.WeightedIndex(w);
            var kind = idx < 0 ? RewardKind.Coin : (RewardKind)(idx + 1); // 无法选择时降级为金币砖
            if (kind == RewardKind.LuckyWheel) wheel++;
            else if (kind == RewardKind.SkinShard) shard++;
            else if (kind == RewardKind.Shield) shield++;
            else if (kind == RewardKind.Sprint) sprint++;
            result.Add(kind);
        }
        return result;
    }

    /// <summary>陷阱类型序列：按档位权重 + 上限（幻影≤2、反向≤1、封印≤1）</summary>
    private List<TrapKind> DrawTrapKinds(Pcg32 rng, int count, int[] tierWeights)
    {
        var result = new List<TrapKind>(count);
        int phantom = 0, reverse = 0, seal = 0;
        var w = new int[7];
        for (int k = 0; k < count; k++)
        {
            for (int i = 0; i < 7; i++) w[i] = tierWeights[i];
            if (phantom >= _trapConfig.CapPhantom) w[1] = 0;
            if (reverse >= _trapConfig.CapReverse) w[5] = 0;
            if (seal >= _trapConfig.CapSeal) w[6] = 0;
            int idx = rng.WeightedIndex(w);
            if (idx < 0) return null; // 档位权重全 0（配置异常）→ 本次尝试失败
            var kind = (TrapKind)(idx + 1);
            if (kind == TrapKind.Phantom) phantom++;
            else if (kind == TrapKind.Reverse) reverse++;
            else if (kind == TrapKind.Seal) seal++;
            result.Add(kind);
        }
        return result;
    }

    // ---------- 放置（随机起点环扫；一轮扫完仍放不下 → 尝试失败，绝不静默删减） ----------

    private delegate bool RewardPlacement(int i, RewardKind kind, GenContext ctx);
    private delegate bool TrapPlacement(int i, TrapKind kind, GenContext ctx);

    private bool PlaceRewardKind(Pcg32 rng, List<RewardKind> kinds, RewardKind kind, GenContext ctx, RewardPlacement placement)
    {
        int remaining = 0;
        for (int i = 0; i < kinds.Count; i++) if (kinds[i] == kind) remaining++;
        while (remaining > 0)
        {
            int start = rng.Range(0, ctx.N);
            bool placed = false;
            for (int c = 0; c < ctx.N && !placed; c++)
                placed = placement((start + c) % ctx.N, kind, ctx);
            if (!placed) return false;
            remaining--;
        }
        return true;
    }

    private bool PlaceTrapKind(Pcg32 rng, List<TrapKind> kinds, TrapKind kind, GenContext ctx)
        => PlaceTrapKind(rng, kinds, kind, ctx, TryPlaceTrap);

    private bool PlaceTrapKind(Pcg32 rng, List<TrapKind> kinds, TrapKind kind, GenContext ctx, TrapPlacement placement)
    {
        int remaining = 0;
        for (int i = 0; i < kinds.Count; i++) if (kinds[i] == kind) remaining++;
        while (remaining > 0)
        {
            int start = rng.Range(0, ctx.N);
            bool placed = false;
            for (int c = 0; c < ctx.N && !placed; c++)
                placed = placement((start + c) % ctx.N, kind, ctx);
            if (!placed) return false;
            remaining--;
        }
        return true;
    }

    /// <summary>奖励放置：起手区后、非终点、未占用、与既有奖励间隔 ≥ MinRewardIndexGap</summary>
    private bool TryPlaceReward(int i, RewardKind kind, GenContext ctx)
    {
        int n = ctx.N;
        if (i < _rewardConfig.FirstRewardIndex || i > n - 2 || ctx.Occupied[i]) return false;
        for (int j = 0; j < ctx.PlacedRewards.Count; j++)
            if (Mathf.Abs(i - ctx.PlacedRewards[j]) < _rewardConfig.MinRewardIndexGap) return false;
        ctx.Occupied[i] = true;
        ctx.Kinds[i] = TileKind.Reward;
        ctx.Rewards[i] = kind;
        ctx.PlacedRewards.Add(i);
        return true;
    }

    /// <summary>冲刺放置：终点前 5 格禁放；后 4 格必须全部为普通安全格（保留占用）</summary>
    private bool TryPlaceSprint(int i, RewardKind kind, GenContext ctx)
    {
        int n = ctx.N;
        int maxIndex = n - 1 - _trapConfig.SprintPreEndExclusion - 1; // 终点前 5 格内禁放
        if (i < _rewardConfig.FirstRewardIndex || i > maxIndex || ctx.Occupied[i]) return false;
        if (i + _trapConfig.SprintTailSafeTiles > n - 1) return false; // 落点必须存在
        for (int g = i + 1; g <= i + _trapConfig.SprintTailSafeTiles; g++)
            if (ctx.Occupied[g]) return false; // 后 4 格须全为普通
        for (int j = 0; j < ctx.PlacedRewards.Count; j++)
            if (Mathf.Abs(i - ctx.PlacedRewards[j]) < _rewardConfig.MinRewardIndexGap) return false;

        ctx.Occupied[i] = true;
        ctx.Kinds[i] = TileKind.Reward;
        ctx.Rewards[i] = kind;
        ctx.PlacedRewards.Add(i);
        for (int g = i + 1; g <= i + _trapConfig.SprintTailSafeTiles; g++)
            ctx.Occupied[g] = true; // 保留为普通安全格（其后任何特殊砖分配都会跳过）
        return true;
    }

    /// <summary>陷阱放置：禁区(前 5 可跳格 / 终点前 3 格 / 起终点)、未占用、与既有陷阱不连续</summary>
    private bool TryPlaceTrap(int i, TrapKind kind, GenContext ctx)
    {
        int n = ctx.N;
        if (i < _trapConfig.FirstTrapIndex || i > n - 1 - _trapConfig.PreEndTrapFree - 1) return false;
        if (ctx.Occupied[i]) return false;
        for (int j = 0; j < ctx.PlacedTraps.Count; j++)
            if (Mathf.Abs(i - ctx.PlacedTraps[j]) <= 1) return false; // 陷阱不得连续

        if (kind == TrapKind.SlowCharge)
        {
            // 减速效果覆盖的后 3 格内不得放置反向蓄力砖（反向已先行放置）
            for (int g = i + 1; g <= i + _trapConfig.SlowCoverReverseBan && g < n; g++)
                if (ctx.Traps[g] == TrapKind.Reverse) return false;
        }

        ctx.Occupied[i] = true;
        ctx.Kinds[i] = TileKind.Trap;
        ctx.Traps[i] = kind;
        ctx.PlacedTraps.Add(i);
        return true;
    }

    // ---------- 工具 ----------

    private static Vector2 Dir(TileAxis axis) => axis == TileAxis.X ? Vector2.right : Vector2.up; // up 即 +Z

    private static TileAxis Other(TileAxis axis) => axis == TileAxis.X ? TileAxis.Z : TileAxis.X;

    private static LevelDefinition CloneDefinition(LevelDefinition def)
        => new LevelDefinition
        {
            LevelId = def.LevelId,
            ChapterId = def.ChapterId,
            OrderInChapter = def.OrderInChapter,
            GridCount = def.GridCount,
            Difficulty = def.Difficulty,
            Seed = def.Seed,
            ThemeId = def.ThemeId,
            Enabled = def.Enabled,
        };
}
