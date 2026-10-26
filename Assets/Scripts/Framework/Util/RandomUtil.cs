using System;
using System.Collections.Generic;

/// <summary>随机工具（独立于 UnityEngine.Random，可设种子复现测试）</summary>
public static class RandomUtil
{
    private static Random _rng = new Random();

    /// <summary>重置随机种子（可复现测试）</summary>
    public static void SetSeed(int seed) => _rng = new Random(seed);

    public static int Range(int minInclusive, int maxExclusive) => _rng.Next(minInclusive, maxExclusive);

    public static float Range(float minInclusive, float maxInclusive)
        => (float)(_rng.NextDouble() * (maxInclusive - minInclusive)) + minInclusive;

    /// <summary>百分之概率（0~100）</summary>
    public static bool Chance(int percent) => _rng.Next(0, 100) < percent;

    /// <summary>0~1 概率</summary>
    public static bool Chance01(float probability) => _rng.NextDouble() < probability;

    public static T Pick<T>(IReadOnlyList<T> list)
        => (list != null && list.Count > 0) ? list[_rng.Next(list.Count)] : default;

    /// <summary>Fisher-Yates 原地洗牌</summary>
    public static void Shuffle<T>(List<T> list)
    {
        if (list == null || list.Count < 2) return;
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = _rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
