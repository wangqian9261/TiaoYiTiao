using System;
using System.Collections.Generic;

/// <summary>
/// 关卡 JSON DTO：全部公开字段/枚举(整数)/数组，不用继承、多态字段或字典——JsonUtility 兼容。
/// 坐标一律整数毫米。领域模型 ↔ DTO 转换集中在 LevelPackIo。
/// 重要：嵌套自定义类必须标注 [Serializable]——否则 JsonUtility 静默丢弃该字段
/// （顶层平铺类可省略，但含嵌套类/数组时缺失会静默截断 JSON，发布重导入校验即兜底）。
/// </summary>

/// <summary>关卡定义草稿（编辑器可编辑；Assets/GameRes/Config/Levels/Drafts 每关一份）</summary>
[Serializable]
public class LevelDefinitionData
{
    public int levelId = 1;
    public int chapterId = 1;
    public int order = 1;
    public int gridCount = 24;         // 20–60，含起点与终点
    public int difficultyTier;         // DifficultyTier
    public long seed;                  // 固定 Seed
    public int themeId;
    public bool enabled = true;
}

/// <summary>烘焙瓦片（整数毫米）</summary>
[Serializable]
public class PackedTileData
{
    public int index;          // 0 起（0=起点；末位=终点）
    public int kind;           // TileKind
    public int arrivalAxis;    // TileAxis（从上一格跳入本格的行进轴；格 0 无意义）
    public int x;              // 毫米
    public int z;              // 毫米
    public int size;           // 顶面边长（毫米）
    public int rewardKind;     // RewardKind（无 = 0）
    public int trapKind;       // TrapKind（无 = 0）
    public long effectSeed;    // 特殊砖效果随机源
    public int shakeOffset;    // 毫米，有符号（本格相对理想落点的横移，由上一格震荡陷阱施加）
}

/// <summary>烘焙关卡（发布产物；layoutHash 锁定布局）</summary>
[Serializable]
public class PackedLevelData
{
    public int levelId;
    public int chapterId;
    public int order;
    public bool enabled;
    public int difficultyTier;
    public int themeId;
    public long seed;
    public int gridCount;
    public string generatorVersion = "";
    public string layoutHash = "";
    public int tileCount;
    public PackedTileData[] tiles;
}

/// <summary>关卡包（全包原子替换；旧包保留用于进行中的关卡、回放与快速回滚）</summary>
[Serializable]
public class LevelPackData
{
    public int schemaVersion;
    public string packVersion = "";
    public long buildTimeUtc;
    public int levelCount;
    public PackedLevelData[] levels;
}

/// <summary>包索引：运行时据此选择最新且受支持的包版本（回滚 = 索引指向旧版本）</summary>
[Serializable]
public class PackIndexEntryData
{
    public string version = "";
    public int schemaVersion;
    public int levelCount;
    public string sha256 = "";    // 包文件内容 SHA-256（重导入校验用）
    public long buildTimeUtc;
}

[Serializable]
public class PackIndexData
{
    public int schemaVersion;
    public string latest = "";
    public int entryCount;
    public PackIndexEntryData[] entries;
}

/// <summary>玩家关卡进度（DataMgr XOR 混淆落盘）</summary>
[Serializable]
public class LevelRecordData
{
    public int levelId;
    public int bestScore;   // 单关个人最高分
    public bool cleared;    // 是否通关过（首次通关时累计通关数 +1）
}

[Serializable]
public class LevelProgressData
{
    public const string SaveKey = "level_progress";

    public int seq = 1;                 // 当前游玩的关卡顺序位（包内按 章节+顺序 排列，1 起）
    public int cumulativeClears;        // 累计首次通关数（排行榜口径；重复通关不增）
    public LevelRecordData[] records = new LevelRecordData[0];
}

// ---------- 领域：关卡包 ----------

/// <summary>装载后的关卡包（DTO + 已解析的可直接运行关卡）</summary>
public class LevelPack
{
    public LevelPackData Data;
    public string Version;
    public int SchemaVersion;
    public string Sha256;
    /// <summary>包内按 (chapterId, order) 排列的启用关卡（运行序列）</summary>
    public List<GeneratedLevel> Levels = new List<GeneratedLevel>();

    public int LevelCount => Levels?.Count ?? 0;
}
