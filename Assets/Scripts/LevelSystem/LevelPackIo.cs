using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

/// <summary>
/// 关卡包 IO：领域模型 ↔ JSON DTO（JsonUtility 兼容）、layoutHash 与 SHA-256。
/// 纯转换层，不碰文件系统——草稿读写与发布落盘由编辑器管线（AssetDatabase）完成，
/// 运行时装载经 DataMgr.LoadConfigAsync（GameRes 模拟/AB 同构）。
/// 发布产物：pack_{version}.json + pack_{version}.sha256.txt + pack_index.json（全包原子替换，旧包保留供回滚）。
/// </summary>
public class LevelPackIo : ILevelPackExporter
{
    /// <summary>关卡包结构版本（不支持的 schemaVersion 阻断开局并提示更新）</summary>
    public const int SchemaVersion = 1;

    public const string PackResDir = "Config/Levels/Pack";   // GameRes 内目录
    public const string DraftResDir = "Config/Levels/Drafts";
    public static string PackAssetKey(string version)
        => $"{PackResDir}/pack_{(version ?? "0").Replace('.', '_')}";
    public static string IndexAssetKey => $"{PackResDir}/pack_index";

    // ---------- 导出 ----------

    public LevelPack Export(IReadOnlyList<GeneratedLevel> levels, string packVersion)
    {
        if (levels == null || levels.Count == 0) return null;
        var data = new LevelPackData
        {
            schemaVersion = SchemaVersion,
            packVersion = packVersion ?? "",
            buildTimeUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            levelCount = levels.Count,
            levels = new PackedLevelData[levels.Count],
        };
        var packedLevels = new List<PackedLevelData>(levels.Count);
        for (int i = 0; i < levels.Count; i++)
        {
            data.levels[i] = ToPackedLevel(levels[i]);
            packedLevels.Add(data.levels[i]);
        }

        var pack = new LevelPack
        {
            Data = data,
            Version = data.packVersion,
            SchemaVersion = data.schemaVersion,
        };
        // 运行序列：包内按（章节, 章节内顺序）排列，仅启用关卡
        packedLevels.Sort((a, b) =>
        {
            int c = a.chapterId.CompareTo(b.chapterId);
            return c != 0 ? c : a.order.CompareTo(b.order);
        });
        foreach (var d in packedLevels)
        {
            if (!d.enabled) continue;
            var level = ToGeneratedLevel(d);
            if (level != null) pack.Levels.Add(level);
        }
        pack.Sha256 = ComputeSha256(ToJson(data));
        return pack;
    }

    // ---------- 领域 ↔ DTO ----------

    public static PackedLevelData ToPackedLevel(GeneratedLevel level)
    {
        var def = level.Definition;
        var tiles = level.Tiles;
        var d = new PackedLevelData
        {
            levelId = def.LevelId,
            chapterId = def.ChapterId,
            order = def.OrderInChapter,
            enabled = def.Enabled,
            difficultyTier = (int)def.Difficulty,
            themeId = def.ThemeId,
            seed = unchecked((long)def.Seed),
            gridCount = def.GridCount,
            generatorVersion = level.GeneratorVersion ?? "",
            layoutHash = level.LayoutHash ?? "",
            tileCount = level.TileCount,
            tiles = new PackedTileData[tiles.Count],
        };
        for (int i = 0; i < tiles.Count; i++)
        {
            var t = tiles[i];
            d.tiles[i] = new PackedTileData
            {
                index = t.Index,
                kind = (int)t.Kind,
                arrivalAxis = (int)t.ArrivalAxis,
                x = LevelMath.ToMm(t.X),
                z = LevelMath.ToMm(t.Z),
                size = LevelMath.ToMm(t.Size),
                rewardKind = (int)t.Reward,
                trapKind = (int)t.Trap,
                effectSeed = unchecked((long)t.EffectSeed),
                shakeOffset = LevelMath.ToMm(t.ShakeOffset),
            };
        }
        return d;
    }

    /// <summary>烘焙 DTO → 运行时领域模型（运行时不重新生成正式关卡，只读烘焙路线）</summary>
    public static GeneratedLevel ToGeneratedLevel(PackedLevelData d)
    {
        if (d == null || d.tiles == null || d.tiles.Length == 0) return null;
        var level = new GeneratedLevel
        {
            Definition = new LevelDefinition
            {
                LevelId = d.levelId,
                ChapterId = d.chapterId,
                OrderInChapter = d.order,
                GridCount = d.gridCount,
                Difficulty = (DifficultyTier)d.difficultyTier,
                Seed = unchecked((uint)d.seed),
                ThemeId = d.themeId,
                Enabled = d.enabled,
            },
            GeneratorVersion = d.generatorVersion,
            TileCount = d.tileCount,
            LayoutHash = d.layoutHash,
        };
        level.Tiles.Capacity = d.tiles.Length;
        foreach (var t in d.tiles)
        {
            level.Tiles.Add(new GeneratedTile
            {
                Index = t.index,
                Kind = (TileKind)t.kind,
                ArrivalAxis = (TileAxis)t.arrivalAxis,
                X = LevelMath.FromMm(t.x),
                Z = LevelMath.FromMm(t.z),
                Size = LevelMath.FromMm(t.size),
                Reward = (RewardKind)t.rewardKind,
                Trap = (TrapKind)t.trapKind,
                EffectSeed = unchecked((uint)t.effectSeed),
                ShakeOffset = LevelMath.FromMm(t.shakeOffset),
            });
        }
        return level;
    }

