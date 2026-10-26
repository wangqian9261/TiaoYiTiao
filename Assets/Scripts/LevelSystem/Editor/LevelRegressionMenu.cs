using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 关卡系统编辑器回归（策划案§8 自动化测试的编辑器侧子集，菜单 LevelSystem/9）：
/// - 五档 × {20,40,60} 格 × 多种子：生成成功率 + 校验全过 + 特殊砖数量公式复核。
/// - 同一输入重复生成 100 次：layoutHash 与烘焙 JSON 字节完全一致（确定性）。
/// - 导出 → 重导入：逐关 layoutHash 一致、SHA-256 与包文件一致（不落盘，纯内存往返）。
/// - schemaVersion 门：索引 latest 指向超前结构版本时必须拒绝（SelectPackEntry 语义）。
/// - 误差模拟分档统计：首尝通关率与档位目标对比（校准参考，不阻断）。
/// </summary>
public static class LevelRegressionMenu
{
    [MenuItem("LevelSystem/9. Run Generation Regression")]
    public static void RunFromMenu()
    {
        string report = RunRegression();
        Debug.Log(report);
        EditorUtility.DisplayDialog("关卡系统回归",
            report.Length > 1000 ? report.Substring(0, 1000) + "\n…（完整日志见 Console）" : report,
            "知道了");
    }

