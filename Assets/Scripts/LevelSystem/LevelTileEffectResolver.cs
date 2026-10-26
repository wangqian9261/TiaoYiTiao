using UnityEngine;

/// <summary>
/// 落格效果解析器（策划案核心接口 ITileEffectResolver 的有限关卡实现）：
/// 按格子烘焙数据 + 局内状态给出效果参数；数值统一来自全局经济配置与 JumpConfig 常量。
/// 演出与流程（撒花/音效/UI/状态机）由 JumpGame 执行——本类只做"这一格落上去会发生什么"的裁决。
/// </summary>
public class LevelTileEffectResolver : ITileEffectResolver
{
    private readonly LevelEconomyConfig _economy;

    public LevelTileEffectResolver(LevelEconomyConfig economy)
    {
        _economy = economy != null ? economy : LevelEconomyConfig.Fallback;
    }

    public TileEffectResult ResolveLanding(GeneratedTile tile, RunStateSnapshot runState)
    {
        var r = new TileEffectResult
        {
            Reward = tile != null ? tile.Reward : RewardKind.None,
            Trap = tile != null ? tile.Trap : TrapKind.None,
        };
        if (tile == null) return r;

        switch (tile.Reward)
        {
            case RewardKind.Coin:
                // 金币砖：固定 20 + 随机 5–15（随机值由格子 effectSeed 决定）
                r.Coins = _economy.coinBagFixed
                        + EffectRand(tile.EffectSeed, _economy.coinBagRandomMin, _economy.coinBagRandomMax);
                break;
            case RewardKind.SkinShard:
                r.Shards = _economy.shardPerBrick;
                break;
            case RewardKind.Shield:
                // 护盾存在时再次踩中，转换为金币（数值在全局经济配置）
                r.ShieldConvertCoins = runState != null && runState.ShieldCount > 0;
                r.Coins = _economy.shieldReTriggerCoins;
                break;
            case RewardKind.ScoreBoost:
                r.ScoreMultAdd = JumpConfig.ScoreBoostPerStack;
                r.ScoreMultCap = JumpConfig.ScoreBoostMax;
                break;
            case RewardKind.Sprint:
                r.SprintLandIndex = tile.Index + JumpConfig.SprintSkipBlocks + 1; // 越 3 格落第 4 格
                break;
            case RewardKind.LuckyWheel:
                break; // 转盘效果由弹窗与全局奖池处理
        }

        switch (tile.Trap)
        {
            case TrapKind.Slippery:
                r.SlipperyOffset = GlobalTrapConfig.Default.SlipperyOffset; // 0.12m 固定
                r.SlipperyAngleDeg = tile.EffectSeed % 360;                  // 方向由 Seed 决定
                break;
            case TrapKind.Phantom:
                r.PhantomCollapseSeconds = GlobalTrapConfig.Default.PhantomCollapseSeconds;
                break;
            case TrapKind.SlowCharge:
                r.SlowChargeJumps = JumpConfig.SlowChargeJumps; // 再触发只刷新次数，不叠加
                break;
            case TrapKind.Burn:
                r.BurnSeconds = GlobalTrapConfig.Default.BurnSeconds;
                break;
            case TrapKind.Reverse:
                r.ReverseNext = true;
                break;
        }
        return r;
    }

    /// <summary>effectSeed → [min,max] 闭区间整数（SplitMix 混合，确定性）</summary>
    public static int EffectRand(uint seed, int minInclusive, int maxInclusive)
    {
        if (maxInclusive < minInclusive) return minInclusive;
        uint s = seed;
        s ^= 0x9E3779B9u;
        s *= 0x85EBCA6Bu;
        s ^= s >> 13;
        s *= 0xC2B2AE35u;
        s ^= s >> 16;
        int span = maxInclusive - minInclusive + 1;
        return minInclusive + (int)(s % (uint)span);
    }
}
