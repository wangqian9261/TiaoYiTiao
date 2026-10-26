using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 关卡运行时门面（纯静态，无管理器生命周期）：
/// - 装载最新受支持的关卡包（索引指向 latest；schemaVersion 超前 → 阻断开局并提示更新；
///   回滚 = 编辑器把索引 latest 指回旧版本，新对局即用回滚版本，进行中的对局持有原计划对象不受影响）。
/// - 玩家关卡进度（当前顺序位 / 累计首次通关数 / 单关个人最高分；DataMgr XOR 落盘）。
/// - 新对局计划解析（试玩注入优先 → 包内进度关卡；无包 → null，JumpGame 回退旧无限模式）。
/// - 编辑器试玩桥：SessionState 携带草稿关卡 JSON，免体力直入，不写进度、不落正式资产。
/// </summary>
public static class LevelRuntime
{
    public enum PackStatus
    {
        NotLoaded,         // 未装载
        Ok,                 // 已装载（可能为空包）
        Missing,            // 无包索引/包文件（回退旧模式）
        SchemaUnsupported,  // 包结构版本超前（阻断开局并提示更新）
        Corrupt,            // 布局哈希不符（拒绝有限模式，防篡改/损坏）
    }

    private static PackStatus _status = PackStatus.NotLoaded;
    private static LevelPack _pack;
    private static LevelProgressData _progress;
    private static LevelEconomyConfig _economy;
    private static bool _economyTried;

    public static PackStatus Status => _status;
    public static LevelPack Pack => _pack;
    public static bool HasFiniteLevels => _status == PackStatus.Ok && _pack != null && _pack.LevelCount > 0;
    public static int SupportedSchemaVersion => LevelPackIo.SchemaVersion;

    // ---------- 包装载 ----------

    /// <summary>确保关卡包已装载（幂等；返回是否有可玩有限关卡）</summary>
    public static async UniTask<bool> EnsureLoadedAsync()
    {
        if (_status != PackStatus.NotLoaded) return HasFiniteLevels;

        var index = await DataMgr.Instance.LoadConfigAsync<PackIndexData>(LevelPackIo.IndexAssetKey);
        if (index == null || index.entries == null || index.entries.Length == 0 || string.IsNullOrEmpty(index.latest))
        {
            _status = PackStatus.Missing;
            return false;
        }

        // 索引 latest 即当前生效版本；结构版本超前 → 阻断（不静默回退旧包）
        PackIndexEntryData target = SelectPackEntry(index);
        if (index.schemaVersion != SupportedSchemaVersion || target == null || target.schemaVersion != SupportedSchemaVersion)
        {
            _status = PackStatus.SchemaUnsupported;
            Debug.LogWarning($"[LevelRuntime] 关卡包版本不受支持（latest={index.latest}），需更新游戏");
            return false;
        }

        var data = await DataMgr.Instance.LoadConfigAsync<LevelPackData>(LevelPackIo.PackAssetKey(index.latest));
        if (data == null || data.levels == null || data.levels.Length == 0)
        {
            _status = PackStatus.Missing;
            return false;
        }

        var pack = ParsePack(data, index.latest, target.sha256);
        if (pack == null)
        {
            _status = PackStatus.Corrupt;
            Debug.LogError("[LevelRuntime] 关卡包布局哈希校验失败，拒绝有限关卡模式");
            return false;
        }
        _pack = pack;
        _status = PackStatus.Ok;
        if (_pack.LevelCount == 0)
            Debug.LogWarning("[LevelRuntime] 关卡包内没有启用关卡");
        return HasFiniteLevels;
    }