    // ---------- 哈希 ----------

    /// <summary>layoutHash：对生成关卡（烘焙口径：毫米整数）做 SHA-256</summary>
    public static string ComputeLayoutHash(GeneratedLevel level)
    {
        if (level?.Tiles == null || level.Tiles.Count == 0) return "";
        var sb = new StringBuilder(256);
        AppendLayoutHeader(sb, level.GeneratorVersion, level.Definition.LevelId, level.TileCount);
        for (int i = 0; i < level.Tiles.Count; i++)
        {
            var t = level.Tiles[i];
            AppendLayoutTile(sb,
                t.Index, (int)t.Kind, (int)t.ArrivalAxis,
                LevelMath.ToMm(t.X), LevelMath.ToMm(t.Z), LevelMath.ToMm(t.Size),
                (int)t.Reward, (int)t.Trap, unchecked((long)t.EffectSeed),
                LevelMath.ToMm(t.ShakeOffset));
        }
        return Sha256Hex(sb.ToString());
    }

    /// <summary>layoutHash：对烘焙 DTO（重导入路径）重算——与生成路径字节一致</summary>
    public static string ComputeLayoutHashFromPacked(PackedLevelData d)
    {
        if (d?.tiles == null || d.tiles.Length == 0) return "";
        var sb = new StringBuilder(256);
        AppendLayoutHeader(sb, d.generatorVersion, d.levelId, d.tileCount);
        for (int i = 0; i < d.tiles.Length; i++)
        {
            var t = d.tiles[i];
            AppendLayoutTile(sb, t.index, t.kind, t.arrivalAxis, t.x, t.z, t.size,
                t.rewardKind, t.trapKind, t.effectSeed, t.shakeOffset);
        }
        return Sha256Hex(sb.ToString());
    }

    /// <summary>烘焙关卡完整性校验（运行时装载 / 发布重导入共用）</summary>
    public static bool VerifyPackedLevel(PackedLevelData d)
        => !string.IsNullOrEmpty(d.layoutHash)
           && ComputeLayoutHashFromPacked(d) == d.layoutHash;

    private static void AppendLayoutHeader(StringBuilder sb, string version, int levelId, int tileCount)
    {
        sb.Append(version ?? "").Append('|').Append(levelId).Append('|').Append(tileCount).Append('|');
    }

    private static void AppendLayoutTile(StringBuilder sb, int index, int kind, int axis,
        int x, int z, int size, int reward, int trap, long effectSeed, int shakeOffset)
    {
        sb.Append(index).Append(',').Append(kind).Append(',').Append(axis).Append(',')
          .Append(x).Append(',').Append(z).Append(',').Append(size).Append(',')
          .Append(reward).Append(',').Append(trap).Append(',')
          .Append(effectSeed).Append(',').Append(shakeOffset).Append(';');
    }

    /// <summary>SHA-256 十六进制（layoutHash 专用口径）</summary>
    private static string Sha256Hex(string content) => ComputeSha256(content);

    // ---------- SHA-256 ----------

    /// <summary>内容 SHA-256（十六进制小写；平台不支持时抛异常由调用方兜底提示）</summary>
    public static string ComputeSha256(string content)
    {
        if (content == null) return "";
        using (var sha = SHA256.Create())
        {
            byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(content));
            var sb = new StringBuilder(hash.Length * 2);
            for (int i = 0; i < hash.Length; i++) sb.Append(hash[i].ToString("x2"));
            return sb.ToString();
        }
    }

    /// <summary>安全版 SHA-256：平台摘要实现缺席时返回 "unavailable"（游戏不因校验缺席而崩溃）</summary>
    public static string TryComputeSha256(string content)
    {
        try { return ComputeSha256(content); }
        catch (Exception) { return "unavailable"; }
    }

    // ---------- JSON ----------

    public static string ToJson(LevelPackData pack) => JsonUtility.ToJson(pack);
    public static string ToJson(PackedLevelData level) => JsonUtility.ToJson(level);
    public static string ToJson(LevelDefinitionData def) => JsonUtility.ToJson(def);
    public static string ToJson(PackIndexData index) => JsonUtility.ToJson(index);

    public static T FromJson<T>(string json) where T : class
        => string.IsNullOrEmpty(json) ? null : JsonUtility.FromJson<T>(json);

    // ---------- DTO ↔ 领域 ----------

    public static LevelDefinitionData ToDefinitionData(LevelDefinition def)
        => new LevelDefinitionData
        {
            levelId = def.LevelId,
            chapterId = def.ChapterId,
            order = def.OrderInChapter,
            gridCount = def.GridCount,
            difficultyTier = (int)def.Difficulty,
            seed = unchecked((long)def.Seed),
            themeId = def.ThemeId,
            enabled = def.Enabled,
        };

    public static LevelDefinition ToDefinition(LevelDefinitionData d)
        => new LevelDefinition
        {
            LevelId = d.levelId,
            ChapterId = d.chapterId,
            OrderInChapter = d.order,
            GridCount = d.gridCount,
            Difficulty = (DifficultyTier)d.difficultyTier,
            Seed = unchecked((uint)d.seed),
            ThemeId = d.themeId,
            Enabled = d.enabled,
        };
}
