using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

/// <summary>
/// 关卡包 IO：领域模型 ↔ JSON DTO（JsonUtility 兼容）、layoutHash 与 SHA-256。
/// 纯转换层，不碰文件系统——草稿读写与发布落盘由编辑器管线（AssetDatabase）完成，
/// 运行时装载经 DataMgr.LoadConfigAsync（GameRes 模拟/AB 同构）。
/// 发布产物：pack_{version}.json + pack_{version}.sha256.txt + pack_index.json（原子写入；仅 schema 2 包可回滚）。
/// </summary>
public class LevelPackIo : ILevelPackExporter
{
    /// <summary>关卡包结构版本（不支持的 schemaVersion 阻断开局并提示更新）</summary>
    public const int SchemaVersion = 2;

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
            specialRules = Clone(level.Definition.SpecialRules ?? DifficultyProfiles.DefaultRules(def.Difficulty)),
            overrides = Clone(def.Overrides ?? new SpecialTileOverride[0]),
            effects = level.Effects.Snapshot(),
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
                effectLevelId = t.EffectLevelId,
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
                SpecialRules = Clone(d.specialRules),
                Overrides = Clone(d.overrides ?? new SpecialTileOverride[0]),
            },
            GeneratorVersion = d.generatorVersion,
            TileCount = d.tileCount,
            Effects = d.effects?.Snapshot(),
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
                EffectLevelId = t.effectLevelId,
            });
        }
        return level;
    }

    // ---------- 哈希 ----------

    /// <summary>layoutHash：对生成关卡（烘焙口径：毫米整数）做 SHA-256</summary>
    public static string ComputeLayoutHash(GeneratedLevel level)
        => level == null ? "" : ComputeLayoutHashFromPacked(ToPackedLevel(level));

    public static string ComputeLayoutHashFromPacked(PackedLevelData d)
    {
        if (d?.tiles == null || d.tiles.Length == 0 || d.effects == null) return "";
        // Hash every serialized gameplay/authoring field, excluding only the hash itself.
        var canonical = JsonUtility.FromJson<PackedLevelData>(JsonUtility.ToJson(d));
        canonical.layoutHash = "";
        return ComputeSha256(JsonUtility.ToJson(canonical));
    }

    public static bool VerifyPackedLevel(PackedLevelData d)
    {
        if (d == null || string.IsNullOrEmpty(d.layoutHash) || d.effects == null ||
            d.tiles == null || Array.Exists(d.tiles, t => t == null)) return false;
        if (ComputeLayoutHashFromPacked(d) != d.layoutHash) return false;
        return new LevelValidator().Validate(ToGeneratedLevel(d)).Pass;
    }

    [Serializable] private class ArrayWrapper<T> { public T[] values; }
    public static T[] Clone<T>(T[] values)
        => values == null ? null : JsonUtility.FromJson<ArrayWrapper<T>>(
            JsonUtility.ToJson(new ArrayWrapper<T> { values = values })).values;

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
            useDefaultSpecialRules = def.SpecialRules == null,
            specialRules = Clone(def.SpecialRules),
            overrides = Clone(def.Overrides ?? new SpecialTileOverride[0]),
        };

    public static LevelDefinition ToDefinition(LevelDefinitionData d)
        => d == null ? null : new LevelDefinition
        {
            LevelId = d.levelId,
            ChapterId = d.chapterId,
            OrderInChapter = d.order,
            GridCount = d.gridCount,
            Difficulty = (DifficultyTier)d.difficultyTier,
            Seed = unchecked((uint)d.seed),
            ThemeId = d.themeId,
            Enabled = d.enabled,
            SpecialRules = d.useDefaultSpecialRules ? null : Clone(d.specialRules),
            Overrides = Clone(d.overrides ?? new SpecialTileOverride[0]),
        };
}
