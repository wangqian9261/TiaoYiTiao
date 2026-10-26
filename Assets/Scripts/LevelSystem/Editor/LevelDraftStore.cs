using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 关卡草稿存储：Assets/GameRes/Config/Levels/Drafts 每关一份 JSON（LevelDefinitionData）。
/// 编辑器只读写草稿与发布产物，不直接连接正式数据库；正式关卡包经烘焙发布。
/// </summary>
public static class LevelDraftStore
{
    public const string DraftDir = "Assets/GameRes/Config/Levels/Drafts";

    public static string DraftPath(int levelId) => $"{DraftDir}/level_{levelId:D3}.json";

    /// <summary>全部草稿（按章节 + 章节内顺序排序）</summary>
    public static List<LevelDefinition> LoadAll()
    {
        var result = new List<LevelDefinition>();
        if (!Directory.Exists(DraftDir)) return result;
        foreach (var file in Directory.GetFiles(DraftDir, "level_*.json"))
        {
            var def = LoadAtPath(file);
            if (def != null) result.Add(def);
        }
        result.Sort((a, b) =>
        {
            int c = a.ChapterId.CompareTo(b.ChapterId);
            return c != 0 ? c : a.OrderInChapter.CompareTo(b.OrderInChapter);
        });
        return result;
    }

    public static LevelDefinition Load(int levelId)
    {
        var path = DraftPath(levelId);
        return File.Exists(path) ? LoadAtPath(path) : null;
    }

    private static LevelDefinition LoadAtPath(string path)
    {
        try
        {
            var data = LevelPackIo.FromJson<LevelDefinitionData>(File.ReadAllText(path));
            return LevelPackIo.ToDefinition(data);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[LevelDraftStore] 草稿解析失败: {path}\n{e.Message}");
            return null;
        }
    }

    /// <summary>保存草稿（保存当前编辑态；同 id 覆盖）</summary>
    public static void Save(LevelDefinition def)
    {
        EnsureDir();
        File.WriteAllText(DraftPath(def.LevelId), LevelPackIo.ToJson(LevelPackIo.ToDefinitionData(def)));
        AssetDatabase.ImportAsset(DraftPath(def.LevelId));
    }

    public static void Delete(int levelId)
    {
        var path = DraftPath(levelId);
        if (File.Exists(path)) AssetDatabase.DeleteAsset(path);
    }

    public static bool Exists(int levelId) => File.Exists(DraftPath(levelId));

    /// <summary>下一个可用 levelId（新建/复制用）</summary>
    public static int NextLevelId()
    {
        int max = 0;
        foreach (var def in LoadAll()) max = Mathf.Max(max, def.LevelId);
        return max + 1;
    }

    /// <summary>章内下一个顺序位</summary>
    public static int NextOrderInChapter(int chapterId)
    {
        int max = 0;
        foreach (var def in LoadAll())
            if (def.ChapterId == chapterId) max = Mathf.Max(max, def.OrderInChapter);
        return max + 1;
    }

    public static void EnsureDir()
    {
        if (!Directory.Exists(DraftDir)) Directory.CreateDirectory(DraftDir);
        if (!Directory.Exists(LevelBakePipeline.PackDir)) Directory.CreateDirectory(LevelBakePipeline.PackDir);
        AssetDatabase.Refresh();
    }
}

/// <summary>
/// 发布管线（策划案§2 发布流程）：
/// 保存草稿 → 生成路线 → 阻断校验 → 1000 次模拟 →（编辑器试玩）→ 烘焙 JSON → 重新导入校验 → 生成 SHA-256。
/// 全包原子替换；旧包文件保留（进行中的关卡 / 回放 / 快速回滚）；回滚 = 索引 latest 指回旧版本。
/// </summary>
public static class LevelBakePipeline
{
    public const string PackDir = "Assets/GameRes/Config/Levels/Pack";

    public class BakeResult
    {
        public bool Success;
        public string PackVersion;
        public int LevelCount;
        public string PackSha256;
        public string LayoutHashSample;
        public List<string> Steps = new List<string>();
        public List<string> Failures = new List<string>();
        public List<string> SimSummary = new List<string>();
    }

