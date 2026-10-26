using System.Collections.Generic;
using UnityEngine;

/// <summary>One player model shared by all difficulty tiers; uses the same effect timer as runtime.</summary>
public class LevelSimulator
{
    // Synthetic player calibration, not a measured live-player success guarantee.
    public float SigmaHoldMs = 56f, FumbleChance = .004f, HesitateChance = .08f;
    public float HesitateMinSeconds = .4f, HesitateMaxSeconds = 2.2f, ReactionSeconds = .25f, PressureSigmaMult = 1.5f;
    public int DefaultRuns = 1000;
    public struct SimResult
    {
        public int Runs, Clears;
        public float ClearRate, AvgMaxIndex;
        public Dictionary<string, int> DeathCauses;
    }
    public SimResult Simulate(GeneratedLevel level, int runs, ulong seedBase)
    {
        var result = new SimResult { Runs = Mathf.Max(1, runs), DeathCauses = new Dictionary<string, int>() };
        var rng = new Pcg32(seedBase ^ Pcg32.StableHash("sim/" + level.Definition.LevelId));
        var tiles = level.Tiles;
        long sum = 0;
        for (int run = 0; run < result.Runs; run++)
        {
            int cur = 0, guard = 0;
            float x = tiles[0].X, z = tiles[0].Z;
            var timer = new LevelTileEffectResolver();
            string death = null;
            while (cur < tiles.Count - 1 && death == null && guard++ < 4000)
            {
                var from = tiles[cur];
                if (from.Kind == TileKind.Sprint)
                {
                    cur += level.Effects.GetSprint(from.EffectLevelId).Steps;
                    x = tiles[cur].X; z = tiles[cur].Z;
                    timer.Reset(); continue;
                }
                var to = tiles[cur + 1];
                bool axisX = to.ArrivalAxis == TileAxis.X;
                float distance = axisX ? to.X - x : to.Z - z;
                float holdNeeded = Mathf.InverseLerp(JumpConfig.MinJumpDist, JumpConfig.MaxJumpDist, distance) * JumpConfig.MaxChargeSeconds * 1000;
                bool pressure = from.Kind == TileKind.Ice || from.Kind == TileKind.Collapse;
                float error = Gaussian(rng) * SigmaHoldMs * (pressure ? PressureSigmaMult : 1);
                if (rng.NextDouble() < FumbleChance) error += (rng.NextDouble() < .5 ? -1 : 1) * rng.Range(75f, 300f);
                float hold = Mathf.Max(0, holdNeeded + error);
                float hesitate = pressure && rng.NextDouble() < HesitateChance ? rng.Range(HesitateMinSeconds, HesitateMaxSeconds) : 0;
                float leaveAt = ReactionSeconds + hesitate + hold * .001f;
                if (from.Kind == TileKind.Collapse && timer.AdvanceCollapse(level.Effects.GetCollapse(from.EffectLevelId), leaveAt))
                { death = "collapse"; break; }
                if (from.Kind == TileKind.Ice)
                {
                    float slide = timer.AdvanceIce(level.Effects.GetIce(from.EffectLevelId), leaveAt);
                    if (axisX) x += slide; else z += slide;
                    if (LevelTileEffectResolver.Outside(x - from.X, z - from.Z, from.Size)) { death = "ice"; break; }
                }
                float power = Mathf.Repeat(hold / (JumpConfig.MaxChargeSeconds * 1000), 1f);
                float dist = Mathf.Lerp(JumpConfig.MinJumpDist, JumpConfig.MaxJumpDist, power);
                // Runtime starts at the actual axis offset, keeping the orthogonal lane centered.
                float land = (axisX ? x : z) + dist;
                if (Mathf.Abs(land - (axisX ? to.X : to.Z)) <= to.Size * .5f)
                {
                    x = axisX ? land : from.X; z = axisX ? from.Z : land;
                    cur++; timer.Reset();
                }
                else if (Mathf.Abs(land - (axisX ? from.X : from.Z)) <= from.Size * .5f)
                { x = axisX ? land : from.X; z = axisX ? from.Z : land; }
                else death = "fall";
            }
            if (cur >= tiles.Count - 1) result.Clears++;
            else
            {
                death = death ?? "timeout";
                result.DeathCauses.TryGetValue(death, out int count);
                result.DeathCauses[death] = count + 1;
            }
            sum += cur;
        }
        result.ClearRate = (float)result.Clears / result.Runs;
        result.AvgMaxIndex = (float)sum / result.Runs;
        return result;
    }
    private static float Gaussian(Pcg32 rng) => Mathf.Sqrt(-2 * Mathf.Log((float)(1 - rng.NextDouble()))) * Mathf.Sin(2 * Mathf.PI * (float)rng.NextDouble());
}
