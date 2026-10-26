using System;
using System.Collections.Generic;

public enum DifficultyTier { Easy, Normal, Hard, Expert, Master }
// Schema 2 only. Never reinterpret schema 1 enum values.
public enum TileKind { Start, Normal, Ice, Collapse, Sprint, End }
public enum TileAxis { X, Z }
public enum ValidationSeverity { Info, Warning, Error }

[Serializable]
public class EffectLevelWeight
{
    public int LevelId;
    public int Weight;
    public EffectLevelWeight(int id, int weight) { LevelId = id; Weight = weight; }
}
[Serializable]
public class SpecialTileRule
{
    public TileKind Kind;
    public int Weight;
    public EffectLevelWeight[] Levels = new EffectLevelWeight[0];
}
[Serializable]
public class SpecialTileOverride
{
    public int Index;
    public TileKind Kind = TileKind.Normal;
    public int EffectLevelId;
}
public class LevelDefinition
{
    public int LevelId = 1, ChapterId = 1, OrderInChapter = 1, GridCount = 24;
    public DifficultyTier Difficulty;
    public uint Seed;
    public int ThemeId;
    public bool Enabled = true;
    // null means difficulty defaults; an explicit empty/zero table is an error.
    public SpecialTileRule[] SpecialRules;
    public SpecialTileOverride[] Overrides = new SpecialTileOverride[0];
}
public class DifficultyProfile
{
    public DifficultyTier Tier;
    public string DisplayName;
    public float Coefficient, SizePct, GapMulLo, GapMulHi, TurnRate, SpecialDensity, ClearCoinMult;
}
public class GeneratedTile
{
    public int Index;
    public TileKind Kind;
    public TileAxis ArrivalAxis;
    public float X, Z, Size;
    public int EffectLevelId;
}
public class GeneratedLevel
{
    public LevelDefinition Definition;
    public string GeneratorVersion;
    public int TileCount;
    public List<GeneratedTile> Tiles = new List<GeneratedTile>();
    public SpecialTileSettings Effects;
    public string LayoutHash;
    public float PredictedClearSeconds;
}
public class ValidationIssue
{
    public ValidationSeverity Severity;
    public string Code, Message;
    public ValidationIssue(ValidationSeverity severity, string code, string message)
    { Severity = severity; Code = code; Message = message; }
}
public class ValidationReport
{
    public List<ValidationIssue> Issues = new List<ValidationIssue>();
    public bool HasErrors;
    public int ErrorCount, WarningCount;
    public bool Pass => !HasErrors;
    public void Add(ValidationSeverity severity, string code, string message)
    {
        Issues.Add(new ValidationIssue(severity, code, message));
        if (severity == ValidationSeverity.Error) { HasErrors = true; ErrorCount++; }
        else if (severity == ValidationSeverity.Warning) WarningCount++;
    }
}
public class LevelResult
{
    public int LevelId, Seq, Score, CoinsGained;
    public bool Cleared, NewBest, FirstClear;
}
public class LevelRecord { public int LevelId, BestScore; public bool Cleared; }
public interface ILevelGenerator { GeneratedLevel Generate(LevelDefinition definition, DifficultyProfile profile); }
public interface ILevelValidator { ValidationReport Validate(GeneratedLevel level); }
public interface ILevelPackExporter { LevelPack Export(IReadOnlyList<GeneratedLevel> levels, string packVersion); }
