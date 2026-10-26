using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>Deterministic geometry, weighted single-effect tiles, then explicit overrides.</summary>
public class LevelGenerator : ILevelGenerator
{
    public const string GeneratorVersion = "2.0.0";
    public const int MaxAttempts = 100;
    private readonly SpecialTileSettings _effects;
    public ValidationReport LastReport { get; private set; }
    public LevelGenerator() : this(new SpecialTileSettings()) { }
    public LevelGenerator(SpecialTileSettings effects) { _effects = effects; }

    public GeneratedLevel Generate(LevelDefinition definition, DifficultyProfile profile)
    {
        LastReport = LevelValidator.ValidateDefinition(definition, _effects);
        if (!LastReport.Pass || profile == null) return null;
        var def = LevelPackIo.ToDefinition(LevelPackIo.ToDefinitionData(definition));
        def.SpecialRules = (def.SpecialRules ?? DifficultyProfiles.DefaultRules(def.Difficulty)).OrderBy(r => r.Kind).ToArray();
        foreach (var r in def.SpecialRules) r.Levels = r.Levels.OrderBy(v => v.LevelId).ToArray();
        var effects = _effects.Snapshot();
        var root = new Pcg32(Pcg32.StreamSeed(def.LevelId, def.Seed, GeneratorVersion));
        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            var rng = new Pcg32(root.NextULong(), (ulong)attempt * 2 + 1);
            var level = BuildAutomatic(def, profile, effects, rng);
            if (level == null) continue;
            // Overrides deliberately do not cause re-rolls of the automatic layout.
            foreach (var o in def.Overrides)
            {
                level.Tiles[o.Index].Kind = o.Kind;
                level.Tiles[o.Index].EffectLevelId = o.EffectLevelId;
            }
            level.LayoutHash = LevelPackIo.ComputeLayoutHash(level);
            LastReport = new LevelValidator().Validate(level);
            return level; // Keep invalid previews visible; publishing is blocked by LastReport.
        }
        LastReport.Add(ValidationSeverity.Error, "gen.failed", "100 次尝试无法满足密度与冲刺通道；请调整权重、等级或 Seed");
        return null;
    }

    private GeneratedLevel BuildAutomatic(LevelDefinition def, DifficultyProfile p, SpecialTileSettings effects, Pcg32 rng)
    {
        int n = def.GridCount;
        var level = new GeneratedLevel { Definition = def, Effects = effects, GeneratorVersion = GeneratorVersion, TileCount = n };
        var axis = rng.NextDouble() < .5 ? TileAxis.X : TileAxis.Z;
        for (int i = 0; i < n; i++)
        {
            var tile = new GeneratedTile { Index = i, Kind = i == 0 ? TileKind.Start : i == n - 1 ? TileKind.End : TileKind.Normal,
                Size = LevelMath.FromMm(LevelMath.ToMm(DifficultyProfiles.BaseTileSize * p.SizePct * rng.Range(.95f, 1.05f))) };
            if (i > 0)
            {
                if (i > 1 && rng.NextDouble() < p.TurnRate) axis = axis == TileAxis.X ? TileAxis.Z : TileAxis.X;
                var prev = level.Tiles[i - 1];
                float d = LevelMath.ClampReach(DifficultyProfiles.BaseEdgeGap * rng.Range(p.GapMulLo, p.GapMulHi) * p.Coefficient + (prev.Size + tile.Size) * .5f);
                tile.X = LevelMath.FromMm(LevelMath.ToMm(prev.X + (axis == TileAxis.X ? d : 0)));
                tile.Z = LevelMath.FromMm(LevelMath.ToMm(prev.Z + (axis == TileAxis.Z ? d : 0)));
                tile.ArrivalAxis = axis;
                level.PredictedClearSeconds += LevelMath.ChargeMsAtDist(d) * .001f + LevelMath.AirTimeAtDist(d) + .35f;
            }
            level.Tiles.Add(tile);
        }
        int count = Mathf.RoundToInt((n - 2) * p.SpecialDensity);
        var chosen = new List<GeneratedTile>();
        bool hasSprint = false;
        for (int k = 0; k < count; k++)
        {
            var rules = def.SpecialRules.Where(r => r.Weight > 0 && !(hasSprint && r.Kind == TileKind.Sprint)).ToArray();
            var rule = Pick(rules, r => r.Weight, rng);
            if (rule == null) return null;
            var grade = Pick(rule.Levels.Where(v => v.Weight > 0).ToArray(), v => v.Weight, rng);
            if (grade == null) return null;
            chosen.Add(new GeneratedTile { Kind = rule.Kind, EffectLevelId = grade.LevelId });
            hasSprint |= rule.Kind == TileKind.Sprint;
        }
        // Reserve the long automatic channel before placing other specials.
        foreach (var chosenTile in chosen.OrderByDescending(v => v.Kind == TileKind.Sprint))
        {
            var candidates = new List<int>();
            for (int i = 6; i <= n - 5; i++)
            {
                if (level.Tiles[i].Kind != TileKind.Normal || SpecialTiles.IsSpecial(level.Tiles[i - 1].Kind)
                    || SpecialTiles.IsSpecial(level.Tiles[i + 1].Kind)) continue;
                bool inChannel = level.Tiles.Any(t => t.Kind == TileKind.Sprint && i > t.Index && i <= t.Index + effects.GetSprint(t.EffectLevelId).Steps);
                if (inChannel) continue;
                int steps = chosenTile.Kind == TileKind.Sprint ? effects.GetSprint(chosenTile.EffectLevelId).Steps : 0;
                if (i + steps >= n - 1) continue;
                bool safe = true;
                for (int j = i + 1; j <= i + steps; j++) safe &= level.Tiles[j].Kind == TileKind.Normal;
                if (safe) candidates.Add(i);
            }
            if (candidates.Count == 0) return null;
            int index = candidates[rng.Range(0, candidates.Count)];
            level.Tiles[index].Kind = chosenTile.Kind;
            level.Tiles[index].EffectLevelId = chosenTile.EffectLevelId;
        }
        return level;
    }

    private static T Pick<T>(T[] values, Func<T, int> weight, Pcg32 rng) where T : class
    {
        long total = 0;
        foreach (var value in values) total += weight(value);
        if (total <= 0 || total > int.MaxValue) return null;
        int roll = rng.Range(0, (int)total);
        foreach (var value in values) { roll -= weight(value); if (roll < 0) return value; }
        return null;
    }
}
