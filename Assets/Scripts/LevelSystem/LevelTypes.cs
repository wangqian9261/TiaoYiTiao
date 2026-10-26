using System.Collections.Generic;

/// <summary>
/// 关卡系统公共类型：枚举 / 关卡定义 / 五档难度档位 / 生成结果 / 校验报告 / 全局奖励与陷阱配置 / 核心接口。
/// 设计要点（策划案）：
/// - 每关 20–60 格，包含起点与独立终点；同一关卡版本布局永远一致（固定 Seed + 版本化生成器）。
/// - 每格只能是普通、奖励、陷阱或终点中的一种；奖励砖落地停留自动触发，陷阱按各自预警/倒计时规则触发。
/// - 客户端运行时不重新生成正式关卡，只读取烘焙后的路线（整数毫米坐标 + layoutHash）。
/// </summary>

// ---------- 枚举 ----------

/// <summary>五档难度（策划案难度表）</summary>
public enum DifficultyTier
{
    Easy = 0,     // 简单
    Normal = 1,   // 普通
    Hard = 2,     // 困难
    Expert = 3,   // 专家
    Master = 4,   // 大师
}

/// <summary>格子种类（一块只属于一种）</summary>
public enum TileKind
{
    Start = 0,   // 起点（唯一）
    Normal = 1,  // 普通安全格
    Reward = 2,  // 奖励砖
    Trap = 3,    // 陷阱砖
    End = 4,     // 终点（唯一）
}

/// <summary>奖励类型（固定全局权重 + 单关上限；达到上限后权重重新归一化，无法选择时降级为金币）</summary>
public enum RewardKind
{
    None = 0,
    LuckyWheel = 1,  // 幸运转盘（≤1）：暂停蓄力/陷阱计时/关卡逻辑，3 秒自动开奖领奖关闭
    ScoreBoost = 2,  // 积分增益：本关分数倍率 +20%，叠至上限 2.0；只影响触发后的落地分
    Coin = 3,        // 金币：固定 20 + 随机 5–15（随机值由格子 effectSeed 决定）
    SkinShard = 4,   // 皮肤碎片（≤2）：固定 1 个通用碎片
    Shield = 5,      // 护盾（≤1）：一次掉落保护；已有护盾再踩中转换为 20 金币
    Sprint = 6,      // 冲刺（≤1）：自动越过后面 3 格落到第 4 格，不可打断 1.2s 演出
}

/// <summary>陷阱类型（按档位表出权；幻影≤2、反向/封印≤1；前 5 可跳格与终点前 3 格不放陷阱；陷阱不连续）</summary>
public enum TrapKind
{
    None = 0,
    Slippery = 1,    // 打滑：落地横向偏移 0.12m（方向由 Seed 决定；保留 ≥65% 支撑面积，绝不直接坠落）
    Phantom = 2,     // 幻影：落地闪烁 1.5s 后塌陷；倒计时内跳走可规避
    SlowCharge = 3,  // 蓄力减速：后续 3 次跳跃蓄力速度 -50%；再触发只刷新为 3 次不叠加
    Shake = 4,       // 震荡错位：下一格横向偏移 0.15–0.25m（生成期烘焙；仍须满足可达性校验）
    Burn = 5,        // 燃烧：落地 2.5s 倒计时，超时未离开塌陷
    Reverse = 6,     // 反向蓄力：仅影响下一跳 qReverse = 1 - q（短按远、长按近）
    Seal = 7,        // 封印：剩余奖励图标保留轮廓但隐藏颜色与发光，实际奖励不变
}

/// <summary>校验问题级别：Error=发布阻断；Warning=提示；Info=说明</summary>
public enum ValidationSeverity
{
    Info = 0,
    Warning = 1,
    Error = 2,
}

/// <summary>路线行进轴（跳一跳引擎恒 +X/+Z 正向步进）</summary>
public enum TileAxis
{
    X = 0,
    Z = 1,
}

// ---------- 关卡定义与生成结果 ----------

/// <summary>关卡定义（策划配置项；编辑器草稿的领域模型）</summary>
public class LevelDefinition
{
    public int LevelId = 1;
    public int ChapterId = 1;
    public int OrderInChapter = 1;
    public int GridCount = 24;                    // 20–60，含起点与终点
    public DifficultyTier Difficulty = DifficultyTier.Easy;
    public uint Seed;                             // 固定 Seed：同版本布局一致
    public int ThemeId;                           // 主题（JumpConfig.LevelThemeNames 索引）
    public bool Enabled = true;                   // 停用关卡不进包
}