    /// <summary>烘焙全部启用草稿为版本化关卡包（含全部阻断校验与重导入校验）</summary>
    public static BakeResult BakeAll(string packVersion, System.Action<string> progress = null)
    {
        var result = new BakeResult { PackVersion = packVersion };
        void Step(string msg)
        {
            result.Steps.Add(msg);
            progress?.Invoke(msg);
        }

        if (string.IsNullOrEmpty(packVersion))
        {
            result.Failures.Add("包版本号为空");
            return result;
        }

        // ---- 1. 草稿 ----
        Step("读取草稿");
        var drafts = LevelDraftStore.LoadAll().Where(d => d.Enabled).ToList();
        if (drafts.Count == 0)
        {
            result.Failures.Add("没有启用的关卡草稿");
            return result;
        }
        var ids = new HashSet<int>();
        foreach (var d in drafts)
            if (!ids.Add(d.LevelId))
            {
                result.Failures.Add($"levelId 重复: {d.LevelId}（发布阻断）");
                return result;
            }

        // ---- 2. 生成 + 3. 阻断校验 ----
        var generator = new LevelGenerator();
        var validator = new LevelValidator();
        var levels = new List<GeneratedLevel>();
        for (int i = 0; i < drafts.Count; i++)
        {
            var def = drafts[i];
            progress?.Invoke($"生成关卡 {def.LevelId}（{i + 1}/{drafts.Count}）");
            var level = generator.Generate(def, DifficultyProfiles.Get(def.Difficulty));
            if (level == null)
            {
                result.Failures.Add($"关卡 {def.LevelId} 生成失败（100 次尝试均不可达/约束冲突）：请更换 Seed");
                return result;
            }
            var report = validator.Validate(level, GlobalRewardConfig.Default, GlobalTrapConfig.Default);
            if (!report.Pass)
            {
                foreach (var issue in report.Issues)
                    if (issue.Severity == ValidationSeverity.Error)
                        result.Failures.Add($"关卡 {def.LevelId} 校验失败 [{issue.Code}] {issue.Message}");
                if (result.Failures.Count > 0) return result;
            }
            levels.Add(level);
        }
        Step($"生成+校验 {levels.Count} 关全部通过");

        // ---- 4. 1000 次模拟（不阻断；低于档位目标给警告供策划定夺） ----
        var simulator = new LevelSimulator();
        for (int i = 0; i < levels.Count; i++)
        {
            var level = levels[i];
            progress?.Invoke($"误差模拟 {level.Definition.LevelId}（{i + 1}/{levels.Count}）");
            var sim = simulator.Simulate(level, simulator.DefaultRuns,
                Pcg32.StableHash($"bake/{packVersion}/{level.Definition.LevelId}"));
            float target = DifficultyProfiles.TargetFirstTryRate(level.Definition.Difficulty);
            string line = $"关卡 {level.Definition.LevelId}（{DifficultyProfiles.Get(level.Definition.Difficulty).DisplayName}）" +
                          $"首尝通关率 {sim.ClearRate:P1}（目标 ≥{target:P0}） 平均到达 {sim.AvgMaxIndex:F1}/{level.TileCount - 1}";
            if (sim.ClearRate < target) line += "  ⚠低于目标";
            result.SimSummary.Add(line);
        }
        Step($"{simulator.DefaultRuns} 次模拟完成");

        // ---- 5. 烘焙 JSON（全包原子替换：先写临时文件再改索引；旧包保留） ----
        Step("烘焙关卡包");
        var exporter = new LevelPackIo();
        var pack = exporter.Export(levels, packVersion);
        if (pack == null)
        {
            result.Failures.Add("关卡包导出失败（空包）");
            return result;
        }
        string packJson = LevelPackIo.ToJson(pack.Data);
        string packPath = $"{PackDir}/pack_{packVersion.Replace('.', '_')}.json";
        LevelDraftStore.EnsureDir();
        File.WriteAllText(packPath, packJson);
        File.WriteAllText($"{PackPathSha(packVersion)}", pack.Sha256);
        AssetDatabase.ImportAsset(packPath);
        AssetDatabase.ImportAsset(PackPathSha(packVersion));
        Step($"写包 {packPath}（{pack.Data.levelCount} 关）");

        // ---- 6. 重新导入校验（磁盘重读 → 逐关 layoutHash 复核 + 文件 SHA-256 复核） ----
        Step("重导入校验");
        string reloaded = File.ReadAllText(packPath);
        var reparsed = LevelPackIo.FromJson<LevelPackData>(reloaded);
        if (reparsed == null || reparsed.levels == null || reparsed.levels.Length != levels.Count)
        {
            result.Failures.Add("重导入解析失败或关卡数不一致");
            return result;
        }
        foreach (var d in reparsed.levels)
        {
            if (!LevelPackIo.VerifyPackedLevel(d))
            {
                result.Failures.Add($"重导入后关卡 {d.levelId} layoutHash 不一致（发布阻断）");
                return result;
            }
        }
        string shaOnDisk = LevelPackIo.ComputeSha256(reloaded);
        if (shaOnDisk != pack.Sha256 || File.ReadAllText(PackPathSha(packVersion)).Trim() != shaOnDisk)
        {
            result.Failures.Add("SHA-256 与包文件内容不一致（发布阻断）");
            return result;
        }
        Step($"layoutHash 全部一致 · SHA-256 {shaOnDisk.Substring(0, 16)}…");

        // ---- 7. 更新包索引（latest 指向新版本；旧包条目保留） ----
        Step("更新包索引");
        var index = LoadIndex() ?? NewIndex();
        var entry = new PackIndexEntryData
        {
            version = packVersion,
            schemaVersion = LevelPackIo.SchemaVersion,
            levelCount = levels.Count,
            sha256 = shaOnDisk,
            buildTimeUtc = pack.Data.buildTimeUtc,
        };
        bool replaced = false;
        for (int i = 0; i < index.entries.Length; i++)
            if (index.entries[i].version == packVersion) { index.entries[i] = entry; replaced = true; break; }
        if (!replaced)
        {
            var list = index.entries.ToList();
            list.Add(entry);
            index.entries = list.ToArray();
        }
        index.schemaVersion = LevelPackIo.SchemaVersion;
        index.latest = packVersion;
        index.entryCount = index.entries.Length;
        File.WriteAllText(IndexPath(), LevelPackIo.ToJson(index));
        AssetDatabase.ImportAsset(IndexPath());
        Step($"索引 latest → {packVersion}（共 {index.entryCount} 个版本，旧包保留可回滚）");

        result.Success = true;
        result.LevelCount = levels.Count;
        result.PackSha256 = shaOnDisk;
        result.LayoutHashSample = levels[0].LayoutHash;
        AssetDatabase.SaveAssets();
        return result;
    }