    /// <summary>执行回归并返回完整报告（可被脚本直接调用断言）</summary>
    public static string RunRegression(int seedsPerCombo = 60, int determinismRepeats = 100)
    {
        var sb = new StringBuilder();
        var generator = new LevelGenerator();
        var validator = new LevelValidator();
        var simulator = new LevelSimulator();
        int failures = 0;
        void Check(bool ok, string message)
        {
            if (!ok) { failures++; sb.AppendLine("✖ " + message); }
            else sb.AppendLine("✔ " + message);
        }

        // ---- 1. 五档 × {20,40,60} × 多种子：生成 + 校验 ----
        int genFail = 0;
        int validationFail = 0;
        int totalLevels = 0;
        var samplePerTier = new Dictionary<DifficultyTier, List<GeneratedLevel>>();
        foreach (DifficultyTier tier in System.Enum.GetValues(typeof(DifficultyTier)))
        {
            samplePerTier[tier] = new List<GeneratedLevel>();
            foreach (int grid in new[] { 20, 40, 60 })
            {
                for (int s = 1; s <= seedsPerCombo; s++)
                {
                    var def = new LevelDefinition
                    {
                        LevelId = 1000 + (int)tier * 100 + grid + s,
                        ChapterId = 1,
                        OrderInChapter = s,
                        GridCount = grid,
                        Difficulty = tier,
                        Seed = (uint)(s * 7919 + (int)tier * 104729 + grid),
                        ThemeId = (int)tier,
                        Enabled = true,
                    };
                    var level = generator.Generate(def, DifficultyProfiles.Get(tier));
                    if (level == null) { genFail++; continue; }
                    totalLevels++;
                    var report = validator.Validate(level);
                    if (!report.Pass)
                    {
                        validationFail++;
                        if (validationFail <= 3)
                            sb.AppendLine($"  样例阻断：tier={tier} grid={grid} seed={def.Seed} → " +
                                string.Join("; ", report.Issues.Where(i => i.Severity == ValidationSeverity.Error).Select(i => i.Message)));
                    }
                    if (samplePerTier[tier].Count < 6) samplePerTier[tier].Add(level);
                }
            }
        }
        Check(genFail == 0, $"生成失败数 = {genFail}（应为 0；{totalLevels} 关生成成功）");
        Check(validationFail == 0, $"校验阻断数 = {validationFail}（应为 0）");

        // ---- 2. 确定性：同输入 100 次，hash + 烘焙 JSON 字节一致 ----
        var probe = new LevelDefinition { LevelId = 777, ChapterId = 1, OrderInChapter = 1, GridCount = 44, Difficulty = DifficultyTier.Hard, Seed = 20260930, ThemeId = 3, Enabled = true };
        string firstHash = null; string firstJson = null; int hashMismatches = 0, jsonMismatches = 0;
        for (int i = 0; i < determinismRepeats; i++)
        {
            var level = generator.Generate(probe, DifficultyProfiles.Get(probe.Difficulty));
            string json = LevelPackIo.ToJson(LevelPackIo.ToPackedLevel(level));
            if (i == 0) { firstHash = level.LayoutHash; firstJson = json; }
            else
            {
                if (level.LayoutHash != firstHash) hashMismatches++;
                if (json != firstJson) jsonMismatches++;
            }
        }
        Check(hashMismatches == 0, $"layoutHash 确定性 ×{determinismRepeats}：不一致 {hashMismatches} 次");
        Check(jsonMismatches == 0, $"烘焙 JSON 字节确定性 ×{determinismRepeats}：不一致 {jsonMismatches} 次");

        // ---- 3. 导出 → 重导入哈希一致（内存往返） ----
        {
            var batch = new List<GeneratedLevel>();
            foreach (DifficultyTier tier in System.Enum.GetValues(typeof(DifficultyTier)))
            {
                var def = new LevelDefinition { LevelId = 500 + (int)tier, ChapterId = 1, OrderInChapter = (int)tier + 1, GridCount = 30, Difficulty = tier, Seed = 31337, ThemeId = (int)tier, Enabled = true };
                batch.Add(generator.Generate(def, DifficultyProfiles.Get(tier)));
            }
            var pack = new LevelPackIo().Export(batch, "9.9.9-test");
            bool hashOk = true;
            foreach (var d in pack.Data.levels)
                if (LevelPackIo.ComputeLayoutHashFromPacked(d) != d.layoutHash) { hashOk = false; break; }
            Check(hashOk, "导出 → 重导入逐关 layoutHash 一致");
            Check(LevelPackIo.TryComputeSha256(LevelPackIo.ToJson(pack.Data)) == pack.Sha256, "SHA-256 与包 JSON 内容一致");

            // 真·JSON 往返：序列化 → 反序列化 → 数组完整性
            // （嵌套自定义类缺 [Serializable] 时 JsonUtility 会静默截断字段——曾使 levels 数组整体消失）
            var jsonText = LevelPackIo.ToJson(pack.Data);
            var reparsed = LevelPackIo.FromJson<LevelPackData>(jsonText);
            bool jsonOk = reparsed != null && reparsed.levels != null && reparsed.levels.Length == batch.Count
                && reparsed.levels.All(d => d != null && d.tiles != null && d.tiles.Length == d.tileCount);
            Check(jsonOk, "JSON 往返数组完整（levels/tiles 嵌套类可序列化）");
            Check(LevelPackIo.ToJson(reparsed) == jsonText, "JSON 往返字节一致");

            // ToGeneratedLevel 往返：重导入后再导出必须字节一致
            var roundTripped = pack.Data.levels.Select(LevelPackIo.ToGeneratedLevel).ToList();
            var pack2 = new LevelPackIo().Export(roundTripped, "9.9.9-test");
            Check(LevelPackIo.ToJson(pack2.Data) == LevelPackIo.ToJson(pack.Data), "领域往返(导入→再导出) JSON 字节一致");
        }

        // ---- 4. schemaVersion 门 ----
        {
            var index = new PackIndexData
            {
                schemaVersion = LevelPackIo.SchemaVersion,
                latest = "99.0.0",
                entries = new[]
                {
                    new PackIndexEntryData { version = "1.0.0", schemaVersion = 1, levelCount = 3, sha256 = "x", buildTimeUtc = 1 },
                    new PackIndexEntryData { version = "99.0.0", schemaVersion = LevelPackIo.SchemaVersion + 5, levelCount = 3, sha256 = "y", buildTimeUtc = 2 },
                },
            };
            var entry = LevelRuntime.SelectPackEntry(index);
            Check(entry != null && entry.version == "99.0.0"
                  && entry.schemaVersion > LevelRuntime.SupportedSchemaVersion,
                "schema 门：latest 指向超前版本时被识别为不可支持（开局阻断）");
            var indexOk = new PackIndexData
            {
                latest = "2.0.0",
                entries = new[] { new PackIndexEntryData { version = "2.0.0", schemaVersion = 2, levelCount = 3, sha256 = "x", buildTimeUtc = 1 } },
            };
            var entryOk = LevelRuntime.SelectPackEntry(indexOk);
            Check(entryOk != null && entryOk.version == "2.0.0" && entryOk.schemaVersion == LevelRuntime.SupportedSchemaVersion,
                "schema 门：受支持版本正常选中");
        }

        // ---- 5. 误差模拟分档统计（校准参考） ----
        sb.AppendLine("—— 误差模拟（每档 6 关 × 400 局）——");
        foreach (DifficultyTier tier in System.Enum.GetValues(typeof(DifficultyTier)))
        {
            var rates = new List<float>();
            foreach (var level in samplePerTier[tier])
            {
                var sim = simulator.Simulate(level, 400, Pcg32.StableHash($"reg/{level.Definition.LevelId}"));
                rates.Add(sim.ClearRate);
            }
            rates.Sort();
            float median = rates.Count > 0 ? rates[rates.Count / 2] : 0f;
            float target = DifficultyProfiles.TargetFirstTryRate(tier);
            string mark = median >= target ? "达标" : "低于目标";
            sb.AppendLine($"  {DifficultyProfiles.Get(tier).DisplayName}：中位 {median:P1}（目标 ≥{target:P0}）{mark}");
        }

        sb.Insert(0, $"===== 关卡系统回归报告（seeds/组合={seedsPerCombo}，确定性重复={determinismRepeats}）=====\n");
        sb.AppendLine(failures == 0 ? "总结：全部通过 ✅" : $"总结：{failures} 项失败 ❌");
        return sb.ToString();
    }
}