/// <summary>五档难度参数（策划案难度表；基准方块边长 1.60m、基准边缘间距 0.65m）</summary>
public class DifficultyProfile
{
    public DifficultyTier Tier;
    public string DisplayName;
    public float Coefficient;    // 档位系数（间距放大）
    public float SizePct;        // 方块尺寸 = 1.60m × 此比例
    public float GapMulLo;       // 间距倍率下限（作用于基准边缘间距）
    public float GapMulHi;       // 间距倍率上限
    public float TurnRate;       // 转向率（本次跳跃切换行进轴的概率）
    public float RewardDensity;  // 奖励密度（rewardCount = round((gridCount-2)×density)）
    public float TrapDensity;    // 陷阱密度（trapCount = round((gridCount-2)×density)）
    public float ClearCoinMult;  // 通关金币倍率
    public int[] TrapWeights;    // 与 TrapKind 1..7 对位的类型权重
}

/// <summary>生成瓦片（领域模型；最终烘焙为毫米整数）</summary>
public class GeneratedTile
{
    public int Index;
    public TileKind Kind;
    public TileAxis ArrivalAxis;  // 从上一格跳入本格的行进轴（格 0 无意义）
    public float X;               // 米（烘焙终点位置，含震荡横移）
    public float Z;
    public float Size;            // 顶面边长（米）
    public RewardKind Reward;
    public TrapKind Trap;
    public uint EffectSeed;       // 特殊砖效果随机源（金币随机值/打滑方向/体验皮肤挑选等）
    public float ShakeOffset;     // 本格相对理想落点的横向偏移（米，有符号；由上一格震荡陷阱施加）
}

/// <summary>生成关卡（同输入永远一致；layoutHash 锁定布局字节）</summary>
public class GeneratedLevel
{
    public LevelDefinition Definition;
    public string GeneratorVersion;
    public int TileCount;
    public List<GeneratedTile> Tiles = new List<GeneratedTile>();
    public string LayoutHash;             // SHA-256（整数毫米坐标数组规范化串）
    public float PredictedClearSeconds;    // 估算通关时长（60–120s 之外编辑器警告）
}

// ---------- 校验 ----------

/// <summary>校验问题</summary>
public class ValidationIssue
{
    public ValidationSeverity Severity;
    public string Code;
    public string Message;

    public ValidationIssue(ValidationSeverity severity, string code, string message)
    {
        Severity = severity;
        Code = code;
        Message = message;
    }
}

/// <summary>校验报告：存在 Error 即发布阻断</summary>
public class ValidationReport
{
    public List<ValidationIssue> Issues = new List<ValidationIssue>();
    public bool HasErrors;
    public int ErrorCount;
    public int WarningCount;

    public void Add(ValidationSeverity severity, string code, string message)
    {
        Issues.Add(new ValidationIssue(severity, code, message));
        if (severity == ValidationSeverity.Error) { HasErrors = true; ErrorCount++; }
        else if (severity == ValidationSeverity.Warning) WarningCount++;
    }

    public bool Pass => !HasErrors;
}

// ---------- 单局结果与进度记录 ----------

/// <summary>单局结果（结算产物）</summary>
public class LevelResult
{
    public int LevelId;
    public int Seq;             // 关卡包顺序位（1 起）
    public int Score;
    public bool Cleared;
    public int CoinsGained;      // 本局入账金币（含通关金币 × 档位倍率）
    public bool NewBest;         // 刷新该关个人最高分
    public bool FirstClear;      // 首次通关（累计通关数只在此刻 +1）
}

/// <summary>单关进度记录（个人最高分只对玩家本人展示）</summary>
public class LevelRecord
{
    public int LevelId;
    public int BestScore;
    public bool Cleared;
}

// ---------- 全局奖励 / 陷阱配置 ----------

/// <summary>
/// 奖励砖全局配置：固定权重 + 单关上限（策划案§4）。
/// 权重为策划案定值；达到上限后重新归一化剩余权重，无法选择时降级为金币砖。
/// </summary>
public class GlobalRewardConfig
{
    public int WeightLuckyWheel = 3;
    public int WeightScoreBoost = 22;
    public int WeightCoin = 35;
    public int WeightSkinShard = 15;
    public int WeightShield = 15;
    public int WeightSprint = 10;
    public int CapLuckyWheel = 1;    // 幸运转盘单关上限
    public int CapSkinShard = 2;     // 皮肤碎片单关上限
    public int CapShield = 1;        // 护盾单关上限
    public int CapSprint = 1;        // 冲刺单关上限
    /// <summary>奖励砖索引间隔下限（"奖励砖之间至少间隔 2 格"）</summary>
    public int MinRewardIndexGap = 3;
    /// <summary>首个可放奖励格（起手教学区不放）</summary>
    public int FirstRewardIndex = 2;

