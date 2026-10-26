using System;
using UnityEngine;

[Serializable]
public class ComboCoinTier
{
    public int MinCombo;
    public int BonusPercent;
}
public struct RunCoinReward
{
    public int BaseCoins, ComboCoins;
    public int Total => BaseCoins + ComboCoins;
}
[CreateAssetMenu(menuName = "Game3D001/Level Economy Config", fileName = "LevelEconomyConfig")]
public class LevelEconomyConfig : ScriptableObject
{
    public int clearCoinBase = 100;
    public ComboCoinTier[] comboTiers = {
        new ComboCoinTier { MinCombo = 0, BonusPercent = 0 },
        new ComboCoinTier { MinCombo = 3, BonusPercent = 10 },
        new ComboCoinTier { MinCombo = 5, BonusPercent = 25 },
        new ComboCoinTier { MinCombo = 8, BonusPercent = 50 },
        new ComboCoinTier { MinCombo = 12, BonusPercent = 100 },
    };

    public RunCoinReward Calculate(bool cleared, bool isPlaytest, float difficultyMultiplier, int maxCombo)
    {
        if (!cleared || isPlaytest) return default;
        int percent = 0, threshold = -1;
        foreach (var tier in comboTiers ?? new ComboCoinTier[0])
            if (tier != null && tier.MinCombo >= 0 && tier.MinCombo <= maxCombo && tier.MinCombo > threshold)
            { threshold = tier.MinCombo; percent = Mathf.Max(0, tier.BonusPercent); }
        int coins = Mathf.Max(0, Mathf.RoundToInt(clearCoinBase * difficultyMultiplier));
        return new RunCoinReward { BaseCoins = coins, ComboCoins = Mathf.RoundToInt(coins * (percent / 100f)) };
    }
    public static LevelEconomyConfig Fallback => CreateInstance<LevelEconomyConfig>();
}
