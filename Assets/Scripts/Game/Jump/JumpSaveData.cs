/// <summary>跳一跳存档（DataMgr XOR 混淆落盘，key 见 SaveKey）</summary>
public class JumpSaveData
{
    public const string SaveKey = "jump_save";

    public int best;
    public int playCount;
    public long totalScore;
    public int level = 1; // 当前关卡（1 起，通关 +1）
}