    public static string PackPathSha(string version) => $"{PackDir}/pack_{version.Replace('.', '_')}.sha256.txt";
    public static string IndexPath() => $"{PackDir}/pack_index.json";

    /// <summary>回滚：把索引 latest 指回已存在的旧版本（新对局即用回滚版本）</summary>
    public static bool RollbackTo(string version)
    {
        var index = LoadIndex();
        if (index == null) return false;
        bool found = false;
        foreach (var e in index.entries)
            if (e.version == version) { found = true; break; }
        if (!found) return false;
        index.latest = version;
        File.WriteAllText(IndexPath(), LevelPackIo.ToJson(index));
        AssetDatabase.ImportAsset(IndexPath());
        Debug.Log($"[LevelBakePipeline] 已回滚：latest → {version}");
        return true;
    }

    public static PackIndexData LoadIndex()
    {
        if (!File.Exists(IndexPath())) return null;
        return LevelPackIo.FromJson<PackIndexData>(File.ReadAllText(IndexPath()));
    }

    private static PackIndexData NewIndex()
        => new PackIndexData { schemaVersion = LevelPackIo.SchemaVersion, latest = "", entries = new PackIndexEntryData[0] };

    /// <summary>编辑器内直接装载运行时口径的关卡包（窗口状态栏/试玩校验用）</summary>
    public static LevelPack LoadPackForPreview(string version)
    {
        var path = $"{PackDir}/pack_{version.Replace('.', '_')}.json";
        if (!File.Exists(path)) return null;
        var data = LevelPackIo.FromJson<LevelPackData>(File.ReadAllText(path));
        if (data == null) return null;
        var packed = new List<PackedLevelData>(data.levels);
        packed.Sort((a, b) =>
        {
            int c = a.chapterId.CompareTo(b.chapterId);
            return c != 0 ? c : a.order.CompareTo(b.order);
        });
        var pack = new LevelPack { Data = data, Version = version, SchemaVersion = data.schemaVersion };
        foreach (var d in packed)
        {
            if (!d.enabled) continue;
            var level = LevelPackIo.ToGeneratedLevel(d);
            if (level != null) pack.Levels.Add(level);
        }
        return pack;
    }
}
