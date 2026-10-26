using UnityEngine;

/// <summary>Shared deterministic grounded timer used by runtime and simulation.</summary>
public sealed class LevelTileEffectResolver
{
    public float Elapsed { get; private set; }
    public void Reset() => Elapsed = 0f;
    public float AdvanceIce(IceLevel level, float deltaTime)
    {
        float before = Mathf.Max(0, Elapsed - level.StartDelay);
        Elapsed += Mathf.Max(0, deltaTime);
        return (Mathf.Max(0, Elapsed - level.StartDelay) - before) * level.Speed;
    }
    public bool AdvanceCollapse(CollapseLevel level, float deltaTime)
    {
        Elapsed += Mathf.Max(0, deltaTime);
        return Elapsed >= level.Seconds;
    }
    public static bool Outside(float x, float z, float size) => Mathf.Abs(x) > size * .5f || Mathf.Abs(z) > size * .5f;
}
