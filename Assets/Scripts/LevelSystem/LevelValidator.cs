using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 关卡校验器（策划案§8 发布阻断条件全集）：
/// 格数越界 / 起终点数量 / 格子重叠 / 不可达与蓄力越界（150–1250ms，含震荡与理想落点链复核）/
/// 特殊砖数量与公式不一致 / 连续陷阱 / 特殊砖禁区与上限 / 冲刺安全格不足 / 效果冲突与同格多类型 /
/// 震荡偏移一致性与孤儿偏移 / 减速覆盖窗内反向。
/// 警告项（不阻断）：预估通关时长 60–120s 之外、反向蓄力出口有效按压越界。
/// 校验独立于生成器重新推导几何——不是生成结果的自说自话。
/// </summary>
public class LevelValidator : ILevelValidator
{
    private const float ChargeEpsilonMs = 1f;   // 浮点容差
    private const float ShakeEpsilonM = 0.002f; // 2mm

    public ValidationReport Validate(GeneratedLevel level, GlobalRewardConfig rewardConfig, GlobalTrapConfig trapConfig)
    {
        var report = new ValidationReport();
        if (level?.Definition == null || level.Tiles == null || level.Tiles.Count == 0)
        {
            report.Add(ValidationSeverity.Error, "level.empty", "关卡数据为空");
            return report;
        }
        rewardConfig = rewardConfig ?? GlobalRewardConfig.Default;
        trapConfig = trapConfig ?? GlobalTrapConfig.Default;

        var def = level.Definition;
        int n = level.Tiles.Count;

        // ---- 格数与主题 ----
        if (def.GridCount < 20 || def.GridCount > 60)
            report.Add(ValidationSeverity.Error, "gridCount.range", $"gridCount={def.GridCount} 越界（20–60）");
        if (n != def.GridCount)
            report.Add(ValidationSeverity.Error, "tileCount.mismatch", $"瓦片数 {n} ≠ gridCount {def.GridCount}");
        if (def.ThemeId < 0 || def.ThemeId >= JumpConfig.LevelThemeNames.Length)
            report.Add(ValidationSeverity.Error, "theme.missing", $"themeId={def.ThemeId} 无效（0..{JumpConfig.LevelThemeNames.Length - 1}）");

        // ---- 起点 / 终点：各恰好 1 个，且位于首末 ----
        int starts = 0, ends = 0;
        for (int i = 0; i < n; i++)
        {
            if (level.Tiles[i].Kind == TileKind.Start) starts++;
            else if (level.Tiles[i].Kind == TileKind.End) ends++;
        }
        if (starts != 1 || level.Tiles[0].Kind != TileKind.Start)
            report.Add(ValidationSeverity.Error, "start.count", $"起点数量 {starts}（须恰 1 个且为首格）");
        if (ends != 1 || level.Tiles[n - 1].Kind != TileKind.End)
            report.Add(ValidationSeverity.Error, "end.count", $"终点数量 {ends}（须恰 1 个且为末格）");

        // ---- 单格单类型 / 类型与携带一致 ----
        for (int i = 0; i < n; i++)
        {
            var t = level.Tiles[i];
            bool hasReward = t.Reward != RewardKind.None;
            bool hasTrap = t.Trap != TrapKind.None;
            if (hasReward && hasTrap)
                report.Add(ValidationSeverity.Error, "tile.conflict", $"格 {i} 同格出现奖励与陷阱多种类型");
            switch (t.Kind)
            {
                case TileKind.Reward when !hasReward:
                    report.Add(ValidationSeverity.Error, "tile.kind", $"格 {i} 标记奖励但缺奖励类型"); break;
                case TileKind.Trap when !hasTrap:
                    report.Add(ValidationSeverity.Error, "tile.kind", $"格 {i} 标记陷阱但缺陷阱类型"); break;
                case TileKind.Normal when (hasReward || hasTrap):
                    report.Add(ValidationSeverity.Error, "tile.kind", $"格 {i} 标记普通却携带特殊效果"); break;
                case TileKind.Start:
                case TileKind.End:
                    if (hasReward || hasTrap)
                        report.Add(ValidationSeverity.Error, "tile.kind", $"起/终点格 {i} 不得携带特殊效果"); break;
            }
        }

        // ---- 特殊砖数量与公式一致 ----
        var prof = DifficultyProfiles.Get(def.Difficulty);
        int expectedRewards = Mathf.RoundToInt((n - 2) * prof.RewardDensity);
        int expectedTraps = Mathf.RoundToInt((n - 2) * prof.TrapDensity);
        var rewardIdx = new List<int>();
        var trapIdx = new List<int>();
        var rewardKinds = new List<RewardKind>();
        var trapKinds = new List<TrapKind>();
        for (int i = 0; i < n; i++)
        {
            if (level.Tiles[i].Kind == TileKind.Reward) { rewardIdx.Add(i); rewardKinds.Add(level.Tiles[i].Reward); }
            else if (level.Tiles[i].Kind == TileKind.Trap) { trapIdx.Add(i); trapKinds.Add(level.Tiles[i].Trap); }
        }
        if (rewardIdx.Count != expectedRewards)
            report.Add(ValidationSeverity.Error, "reward.count", $"奖励砖 {rewardIdx.Count} 个 ≠ 公式 {expectedRewards}");
        if (trapIdx.Count != expectedTraps)
            report.Add(ValidationSeverity.Error, "trap.count", $"陷阱砖 {trapIdx.Count} 个 ≠ 公式 {expectedTraps}");

        // ---- 陷阱禁区 / 连续 / 上限 ----
        for (int k = 0; k < trapIdx.Count; k++)
        {
            int i = trapIdx[k];
            if (i < trapConfig.FirstTrapIndex)
                report.Add(ValidationSeverity.Error, "trap.zone", $"陷阱在格 {i}（前 {trapConfig.FirstSafeJumpables} 个可跳格禁区）");
            if (i > n - 1 - trapConfig.PreEndTrapFree - 1)
                report.Add(ValidationSeverity.Error, "trap.zone", $"陷阱在格 {i}（终点前 {trapConfig.PreEndTrapFree} 格禁区）");
            if (k > 0 && i - trapIdx[k - 1] <= 1)
                report.Add(ValidationSeverity.Error, "trap.consecutive", $"格 {trapIdx[k - 1]} 与 {i} 陷阱连续");
        }
        if (CountKind(trapKinds, TrapKind.Phantom) > trapConfig.CapPhantom)
            report.Add(ValidationSeverity.Error, "trap.cap", $"幻影超过每关上限 {trapConfig.CapPhantom}");
        if (CountKind(trapKinds, TrapKind.Reverse) > trapConfig.CapReverse)
            report.Add(ValidationSeverity.Error, "trap.cap", $"反向超过每关上限 {trapConfig.CapReverse}");
        if (CountKind(trapKinds, TrapKind.Seal) > trapConfig.CapSeal)
            report.Add(ValidationSeverity.Error, "trap.cap", $"封印超过每关上限 {trapConfig.CapSeal}");

        // ---- 奖励间隔 / 起手区 / 上限 ----
        for (int k = 0; k < rewardIdx.Count; k++)
        {
            int i = rewardIdx[k];
            if (i < rewardConfig.FirstRewardIndex)
                report.Add(ValidationSeverity.Error, "reward.zone", $"奖励在格 {i}（起手教学区）");
            if (i >= n - 1)
                report.Add(ValidationSeverity.Error, "reward.zone", $"奖励在格 {i}（终点格）");
            if (k > 0 && i - rewardIdx[k - 1] < rewardConfig.MinRewardIndexGap)
                report.Add(ValidationSeverity.Error, "reward.gap", $"奖励格 {rewardIdx[k - 1]} 与 {i} 间隔不足（至少隔 {rewardConfig.MinRewardIndexGap - 1} 格）");
        }
        if (CountReward(rewardKinds, RewardKind.LuckyWheel) > rewardConfig.CapLuckyWheel)
            report.Add(ValidationSeverity.Error, "reward.cap", $"幸运转盘超过每关上限 {rewardConfig.CapLuckyWheel}");
        if (CountReward(rewardKinds, RewardKind.SkinShard) > rewardConfig.CapSkinShard)
            report.Add(ValidationSeverity.Error, "reward.cap", $"皮肤碎片超过每关上限 {rewardConfig.CapSkinShard}");
        if (CountReward(rewardKinds, RewardKind.Shield) > rewardConfig.CapShield)
            report.Add(ValidationSeverity.Error, "reward.cap", $"护盾超过每关上限 {rewardConfig.CapShield}");
        if (CountReward(rewardKinds, RewardKind.Sprint) > rewardConfig.CapSprint)
            report.Add(ValidationSeverity.Error, "reward.cap", $"冲刺超过每关上限 {rewardConfig.CapSprint}");

        // ---- 冲刺：终点前 5 格禁放；后 4 格必须全部为普通安全格 ----
        for (int k = 0; k < rewardIdx.Count; k++)
        {
            if (rewardKinds[k] != RewardKind.Sprint) continue;
            int s = rewardIdx[k];
            if (s > n - 1 - trapConfig.SprintPreEndExclusion - 1)
                report.Add(ValidationSeverity.Error, "sprint.zone", $"冲刺在格 {s}（终点前 {trapConfig.SprintPreEndExclusion} 格内禁放）");
            if (s + trapConfig.SprintTailSafeTiles > n - 1)
                report.Add(ValidationSeverity.Error, "sprint.tail", $"冲刺在格 {s}（后 {trapConfig.SprintTailSafeTiles} 格越出关卡）");
            for (int g = s + 1; g <= s + trapConfig.SprintTailSafeTiles && g < n; g++)
                if (level.Tiles[g].Kind != TileKind.Normal)
                    report.Add(ValidationSeverity.Error, "sprint.tail", $"冲刺格 {s} 之后的格 {g} 不是普通安全格");
        }

        // ---- 减速覆盖窗内不得有反向蓄力砖 ----
        for (int k = 0; k < trapIdx.Count; k++)
        {
            if (trapKinds[k] != TrapKind.SlowCharge) continue;
            int i = trapIdx[k];
            for (int g = i + 1; g <= i + trapConfig.SlowCoverReverseBan && g < n; g++)
                if (level.Tiles[g].Trap == TrapKind.Reverse)
                    report.Add(ValidationSeverity.Error, "slow.reverse", $"减速格 {i} 覆盖窗内的格 {g} 有反向蓄力砖");
        }

        // ---- 格子重叠（全对检查） ----
        for (int i = 0; i < n; i++)
        {
            for (int j = i + 1; j < n; j++)
            {
                var a = level.Tiles[i]; var b = level.Tiles[j];
                float dx = Mathf.Abs(a.X - b.X);
                float dz = Mathf.Abs(a.Z - b.Z);
                float sumHalf = (a.Size + b.Size) * 0.5f;
                if (dx < sumHalf && dz < sumHalf)
                    report.Add(ValidationSeverity.Error, "tile.overlap", $"格 {i} 与 {j} 重叠（dx={dx:F2} dz={dz:F2} 半宽和={sumHalf:F2}）");
            }
        }

        // ---- 可达性与蓄力区间（独立重推理想落点链；含震荡一致性复核） ----
        float standX = level.Tiles[0].X, standZ = level.Tiles[0].Z;
        for (int j = 0; j < n - 1; j++)
        {
            var to = level.Tiles[j + 1];
            var axis = DirOf(to.ArrivalAxis);
            float dX = to.X - standX, dZ = to.Z - standZ;
            float d = dX * axis.x + dZ * axis.y;             // 行进轴投影 = 本跳轴心距
            float latX = dX - axis.x * d, latZ = dZ - axis.y * d; // 横向分量
            var lateral = DirOf(Other(to.ArrivalAxis));
            float signed = latX * lateral.x + latZ * lateral.y;

            // 震荡偏移一致性：横向分量必须与烘焙 shakeOffset 吻合，且只有上一格为震荡陷阱时允许非零
            if (Mathf.Abs(signed - to.ShakeOffset) > ShakeEpsilonM)
                report.Add(ValidationSeverity.Error, "shake.mismatch", $"格 {j + 1} 横向偏移 {signed:F3}m ≠ 烘焙 {to.ShakeOffset:F3}m");
            if (Mathf.Abs(to.ShakeOffset) > ShakeEpsilonM && level.Tiles[j].Trap != TrapKind.Shake)
                report.Add(ValidationSeverity.Error, "shake.orphan", $"格 {j + 1} 带横移 {to.ShakeOffset:F3}m 但上一格不是震荡陷阱");

            // 理论所需蓄力 150–1250ms
            float charge = LevelMath.ChargeMsAtDist(d);
            if (charge < LevelMath.MinChargeMs - ChargeEpsilonMs || charge > LevelMath.MaxChargeMs + ChargeEpsilonMs)
                report.Add(ValidationSeverity.Error, "charge.range",
                    $"跳 {j}→{j + 1} 轴心距 {d:F2}m 所需蓄力 {charge:F0}ms 越界（150–1250ms）");

            // 反向蓄力出口：离开反向格的按压口径（qReverse=1-q）
            if (level.Tiles[j].Trap == TrapKind.Reverse)
            {
                float hold = LevelMath.ChargeMsReversedAtDist(d);
                if (hold < LevelMath.MinChargeMs || hold > LevelMath.MaxChargeMs)
                    report.Add(ValidationSeverity.Warning, "reverse.hold",
                        $"反向格 {j} 出口有效按压 {hold:F0}ms 超出 150–1250ms（体验风险）");
            }

            standX += axis.x * d; // 理想落点链前进（横向分量不入链——与引擎落点判定口径一致）
            standZ += axis.y * d;
        }

        // ---- 通关时长警告 ----
        if (level.PredictedClearSeconds > 0f)
        {
            if (level.PredictedClearSeconds < 60f)
                report.Add(ValidationSeverity.Warning, "time.short", $"预估通关 {level.PredictedClearSeconds:F0}s（<60s）");
            else if (level.PredictedClearSeconds > 120f)
                report.Add(ValidationSeverity.Warning, "time.long", $"预估通关 {level.PredictedClearSeconds:F0}s（>120s）");
        }
        return report;
    }

    private static int CountKind(List<TrapKind> list, TrapKind kind)
    {
        int c = 0;
        for (int i = 0; i < list.Count; i++) if (list[i] == kind) c++;
        return c;
    }

    private static int CountReward(List<RewardKind> list, RewardKind kind)
    {
        int c = 0;
        for (int i = 0; i < list.Count; i++) if (list[i] == kind) c++;
        return c;
    }

    private static Vector2 DirOf(TileAxis axis) => axis == TileAxis.X ? Vector2.right : Vector2.up;

    private static TileAxis Other(TileAxis axis) => axis == TileAxis.X ? TileAxis.Z : TileAxis.X;
}
