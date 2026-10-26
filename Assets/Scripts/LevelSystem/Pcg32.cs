using System;
using System.Collections.Generic;

/// <summary>
/// PCG32 确定性随机数生成器（O'Neill PCG-XSH-RR 32 位输出）。
/// 生成算法专用：与 UnityEngine.Random / System.Random 完全隔离，
/// 纯整数与 IEEE754 双精度运算，跨平台/跨会话字节级一致。
/// 初始化：state/inc 由 (levelId + seed + generatorVersion) 稳定哈希得到。
/// </summary>
public sealed class Pcg32
{
    private ulong _state;
    private readonly ulong _inc;

    public Pcg32(ulong seed, ulong stream = 0ul)
    {
        _state = 0ul;
        _inc = (stream << 1) | 1ul; // inc 必须为奇数
        NextUInt();
        _state += seed;
        NextUInt();
    }

    /// <summary>32 位原始输出</summary>
    public uint NextUInt()
    {
        ulong old = _state;
        _state = old * 6364136223846793005ul + _inc;
        uint xorshifted = (uint)(((old >> 18) ^ old) >> 27);
        uint rot = (uint)(old >> 59);
        return (xorshifted >> (int)rot) | (xorshifted << (int)((32 - rot) & 31));
    }

    /// <summary>64 位（两次 32 位拼接）</summary>
    public ulong NextULong()
    {
        return ((ulong)NextUInt() << 32) | NextUInt();
    }

    /// <summary>[0,1) 双精度</summary>
    public double NextDouble()
    {
        return NextUInt() * (1.0 / 4294967296.0);
    }

    /// <summary>[min,max) 半开区间整数</summary>
    public int Range(int minInclusive, int maxExclusive)
    {
        long span = (long)maxExclusive - minInclusive;
        if (span <= 0) return minInclusive;
        return (int)(minInclusive + (long)(NextDouble() * span));
    }

    /// <summary>[min,max] 闭区间浮点</summary>
    public float Range(float minInclusive, float maxInclusive)
    {
        return (float)(minInclusive + NextDouble() * (maxInclusive - minInclusive));
    }

    /// <summary>按 int 权重表抽取索引（权重全 0 返回 -1）</summary>
    public int WeightedIndex(int[] weights)
    {
        long total = 0;
        for (int i = 0; i < weights.Length; i++) total += weights[i];
        if (total <= 0) return -1;
        long r = (long)(NextDouble() * total); // [0,total)
        for (int i = 0; i < weights.Length; i++)
        {
            r -= weights[i];
            if (r < 0) return i;
        }
        return weights.Length - 1;
    }

    /// <summary>Fisher-Yates 洗牌（确定性）</summary>
    public void Shuffle<T>(IList<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    // ---------- 稳定哈希（生成器初始化用；与平台无关） ----------

    /// <summary>FNV-1a 64 位（跨平台稳定的字符串哈希）</summary>
    public static ulong StableHash(string text)
    {
        ulong h = 14695981039346656037ul;
        if (string.IsNullOrEmpty(text)) return h;
        for (int i = 0; i < text.Length; i++)
        {
            h ^= (byte)(text[i] & 0xFF);
            h *= 1099511628211ul;
            h ^= (byte)((text[i] >> 8) & 0xFF);
            h *= 1099511628211ul;
        }
        return h;
    }

    /// <summary>策划案：以 levelId + seed + generatorVersion 初始化生成流</summary>
    public static ulong StreamSeed(int levelId, uint seed, string generatorVersion)
    {
        ulong h = StableHash("Game3D001.LevelGen/" + generatorVersion);
        h ^= (ulong)(uint)levelId * 0x9E3779B97F4A7C15ul;
        h = (h << 31) | (h >> 33);
        h ^= (ulong)seed * 0xBF58476D1CE4E5B9ul;
        h ^= h >> 29;
        h *= 0x94D049BB133111EBul;
        h ^= h >> 32;
        return h;
    }
}