    public static GlobalRewardConfig Default => new GlobalRewardConfig();
}

/// <summary>陷阱砖全局配置：数量上限 / 禁区 / 关键参数（策划案§5）</summary>
public class GlobalTrapConfig
{
    public int CapPhantom = 2;        // 幻影每关最多 2
    public int CapReverse = 1;        // 反向每关最多 1
    public int CapSeal = 1;           // 封印每关最多 1
    public int FirstSafeJumpables = 5; // 前 5 个可跳格（格 1..5）不放陷阱
    public int PreEndTrapFree = 3;     // 终点前 3 格不放陷阱
    public int SprintTailSafeTiles = 4; // 冲刺砖后的 4 格必须全部为普通安全格
    public int SprintPreEndExclusion = 5; // 冲刺不允许出现在终点前 5 格内
    public int SlowCoverReverseBan = 3;  // 减速效果覆盖的后 3 格内不得放反向蓄力砖
    public float ShakeOffsetMin = 0.15f;  // 震荡横移区间
    public float ShakeOffsetMax = 0.25f;
    public float SlipperyOffset = 0.12f;  // 打滑固定横移（保留 ≥65% 支撑面积由最小方块尺寸保证）
    public float SlipperyMinSupport = 0.65f;
    public float PhantomCollapseSeconds = 1.5f;
    public float BurnSeconds = 2.5f;
    /// <summary>首个可放陷阱格（= FirstSafeJumpables+1，即格 6 起）</summary>
    public int FirstTrapIndex => FirstSafeJumpables + 1;

    public static GlobalTrapConfig Default => new GlobalTrapConfig();
}

// ---------- 核心接口 ----------

/// <summary>关卡生成器：同一 (定义+档位+生成器版本) 输入永远产出一致布局</summary>
public interface ILevelGenerator
{
    GeneratedLevel Generate(LevelDefinition definition, DifficultyProfile profile);
}

/// <summary>关卡校验器：全部发布阻断条件 + 警告项</summary>
public interface ILevelValidator
{
    ValidationReport Validate(GeneratedLevel level, GlobalRewardConfig rewardConfig, GlobalTrapConfig trapConfig);
}

/// <summary>关卡包导出器：全包原子替换 + 版本化 JSON + SHA-256</summary>
public interface ILevelPackExporter
{
    LevelPack Export(IReadOnlyList<GeneratedLevel> levels, string packVersion);
}

/// <summary>运行时局内状态快照（效果解析入参）</summary>
public class RunStateSnapshot
{
    public int ShieldCount;      // 剩余护盾层数
    public bool CoinDoubleActive; // 转盘"本关金币翻倍"是否已激活
    public float ScoreMult;       // 当前分数倍率加成
}

/// <summary>落格效果解析结果（策划案数值的统一出口；演出与流程由 JumpGame 执行）</summary>
public class TileEffectResult
{
    public RewardKind Reward = RewardKind.None;
    public TrapKind Trap = TrapKind.None;
    // 奖励参数
    public int Coins;                    // 立即入账金币（金币砖 / 护盾转换）
    public int Shards;                   // 碎片数
    public int Diamonds;                 // 钻石（转盘）
    public bool ShieldConvertCoins;      // 已有护盾再踩中 → 转换金币
    public bool GrantTrialSkin;          // 转盘体验皮肤
    public int TrialSkinKindId;
    public int TrialSkinHours;
    public bool ActivateCoinDouble;      // 转盘"本关金币翻倍"
    public float ScoreMultAdd;           // 积分增益 +20%
    public float ScoreMultCap = 2.0f;    // 倍率叠加上限
    public int SprintLandIndex = -1;     // 冲刺落点格索引
    // 陷阱参数
    public float SlipperyOffset;         // 打滑横移距离
    public float SlipperyAngleDeg;       // 打滑方向（effectSeed 决定）
    public int SlowChargeJumps;          // 减速次数（刷新为 3，不叠加）
    public bool ReverseNext;             // 反向下一跳
    public float PhantomCollapseSeconds; // 幻影塌陷倒计时
    public float BurnSeconds;            // 燃烧倒计时
}

/// <summary>落格效果解析器：按格子数据 + 局内状态给出效果参数</summary>
public interface ITileEffectResolver
{
    TileEffectResult ResolveLanding(GeneratedTile tile, RunStateSnapshot runState);
}
