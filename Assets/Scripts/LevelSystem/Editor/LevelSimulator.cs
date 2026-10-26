using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 玩家误差模拟器（策划案：编辑器侧"玩家误差模拟"与发布流程 1000 次模拟步骤）：
/// 同一误差模型跑全部档位——难度差异完全来自关卡几何（轴距/方块尺寸/转向/陷阱），
/// 误差参数不感知档位。引擎口径对齐 JumpGame：
/// - 落点判定只看行进轴投影；跳太短落回原块容错存活并重试本跳。
/// - 蓄力 power = 按压时长/(MaxCharge×减速倍率)；距离 = Lerp(MinJumpDist, MaxJumpDist, power)。
/// - 打滑：0.12m 定向滑移并钳制块内；护盾：坠落自动复活回所站块顶心。
/// - 幻影 1.5s / 燃烧 2.5s：落地反应 + 迟疑 + 蓄力超过倒计时即坠落；压力下按压误差放大。
/// </summary>
public class LevelSimulator
{
    // ---------- 玩家误差模型（档位无关；数值经五档目标通关率校准） ----------
    /// <summary>蓄力按压时长误差 σ（毫秒）</summary>
    public float SigmaHoldMs = 62f;
    /// <summary>每跳大失误概率（随机方向偏 0.3–1.2m）</summary>
    public float FumbleChance = 0.004f;
    /// <summary>落地迟疑概率（幻影/燃烧倒计时内可能致死）</summary>
    public float HesitateChance = 0.08f;
    public float HesitateMinSeconds = 0.4f;
    public float HesitateMaxSeconds = 2.2f;
    /// <summary>基础落地反应（秒）</summary>
    public float ReactionSeconds = 0.25f;
    /// <summary>倒计时压力下的按压误差放大</summary>
    public float PressureSigmaMult = 1.5f;

    public int DefaultRuns = 1000;

    public struct SimResult
    {
        public int Runs;
        public int Clears;
        public float ClearRate;
        public float AvgMaxIndex;                 // 平均最远到达格
        public Dictionary<string, int> DeathCauses; // fall / phantom / burn
    }

