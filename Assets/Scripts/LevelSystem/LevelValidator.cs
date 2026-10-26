using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class LevelValidator : ILevelValidator
{
    public static ValidationReport ValidateDefinition(LevelDefinition def, SpecialTileSettings effects)
    {
        var r = effects != null ? effects.Validate() : new ValidationReport();
        void Error(string code, string text) => r.Add(ValidationSeverity.Error, code, text);
        if (effects == null) Error("effects.missing", "缺少特殊格参数快照");
        if (def == null) { Error("definition.missing", "关卡定义为空"); return r; }
        if (def.GridCount < 20 || def.GridCount > 60) Error("gridCount.range", "格数须为 20–60");
        if (!Enum.IsDefined(typeof(DifficultyTier), def.Difficulty)) { Error("difficulty.range", "未知关卡难度"); return r; }
        if (def.LevelId <= 0 || def.ChapterId <= 0 || def.OrderInChapter <= 0) Error("identity.range", "关卡、章节和顺序 ID 必须为正");
        if (def.ThemeId < 0 || def.ThemeId >= JumpConfig.LevelThemeNames.Length) Error("theme.range", "未知主题");
        var rules = def.SpecialRules ?? DifficultyProfiles.DefaultRules(def.Difficulty);
        var seen = new HashSet<TileKind>();
        long typeTotal = 0;
        foreach (var rule in rules)
        {
            if (rule == null || !SpecialTiles.IsSpecial(rule.Kind) || !seen.Add(rule.Kind))
            { Error("rules.kind", "类型权重表包含空项、未知或重复类型"); continue; }
            if (rule.Weight < 0) Error("rules.weight", "权重不能为负");
            typeTotal += rule.Weight;
            var ids = new HashSet<int>();
            long total = 0;
            if (rule.Levels == null) { Error("rules.levels", "缺少等级权重"); continue; }
            foreach (var w in rule.Levels)
            {
                if (w == null) { Error("rules.level", "空等级权重"); continue; }
                if (w.Weight < 0 || !ids.Add(w.LevelId) || effects == null || !effects.Has(rule.Kind, w.LevelId))
                    Error("rules.level", "等级权重须引用有效且不重复的 ID，权重不可为负");
                total += w.Weight;
            }
            if ((rule.Weight > 0 && total <= 0) || total > int.MaxValue) Error("rules.total", "启用类型的等级权重总和必须有效");
        }
        if (typeTotal <= 0 || typeTotal > int.MaxValue) Error("rules.total", "类型权重总和必须为 1–Int32.MaxValue");
        var indices = new HashSet<int>();
        foreach (var o in def.Overrides ?? new SpecialTileOverride[0])
        {
            if (o == null) { Error("override.empty", "空覆盖项"); continue; }
            if (o.Index <= 0 || o.Index >= def.GridCount - 1 || !indices.Add(o.Index))
                Error("override.index", $"覆盖格 {o.Index} 越界、重复或为起终点");
            if (!(o.Kind == TileKind.Normal || SpecialTiles.IsSpecial(o.Kind)))
                Error("override.kind", "只能覆盖为普通、冰滑、塌陷或冲刺");
            if (effects == null || !effects.Has(o.Kind, o.EffectLevelId) || (SpecialTiles.IsSpecial(o.Kind) && o.EffectLevelId <= 0))
                Error("override.level", $"格 {o.Index} 引用了无效等级 {o.EffectLevelId}");
        }
        return r;
    }

    public ValidationReport Validate(GeneratedLevel level)
    {
        if (level == null) { var empty = new ValidationReport(); empty.Add(ValidationSeverity.Error, "level.empty", "关卡为空"); return empty; }
        var r = ValidateDefinition(level.Definition, level.Effects);
        void Error(string code, string text) => r.Add(ValidationSeverity.Error, code, text);
        if (!r.Pass) return r;
        int n = level.Tiles?.Count ?? 0;
        if (n != level.Definition.GridCount || n != level.TileCount || level.Tiles.Any(t => t == null))
        { Error("tileCount.mismatch", "瓦片数量或内容不匹配"); return r; }
        if (level.Tiles.Count(t => t.Kind == TileKind.Start) != 1 || level.Tiles[0].Kind != TileKind.Start)
            Error("start.count", "起点须唯一且为首格");
        if (level.Tiles.Count(t => t.Kind == TileKind.End) != 1 || level.Tiles[n - 1].Kind != TileKind.End)
            Error("end.count", "终点须唯一且为末格");
        if (level.Tiles.Count(t => t.Kind == TileKind.Sprint) > 1) Error("sprint.cap", "每关最多一个冲刺格");
        for (int i = 0; i < n; i++)
        {
            var t = level.Tiles[i];
            if (t.Index != i || !Enum.IsDefined(typeof(TileKind), t.Kind) || !Enum.IsDefined(typeof(TileAxis), t.ArrivalAxis))
                Error("tile.identity", $"格 {i} 索引、类型或轴无效");
            if (!SpecialTileSettings.Finite(t.Size) || t.Size <= 0 || !SpecialTileSettings.Finite(t.X) || !SpecialTileSettings.Finite(t.Z))
                Error("tile.geometry", $"格 {i} 几何参数无效");
            if (!level.Effects.Has(t.Kind, t.EffectLevelId)) Error("tile.level", $"格 {i} 的等级不存在或普通格等级非 0");
            if (SpecialTiles.IsSpecial(t.Kind))
            {
                if (i < 6 || i > n - 5) Error("special.zone", $"格 {i} 位于起步或终点安全区");
                if (i > 0 && SpecialTiles.IsSpecial(level.Tiles[i - 1].Kind)) Error("special.adjacent", $"格 {i - 1} 与 {i} 特殊格连续");
            }
            if (t.Kind == TileKind.Sprint)
            {
                var grade = level.Effects.GetSprint(t.EffectLevelId);
                if (grade == null) continue;
                if (i + grade.Steps >= n - 1) Error("sprint.channel", $"冲刺格 {i} 的通道超界或触及终点");
                for (int j = i + 1; j <= i + grade.Steps && j < n; j++)
                    if (level.Tiles[j].Kind != TileKind.Normal) Error("sprint.channel", $"冲刺格 {i} 的通道格 {j} 非普通格");
            }
            for (int j = 0; j < i; j++)
            {
                var b = level.Tiles[j];
                float half = (t.Size + b.Size) * .5f;
                if (Mathf.Abs(t.X - b.X) < half && Mathf.Abs(t.Z - b.Z) < half) Error("tile.overlap", $"格 {j}/{i} 重叠");
            }
            if (i == 0) continue;
            var prev = level.Tiles[i - 1];
            float d = t.ArrivalAxis == TileAxis.X ? t.X - prev.X : t.Z - prev.Z;
            float lateral = t.ArrivalAxis == TileAxis.X ? t.Z - prev.Z : t.X - prev.X;
            float charge = LevelMath.ChargeMsAtDist(d);
            if (Mathf.Abs(lateral) > .002f || charge < LevelMath.MinChargeMs - 1 || charge > LevelMath.MaxChargeMs + 1)
                Error("charge.range", $"跳 {i - 1}→{i} 不满足轴向可达性");
        }
        foreach (var o in level.Definition.Overrides ?? new SpecialTileOverride[0])
            if (level.Tiles[o.Index].Kind != o.Kind || level.Tiles[o.Index].EffectLevelId != o.EffectLevelId)
                Error("override.mismatch", $"格 {o.Index} 未兑现手工覆盖");
        if ((level.Definition.Overrides?.Length ?? 0) == 0 &&
            level.Tiles.Count(t => SpecialTiles.IsSpecial(t.Kind)) != Mathf.RoundToInt((n - 2) * DifficultyProfiles.Get(level.Definition.Difficulty).SpecialDensity))
            Error("special.count", "自动特殊格数量与密度公式不一致");
        return r;
    }
}
