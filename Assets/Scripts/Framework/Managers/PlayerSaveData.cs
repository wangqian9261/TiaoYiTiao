/// <summary>玩家基础存档（DataMgr XOR 混淆落盘，key 见 SaveKey；结构仿 JumpSaveData）</summary>
public class PlayerSaveData
{
    public const string SaveKey = "player_save";

    public int avatarId;      // 头像id（暂无 UI 槽位，纯记录）
    public int coins;         // 金币
    public long score;        // 积分
    public int energy;        // 体力
    public int shards;        // 皮肤碎片（砖上物品获取，背包展示）
    public int diamonds;      // 钻石（转盘奖励；皮肤商店就绪前纯记录）
    public long lastRegenUtc; // 体力未满时上次计点基准（Unix 秒；已满时无意义）
    public int trialSkinKindId;    // 体验皮肤档位id（0=无；皮肤系统就绪后接外观）
    public long trialSkinExpiresUtc; // 体验皮肤到期（Unix 秒）
}
