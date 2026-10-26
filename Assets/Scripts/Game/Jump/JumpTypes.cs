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

/// <summary>砖上奖励物品种类（踩上后停留自动触发，无需操作）</summary>
public enum JumpItemKind
{
    None = 0,
    Wheel,      // 幸运转盘：弹迷你转盘抽大奖（最稀有）
    ScoreBoost, // 积分增益：本局积分倍率 +20% 可叠加；完美落点额外 +3
    CoinBag,    // 金币袋：固定金币 + 随机小额
    Shard,      // 皮肤碎片：通用碎片入背包
    Shield,     // 护盾：一次掉落免疫，坠落自动复活
    Sprint,     // 终点冲刺：直接跳过后面 3 块
}

/// <summary>
/// 陷阱砖种类（温和不阴间：全部有预警外观、可规避、无瞬间秒杀；落地即触发）。
/// 标记模型静置块顶正中心（同物品规则）；云台染色/半透明做主预警。
/// </summary>
public enum JumpTrapKind
{
    None = 0,
    Slippery,  // 打滑：落地小幅滑动偏移（限块内，不掉落）
    Phantom,   // 幻影：半透明看似正常，落地塌陷（护盾可抵挡；一关≤2、绝不连续）
    SlowCharge,// 迷雾减速：接下来 3 次蓄力速度减半
    Shake,     // 震荡错位：下一块轻微随机横移（保护可达性，仍可跳）
    Burn,      // 限时燃烧：踩上 2.5 秒倒计时，不起跳则塌落（反应时间充足）
    Reverse,   // 反向蓄力：下一次短按跳远、长按跳近（高阶关）
    Seal,      // 封印：本关剩余物品模型隐匿（考验记忆）
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
    public int CoinsGained;      // 本局入账金币（有限关卡：金币砖+转盘+通关金币 × 档位倍率）
    public int LevelId;          // 有限关卡 levelId（0=旧无限模式）
    public string NextLevelName; // 通关后的下一关名（null=旧模式或已通全部）
}