    private static LevelPack ParsePack(LevelPackData data, string version, string sha256)
    {
        if (data == null || data.schemaVersion != SupportedSchemaVersion || data.levels == null || data.levelCount != data.levels.Length) return null;
        if (data.packVersion != version || string.IsNullOrEmpty(sha256) ||
            LevelPackIo.ComputeSha256(LevelPackIo.ToJson(data)) != sha256) return null;
        var ids = new HashSet<int>();
        // 每关 layoutHash 重算复核（运行时不重新生成，只读烘焙路线 + 完整性校验）
        foreach (var d in data.levels)
        {
            if (d == null || !ids.Add(d.levelId)) return null;
            if (!LevelPackIo.VerifyPackedLevel(d))
            {
                Debug.LogError($"[LevelRuntime] 关卡 {d.levelId} layoutHash 不一致（文件损坏或被篡改）");
                return null;
            }
        }
        var pack = new LevelPack
        {
            Data = data,
            Version = version,
            SchemaVersion = data.schemaVersion,
            Sha256 = sha256,
        };
        var packed = new List<PackedLevelData>(data.levels);
        packed.Sort((a, b) =>
        {
            int c = a.chapterId.CompareTo(b.chapterId);
            return c != 0 ? c : a.order.CompareTo(b.order);
        });
        foreach (var d in packed)
        {
            if (!d.enabled) continue;
            var level = LevelPackIo.ToGeneratedLevel(d);
            if (level != null) pack.Levels.Add(level);
        }
        return pack;
    }

    /// <summary>预热（不阻塞调用方；主页等早期场景调用可隐藏首次装载延迟）</summary>
    public static void Preload()
    {
        EnsureLoadedAsync().Forget();
    }

    /// <summary>
    /// 从包索引选择当前生效版本条目（latest 精确匹配；条目 schemaVersion 超前时仍返回该条目，
    /// 由调用方判定 SchemaUnsupported——不支持的结构版本必须阻断开局并提示更新）。
    /// 返回 null 表示索引无 latest 对应条目。
    /// </summary>
    public static PackIndexEntryData SelectPackEntry(PackIndexData index)
    {
        if (index == null || index.entries == null || index.entries.Length == 0
            || string.IsNullOrEmpty(index.latest)) return null;
        for (int i = 0; i < index.entries.Length; i++)
            if (index.entries[i].version == index.latest) return index.entries[i];
        return null;
    }

    // ---------- 经济配置 ----------

    /// <summary>全局经济配置（缺失回退代码默认值）</summary>
    public static async UniTask<LevelEconomyConfig> LoadEconomyAsync()
    {
        if (_economy != null) return _economy;
        if (!_economyTried)
        {
            _economyTried = true;
            var cfg = await ResMgr.Instance.LoadAssetAsync<LevelEconomyConfig>("Config/LevelEconomyConfig");
            _economy = cfg != null ? cfg : LevelEconomyConfig.Fallback;
            if (cfg == null) Debug.LogWarning("[LevelRuntime] LevelEconomyConfig 缺失，使用代码默认经济值");
        }
        return _economy;
    }

    /// <summary>已装载的经济配置（可能为 null——未装载时；面板显示用）</summary>
    public static LevelEconomyConfig Economy => _economy;

    // ---------- 进度 ----------

    public static LevelProgressData Progress
    {
        get
        {
            if (_progress == null)
                _progress = DataMgr.Instance.Load<LevelProgressData>(LevelProgressData.SaveKey, true)
                            ?? new LevelProgressData();
            return _progress;
        }
    }

    public static int CurrentSeq
    {
        get
        {
            int max = HasFiniteLevels ? _pack.LevelCount : 1;
            return Mathf.Clamp(Progress.seq, 1, Mathf.Max(1, max));
        }
    }

    public static int CumulativeClears => Progress.cumulativeClears;

    /// <summary>单关进度记录（无则空记录）</summary>
    public static LevelRecord GetRecord(int levelId)
    {
        var records = Progress.records;
        if (records != null)
            for (int i = 0; i < records.Length; i++)
                if (records[i].levelId == levelId)
                    return new LevelRecord { LevelId = levelId, BestScore = records[i].bestScore, Cleared = records[i].cleared };
        return new LevelRecord { LevelId = levelId, BestScore = 0, Cleared = false };
    }

    private static void SaveProgress()
    {
        DataMgr.Instance.Save(LevelProgressData.SaveKey, Progress, true);
    }

    public static void SetSeq(int seq)
    {
        var p = Progress;
        p.seq = Mathf.Max(1, seq);
        SaveProgress();
    }

    public static void ResetProgress()
    {
        DataMgr.Instance.Delete(LevelProgressData.SaveKey);
        _progress = null;
    }

    // ---------- 显示名 ----------

    public static string DisplayNameOf(GeneratedLevel level, int seq)
    {
        string theme = "";
        var names = JumpConfig.LevelThemeNames;
        if (names.Length > 0)
            theme = names[((level.Definition.ThemeId % names.Length) + names.Length) % names.Length];
        return $"第 {seq} 关 · {theme}";
    }

