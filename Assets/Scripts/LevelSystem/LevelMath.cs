using UnityEngine;

/// <summary>
/// 关卡数学：蓄力毫秒 ↔ 跳跃距离的换算（与 JumpConfig 手感曲线严格一致）。
/// 引擎曲线：power = 蓄力秒 / MaxChargeSeconds；dist = Lerp(MinJumpDist, MaxJumpDist, power)。
/// 策划案约束：任意跳跃理论所需蓄力必须处于 150–1250ms。
/// 校验/生成/模拟共用本表，保证"理论所需蓄力"口径唯一。
/// </summary>
public static class LevelMath
{
    /// <summary>理论所需蓄力下限（毫秒）</summary>
    public const float MinChargeMs = 150f;
    /// <summary>理论所需蓄力上限（毫秒）</summary>
    public const float MaxChargeMs = 1250f;

    /// <summary>所需蓄力（毫秒）→ 跳跃距离（米，沿行进轴）</summary>
    public static float DistAtChargeMs(float chargeMs)
    {
        float power = Mathf.Clamp01(chargeMs / (JumpConfig.MaxChargeSeconds * 1000f));
        return Mathf.Lerp(JumpConfig.MinJumpDist, JumpConfig.MaxJumpDist, power);
    }

    /// <summary>跳跃距离（米）→ 所需蓄力（毫秒；超程曲线范围被钳制）</summary>
    public static float ChargeMsAtDist(float dist)
    {
        float t = Mathf.InverseLerp(JumpConfig.MinJumpDist, JumpConfig.MaxJumpDist, dist);
        return t * JumpConfig.MaxChargeSeconds * 1000f;
    }

    /// <summary>距离下限（对应 150ms）</summary>
    public static float MinReachDist => DistAtChargeMs(MinChargeMs);   // 2.20m
    /// <summary>距离上限（对应 1250ms）</summary>
    public static float MaxReachDist => DistAtChargeMs(MaxChargeMs);   // 6.60m

    /// <summary>把轴心距钳制进可达区间（生成器内联保证；校验器独立复核）</summary>
    public static float ClampReach(float dist)
        => Mathf.Clamp(dist, MinReachDist, MaxReachDist);

    /// <summary>米 → 毫米整数（四舍五入确定性；layoutHash 与烘焙共用同一口径）</summary>
    public static int ToMm(float meters)
        => (int)Mathf.Floor(meters * 1000f + 0.5f);

    /// <summary>毫米整数 → 米</summary>
    public static float FromMm(int mm)
        => mm * 0.001f;

    /// <summary>轴心距对应的滞空秒（与 JumpGame 跳跃演出一致：Base + PerPower×power）</summary>
    public static float AirTimeAtDist(float dist)
        => JumpConfig.AirTimeBase + JumpConfig.AirTimePerPower
           * Mathf.InverseLerp(JumpConfig.MinJumpDist, JumpConfig.MaxJumpDist, dist);

    /// <summary>反向蓄力格的起跳口径：以 qReverse = 1 - q 抵达目标距离，所需按压时长换算</summary>
    public static float ChargeMsReversedAtDist(float dist)
    {
        float power = Mathf.InverseLerp(JumpConfig.MinJumpDist, JumpConfig.MaxJumpDist, dist);
        return ChargeMsAtDistFromPower(1f - power);
    }

    private static float ChargeMsAtDistFromPower(float power)
        => Mathf.Clamp01(power) * JumpConfig.MaxChargeSeconds * 1000f;
}
