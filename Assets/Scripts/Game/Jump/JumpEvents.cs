/// <summary>跳一跳业务事件Key（点分式，风格对齐 EventKeys / ProcedureEvents）</summary>
public static class JumpEvents
{
    public const string Started = "Jump.Started";
    /// <summary>JumpScoreArgs：得分变化（踩块基础分 / 完美连击 / 倍率加成）</summary>
    public const string ScoreChanged = "Jump.ScoreChanged";
    /// <summary>int：本次完美连击数（≥1）</summary>
    public const string Perfect = "Jump.Perfect";
    /// <summary>string：通用提示飘字（HUD 顶部展示：物品效果/护盾生效/转盘奖励）</summary>
    public const string Toast = "Jump.Toast";
    /// <summary>JumpResultArgs：单局结算（结果页入参同款）</summary>
    public const string Died = "Jump.Died";
    /// <summary>int：通关的关卡号</summary>
    public const string LevelCleared = "Jump.LevelCleared";
}