    /// <summary>主页铭牌用：当前进度关卡名（无包返回 null，调用方回退旧模式文案）</summary>
    public static async UniTask<string> GetCurrentLevelDisplayNameAsync()
    {
        if (!await EnsureLoadedAsync() || !HasFiniteLevels) return null;
        var level = _pack.Levels[CurrentSeq - 1];
        return DisplayNameOf(level, CurrentSeq);
    }

    /// <summary>结果页用：通关后下一关名（已是最后一关返回 null）</summary>
    public static string NextLevelDisplayName(int playedSeq)
    {
        if (!HasFiniteLevels) return null;
        int next = playedSeq + 1;
        if (next > _pack.LevelCount) return null;
        return DisplayNameOf(_pack.Levels[next - 1], next);
    }

    // ---------- 新对局计划 ----------

    /// <summary>解析新对局计划：编辑器试玩注入优先 → 包内进度关卡；无可用包返回 null（回退旧模式）</summary>
    public static async UniTask<LevelRunPlan> ResolvePlanForNewRunAsync()
    {
#if UNITY_EDITOR
        var playtest = TryBuildPlaytestPlan();
        if (playtest != null) return playtest;
#endif
        if (!await EnsureLoadedAsync() || !HasFiniteLevels) return null;
        int seq = CurrentSeq;
        return BuildPlan(_pack.Levels[seq - 1], seq, false);
    }

    public static LevelRunPlan BuildPlan(GeneratedLevel level, int seq, bool isPlaytest)
    {
        if (level == null || !new LevelValidator().Validate(level).Pass) return null;
        int n = level.Tiles.Count;
        var plan = new LevelRunPlan
        {
            IsPlaytest = isPlaytest,
            LevelId = level.Definition.LevelId,
            Seq = seq,
            DisplayName = DisplayNameOf(level, seq),
            Tier = level.Definition.Difficulty,
            ThemeId = level.Definition.ThemeId,
            ClearCoinMult = DifficultyProfiles.Get(level.Definition.Difficulty).ClearCoinMult,
            TileCount = n,
            EndIndex = n - 1,
            TargetJumps = n - 1,
            Source = level,
            Positions = new Vector3[n],
            Sizes = new float[n],
            ArrivalAxis = new TileAxis[n],
            Kinds = new TileKind[n],
            EffectLevelIds = new int[n],
        };
        for (int i = 0; i < n; i++)
        {
            var t = level.Tiles[i];
            plan.Positions[i] = new Vector3(t.X, 0f, t.Z);
            plan.Sizes[i] = t.Size;
            plan.ArrivalAxis[i] = t.ArrivalAxis;
            plan.Kinds[i] = t.Kind;
            plan.EffectLevelIds[i] = t.EffectLevelId;
        }
        return plan;
    }

    // ---------- 结算 ----------

    /// <summary>
    /// 上报单局：重复通关只更新更高的个人分数并照常发放可重复奖励；
    /// 首次通关才使累计通关数 +1（排行榜口径）；通关推进顺序位。
    /// 试玩局不写进度。返回 null 表示未入账（试玩/无计划）。
    /// </summary>
    public static LevelResult ReportRun(LevelRunPlan plan, int score, int coinsGained, bool cleared)
    {
        if (plan == null || plan.IsPlaytest || !HasFiniteLevels) return null;

        var prog = Progress;
        var list = prog.records != null ? new List<LevelRecordData>(prog.records) : new List<LevelRecordData>();
        LevelRecordData rec = null;
        for (int i = 0; i < list.Count; i++)
            if (list[i].levelId == plan.LevelId) { rec = list[i]; break; }
        if (rec == null)
        {
            rec = new LevelRecordData { levelId = plan.LevelId, bestScore = 0, cleared = false };
            list.Add(rec);
        }

        var result = new LevelResult
        {
            LevelId = plan.LevelId,
            Seq = plan.Seq,
            Score = score,
            CoinsGained = coinsGained,
            Cleared = cleared,
            NewBest = score > rec.bestScore,
            FirstClear = cleared && !rec.cleared,
        };
        if (result.NewBest) rec.bestScore = score;
        if (result.FirstClear)
        {
            rec.cleared = true;
            prog.cumulativeClears++;
        }
        if (cleared && plan.Seq < _pack.LevelCount)
            prog.seq = plan.Seq + 1;
        prog.records = list.ToArray();
        SaveProgress();
        return result;
    }