    /// <summary>模拟首尝通关率（seedBase 保证可复现）</summary>
    public SimResult Simulate(GeneratedLevel level, int runs, ulong seedBase)
    {
        var result = new SimResult
        {
            Runs = Mathf.Max(1, runs),
            DeathCauses = new Dictionary<string, int>(),
        };
        int n = level.Tiles.Count;
        var tiles = level.Tiles;
        long sumMaxIndex = 0;
        var rng = new Pcg32(seedBase ^ Pcg32.StableHash("sim/" + level.Definition.LevelId));

        for (int run = 0; run < result.Runs; run++)
        {
            int maxIndex = 0;
            int cur = 0;
            float standX = tiles[0].X, standZ = tiles[0].Z;
            int shield = 0;
            int slowLeft = 0;
            bool reverseNext = false;
            string death = null;

            int guard = 0;
            while (cur < n - 1 && death == null && guard++ < 4000)
            {
                var target = tiles[cur + 1];
                var axis = DirOf(target.ArrivalAxis);
                float d = (target.X - standX) * axis.x + (target.Z - standZ) * axis.y;
                float powerTarget = Mathf.InverseLerp(JumpConfig.MinJumpDist, JumpConfig.MaxJumpDist, d);
                bool slow = slowLeft > 0;
                if (slow) slowLeft--;
                bool reverse = reverseNext;
                reverseNext = false;

                // 需要按压的时长（反向 = 1-power；减速 ×2）
                float holdNeeded = (reverse ? 1f - powerTarget : powerTarget)
                                 * JumpConfig.MaxChargeSeconds * 1000f * (slow ? JumpConfig.SlowChargeFactor : 1f);

                // 幻影/燃烧压力：本跳必须在倒计时内完成
                float deadline = 0f;
                if (tiles[cur].Trap == TrapKind.Phantom) deadline = GlobalTrapConfig.Default.PhantomCollapseSeconds;
                else if (tiles[cur].Trap == TrapKind.Burn) deadline = GlobalTrapConfig.Default.BurnSeconds;
                float sigma = SigmaHoldMs * (deadline > 0f ? PressureSigmaMult : 1f);

                // 迟疑 + 反应 + 蓄力是否超时
                if (deadline > 0f)
                {
                    float hesitate = rng.NextDouble() < HesitateChance
                        ? rng.Range(HesitateMinSeconds, HesitateMaxSeconds) : 0f;
                    float leaveAt = ReactionSeconds + hesitate + holdNeeded * 0.001f;
                    if (leaveAt > deadline)
                    {
                        death = tiles[cur].Trap == TrapKind.Phantom ? "phantom" : "burn";
                        break;
                    }
                }

                // 按压误差 → 实际距离
                float holdErr = (float)Gaussian(rng) * sigma;
                if (rng.NextDouble() < FumbleChance)
                    holdErr += (rng.NextDouble() < 0.5 ? -1f : 1f) * rng.Range(0.3f, 1.2f)
                              / (5.4f / (JumpConfig.MaxChargeSeconds * 1000f)) / (slow ? JumpConfig.SlowChargeFactor : 1f);
                float powerScale = JumpConfig.MaxChargeSeconds * 1000f * (slow ? JumpConfig.SlowChargeFactor : 1f);
                float powerActual = Mathf.Repeat(Mathf.Max(0f, holdNeeded + holdErr) / powerScale, 1f);
                float dist = Mathf.Lerp(JumpConfig.MinJumpDist, JumpConfig.MaxJumpDist,
                    reverse ? 1f - powerActual : powerActual);

                // 落点（行进轴口径，与引擎一致）
                float offNext = dist - d; // 相对目标块心的轴向偏移
                bool onNext = Mathf.Abs(offNext) <= target.Size * 0.5f;
                if (onNext)
                {
                    standX += axis.x * dist;
                    standZ += axis.y * dist;
                    cur++;
                    maxIndex = Mathf.Max(maxIndex, cur);

                    var landed = tiles[cur];
                    // 奖励：护盾/冲刺影响生存；其余无影响
                    switch (landed.Reward)
                    {
                        case RewardKind.Shield:
                            if (shield < 1) shield++;
                            break;
                        case RewardKind.Sprint:
                            int land = cur + JumpConfig.SprintSkipBlocks + 1;
                            if (land <= n - 1)
                            {
                                cur = land;
                                maxIndex = Mathf.Max(maxIndex, cur);
                                standX = tiles[cur].X;
                                standZ = tiles[cur].Z;
                            }
                            break;
                    }
                    // 陷阱
                    switch (landed.Trap)
                    {
                        case TrapKind.Slippery:
                            float angle = (landed.EffectSeed % 360) * Mathf.Deg2Rad;
                            var dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                            float lim = Mathf.Max(0.1f, landed.Size * 0.5f - 0.15f);
                            float nx = Mathf.Clamp((standX - landed.X) + dir.x * GlobalTrapConfig.Default.SlipperyOffset, -lim, lim);
                            float nz = Mathf.Clamp((standZ - landed.Z) + dir.y * GlobalTrapConfig.Default.SlipperyOffset, -lim, lim);
                            standX = landed.X + nx;
                            standZ = landed.Z + nz;
                            break;
                        case TrapKind.SlowCharge:
                            slowLeft = JumpConfig.SlowChargeJumps; // 刷新为 3 次，不叠加
                            break;
                        case TrapKind.Reverse:
                            reverseNext = true;
                            break;
                    }
                }
                else
                {
                    // 落回原块容错：重试本跳（引擎同款双块判定）
                    float offCur = (standX - tiles[cur].X) * axis.x + (standZ - tiles[cur].Z) * axis.y + dist;
                    bool onCur = Mathf.Abs(offCur) <= tiles[cur].Size * 0.5f;
                    if (onCur)
                    {
                        standX += axis.x * dist;
                        standZ += axis.y * dist;
                        continue;
                    }
                    if (shield > 0)
                    {
                        shield--;
                        standX = tiles[cur].X;
                        standZ = tiles[cur].Z;
                        continue;
                    }
                    death = "fall";
                }
            }

            if (death != null)
            {
                int c; result.DeathCauses.TryGetValue(death, out c);
                result.DeathCauses[death] = c + 1;
            }
            else if (cur >= n - 1) result.Clears++;
            sumMaxIndex += maxIndex;
        }

        result.ClearRate = (float)result.Clears / result.Runs;
        result.AvgMaxIndex = (float)sumMaxIndex / result.Runs;
        return result;
    }

    /// <summary>Box-Muller 标准正态（确定性）</summary>
    private static double Gaussian(Pcg32 rng)
    {
        double u1 = 1.0 - rng.NextDouble(); // (0,1]
        double u2 = rng.NextDouble();
        return Mathf.Sqrt(-2f * Mathf.Log((float)u1)) * Mathf.Sin(2f * Mathf.PI * (float)u2);
    }

    private static Vector2 DirOf(TileAxis axis) => axis == TileAxis.X ? Vector2.right : Vector2.up;
}
