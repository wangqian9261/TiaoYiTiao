using System;
using System.Linq;
using UnityEngine;

[Serializable] public class IceLevel
{
    public int Id;
    public float StartDelay, Speed;
}
[Serializable] public class CollapseLevel
{
    public int Id;
    public float Seconds;
}
[Serializable] public class SprintLevel
{
    public int Id, Steps;
    public float ChargeSeconds = .35f, HopSeconds = .20f;
}

/// <summary>Serializable snapshot embedded in every baked level. IDs are local to a tile type.</summary>
[Serializable]
public class SpecialTileSettings
{
    public int Version = 1;
    public IceLevel[] Ice = Enumerable.Range(1, 5).Select(i => new IceLevel
        { Id = i, StartDelay = .95f - i * .15f, Speed = .10f + i * .05f }).ToArray();
    public CollapseLevel[] Collapse = Enumerable.Range(1, 5).Select(i => new CollapseLevel
        { Id = i, Seconds = 2.8f - i * .3f }).ToArray();
    public SprintLevel[] Sprint = Enumerable.Range(1, 5).Select(i => new SprintLevel { Id = i, Steps = i }).ToArray();

    public IceLevel GetIce(int id) => Ice?.FirstOrDefault(v => v != null && v.Id == id);
    public CollapseLevel GetCollapse(int id) => Collapse?.FirstOrDefault(v => v != null && v.Id == id);
    public SprintLevel GetSprint(int id) => Sprint?.FirstOrDefault(v => v != null && v.Id == id);
    public int[] LevelIds(TileKind kind)
    {
        switch (kind)
        {
            case TileKind.Ice: return (Ice ?? new IceLevel[0]).Where(v => v != null).Select(v => v.Id).OrderBy(id => id).ToArray();
            case TileKind.Collapse: return (Collapse ?? new CollapseLevel[0]).Where(v => v != null).Select(v => v.Id).OrderBy(id => id).ToArray();
            case TileKind.Sprint: return (Sprint ?? new SprintLevel[0]).Where(v => v != null).Select(v => v.Id).OrderBy(id => id).ToArray();
            default: return new int[0];
        }
    }
    public bool Has(TileKind kind, int id) => kind == TileKind.Ice ? GetIce(id) != null
        : kind == TileKind.Collapse ? GetCollapse(id) != null : kind == TileKind.Sprint ? GetSprint(id) != null : id == 0;

    public SpecialTileSettings Snapshot()
    {
        var copy = JsonUtility.FromJson<SpecialTileSettings>(JsonUtility.ToJson(this));
        if (copy.Ice != null) copy.Ice = copy.Ice.OrderBy(v => v?.Id ?? 0).ToArray();
        if (copy.Collapse != null) copy.Collapse = copy.Collapse.OrderBy(v => v?.Id ?? 0).ToArray();
        if (copy.Sprint != null) copy.Sprint = copy.Sprint.OrderBy(v => v?.Id ?? 0).ToArray();
        return copy;
    }
    public ValidationReport Validate()
    {
        var r = new ValidationReport();
        void Check(bool ok, string message) { if (!ok) r.Add(ValidationSeverity.Error, "effects.config", message); }
        Check(Version > 0, "特殊格配置版本必须为正数");
        Check(Ice != null && Ice.Length > 0 && Ice.All(v => v != null && v.Id > 0 && Finite(v.StartDelay) && v.StartDelay >= 0 && Positive(v.Speed))
            && Ice.Select(v => v.Id).Distinct().Count() == Ice.Length, "冰滑等级须有唯一正 ID、非负延迟及正滑速");
        Check(Collapse != null && Collapse.Length > 0 && Collapse.All(v => v != null && v.Id > 0 && Positive(v.Seconds))
            && Collapse.Select(v => v.Id).Distinct().Count() == Collapse.Length, "塌陷等级须有唯一正 ID 和正倒计时");
        Check(Sprint != null && Sprint.Length > 0 && Sprint.All(v => v != null && v.Id > 0 && v.Steps > 0 && v.Steps < 60 && Positive(v.ChargeSeconds) && Positive(v.HopSeconds))
            && Sprint.Select(v => v.Id).Distinct().Count() == Sprint.Length, "冲刺等级须有唯一正 ID、1–59 推进格数及正时长");
        return r;
    }
    public static bool Finite(float f) => !float.IsNaN(f) && !float.IsInfinity(f);
    private static bool Positive(float f) => Finite(f) && f > 0;
}
[CreateAssetMenu(menuName = "Game3D001/Special Tile Config")]
public class SpecialTileConfig : ScriptableObject
{
    public const string AssetPath = "Assets/GameRes/Config/SpecialTileConfig.asset";
    public SpecialTileSettings Settings = new SpecialTileSettings();
}
public static class SpecialTiles
{
    public static bool IsSpecial(TileKind kind) => kind == TileKind.Ice || kind == TileKind.Collapse || kind == TileKind.Sprint;
    public static string Name(TileKind kind)
    {
        switch (kind)
        {
            case TileKind.Start: return "起点";
            case TileKind.Ice: return "冰滑";
            case TileKind.Collapse: return "塌陷";
            case TileKind.Sprint: return "冲刺";
            case TileKind.End: return "终点";
            default: return "普通";
        }
    }
    public static Color ColorOf(TileKind kind)
    {
        switch (kind)
        {
            case TileKind.Start: return new Color(.55f, .95f, .65f);
            case TileKind.Ice: return new Color(.55f, .88f, 1f);
            case TileKind.Collapse: return new Color(1f, .63f, .64f);
            case TileKind.Sprint: return new Color(.76f, .68f, 1f);
            case TileKind.End: return JumpConfig.GoalBlockColor;
            default: return Color.white;
        }
    }
}
