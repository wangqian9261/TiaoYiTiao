using UnityEngine;

/// <summary>转盘奖池条目类型（与盘面 5 格一一对应）</summary>
public enum WheelPrizeKind
{
    Diamonds = 0,   // 钻石
    Coins = 1,      // 金币（本关内累计，受"本关金币翻倍"影响）
    Shards = 2,     // 通用皮肤碎片
    TrialSkin = 3,  // 随机稀有皮肤体验（时长见 trialSkinHours）
    CoinDouble = 4, // 本关金币收益翻倍（覆盖通关金币与所有金币砖，含触发前已得）
}

/// <summary>
/// 全局经济配置（ScriptableObject，Assets/GameRes/Config/LevelEconomyConfig.asset）：
/// 奖励值与转盘奖池只能在全局经济配置中修改（策划案§4/§9）。
/// 编辑器菜单 LevelSystem/1 创建；运行时经 ResMgr 加载（缺失时回退代码默认值）。
/// </summary>
[CreateAssetMenu(menuName = "Game3D001/Level Economy Config", fileName = "LevelEconomyConfig")]
public class LevelEconomyConfig : ScriptableObject
{
    [Header("通关金币（结算 = clearCoinBase × 档位通关金币倍率）")]
    public int clearCoinBase = 100;

    [Header("金币砖：固定 + 随机（随机值由格子 effectSeed 决定）")]
    public int coinBagFixed = 20;
    public int coinBagRandomMin = 5;
    public int coinBagRandomMax = 15;

    [Header("皮肤碎片砖：固定碎片数")]
    public int shardPerBrick = 1;

    [Header("护盾重复触发：转换为金币")]
    public int shieldReTriggerCoins = 20;

    [Header("幸运转盘奖池（5 格与盘面一一对应）")]
    public int[] wheelPrizeKinds = { (int)WheelPrizeKind.Diamonds, (int)WheelPrizeKind.Coins, (int)WheelPrizeKind.Shards, (int)WheelPrizeKind.TrialSkin, (int)WheelPrizeKind.CoinDouble };
    public int[] wheelAmounts = { 10, 200, 5, 24, 0 };
    public int[] wheelWeights = { 5, 35, 20, 10, 30 };
    public string[] wheelLabels = { "钻石 ×10", "金币 ×200", "通用碎片 ×5", "稀有皮肤体验 24 小时", "本关金币收益翻倍" };

    [Header("体验皮肤（皮肤系统未就绪：先记账，外观后续接入）")]
    public int trialSkinKindCount = 4;     // 稀有皮肤占位档位数
    public int trialSkinHours = 24;        // 体验时长；已有体验时间则延长
    public int trialSkinConvertCoins = 100; // 已永久拥有时转换金币（皮肤购买系统就绪前该路径休眠）

    /// <summary>运行时兜底实例（配置资产缺失时代码默认值）</summary>
    public static LevelEconomyConfig Fallback
    {
        get
        {
            var cfg = CreateInstance<LevelEconomyConfig>();
            cfg.name = "LevelEconomyConfig(Default)";
            return cfg;
        }
    }
}