    /// <summary>当前进度关卡的 LevelId（调试/结果页）</summary>
    public static int CurrentLevelId => HasFiniteLevels ? _pack.Levels[CurrentSeq - 1].Definition.LevelId : -1;

    // ---------- 编辑器试玩桥（SessionState；不生成/不保存临时正式资产） ----------

#if UNITY_EDITOR
    private const string PlaytestJsonKey = "LevelSystem.Playtest.LevelJson";
    private const string PlaytestAutoStartKey = "LevelSystem.Playtest.AutoStart";

    /// <summary>编辑器写入试玩草稿（PackedLevelData JSON）：本 Play 会话内所有对局注入该草稿</summary>
    public static void RequestPlaytest(string packedLevelJson)
    {
        UnityEditor.SessionState.SetString(PlaytestJsonKey, packedLevelJson ?? "");
        UnityEditor.SessionState.SetBool(PlaytestAutoStartKey, true);
    }

    /// <summary>本 Play 会话是否携带试玩草稿</summary>
    public static bool HasPlaytestDraft
        => !string.IsNullOrEmpty(UnityEditor.SessionState.GetString(PlaytestJsonKey, ""));

    /// <summary>退出 Play 时由编辑器引导器调用：清空草稿，避免下次手动开局误入</summary>
    public static void ClearPlaytest()
    {
        UnityEditor.SessionState.SetString(PlaytestJsonKey, "");
        UnityEditor.SessionState.SetBool(PlaytestAutoStartKey, false);
    }

    /// <summary>主页 OnOpen 时调用：有试玩自动开局请求则免体力直入跳一跳（请求一次性消费）</summary>
    public static bool TryAutoStartPlaytest()
    {
        if (!UnityEditor.SessionState.GetBool(PlaytestAutoStartKey, false)) return false;
        UnityEditor.SessionState.SetBool(PlaytestAutoStartKey, false);
        Debug.Log("[LevelRuntime] 编辑器试玩：注入草稿关卡并直入游戏");
        JumpGame.StartGame();
        return true;
    }

    private static LevelRunPlan TryBuildPlaytestPlan()
    {
        string json = UnityEditor.SessionState.GetString(PlaytestJsonKey, "");
        if (string.IsNullOrEmpty(json)) return null;
        var packed = LevelPackIo.FromJson<PackedLevelData>(json);
        var level = LevelPackIo.VerifyPackedLevel(packed) ? LevelPackIo.ToGeneratedLevel(packed) : null;
        if (level == null)
        {
            Debug.LogWarning("[LevelRuntime] 试玩草稿解析失败，回退正式流程");
            return null;
        }
        return BuildPlan(level, 1, true);
    }
#endif
}

/// <summary>有限关卡运行计划（JumpGame 世界构建依据；一次对局持有同一计划对象——回滚语义基石）</summary>
public sealed class LevelRunPlan
{
    public bool IsPlaytest;
    public int LevelId;
    public int Seq;                 // 包内顺序位（1 起；HUD 关号）
    public string DisplayName;      // "第 N 关 · 主题名"
    public DifficultyTier Tier;
    public int ThemeId;
    public float ClearCoinMult;     // 通关金币倍率（结算 = clearCoinBase × 此值）
    public int TileCount;
    public int EndIndex;            // 终点格索引
    public int TargetJumps;         // 通关所需跳数（格数-1；HUD 进度分母）
    public Vector3[] Positions;     // 烘焙格心位置
    public float[] Sizes;
    public TileAxis[] ArrivalAxis;  // ArrivalAxis[i] = 跳入格 i 的行进轴
    public TileKind[] Kinds;
    public int[] EffectLevelIds;
    public GeneratedLevel Source;

    /// <summary>格 i 顶心（y=0 平面）</summary>
    public Vector3 Position(int i) => Positions[i];
    public float Size(int i) => Sizes[i];
    /// <summary>从格 i 起跳的行进轴</summary>
    public TileAxis TravelAxis(int i) => ArrivalAxis[Mathf.Min(i + 1, TileCount - 1)];
    /// <summary>格 i 的领域瓦片（效果解析用）</summary>
    public GeneratedTile Tile(int i) => Source.Tiles[i];
}
