using UnityEngine;

/// <summary>跳一跳运行状态</summary>
public enum JumpState
{
    Idle,      // 未开局（世界未创建或已销毁）
    Ready,     // 站定：可蓄力；彩蛋停留计时在此态推进
    Charging,  // 长按蓄力中
    Airborne,  // 跳跃滞空中
    Dead,      // 坠落演出中
    Over       // 已结算：等待结果页操作
}

/// <summary>得分变化事件参数</summary>
public class JumpScoreArgs
{
    public int Score;
    public int Gain;
    public bool Perfect;
    public int Combo;
    public Vector3 WorldPos; // 飘分锚点（落点上方）
}

/// <summary>单局结算参数（结果页入参 / Died 事件载荷）</summary>
public class JumpResultArgs
{
    public int Score;
    public int Best;
    public bool NewBest;
    public int MaxCombo;
    public int PerfectCount;
    public int Level;           // 本局所属关卡号（1 起；有限关卡=包内顺序位）
    public string LevelName;    // 关卡显示名（计划=烘焙包名；旧模式=JumpConfig.GetLevelName）
    public int LevelProgress;   // 本局已前进块数
    public int LevelTarget;     // 本关目标块数
    public bool LevelCleared;   // 是否踩中终点块通关
    public int BaseCoins, ComboCoins;
    public int CoinsGained;      // 本局入账金币（有限关卡：金币砖+转盘+通关金币 × 档位倍率）
    public int LevelId;          // 有限关卡 levelId（0=旧无限模式）
    public string NextLevelName; // 通关后的下一关名（null=旧模式或已通全部）
}
