using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 程序化关卡编辑器（策划案§2，IMGUI EditorWindow，菜单 Game/Level Editor）：
/// - 左侧关卡列表：新建 / 复制 / 停用 / 排序 / 章节与难度筛选。
/// - 中间路线预览：格子编号、位置、方向、距离（蓄力毫秒）、类型与触发范围；只读不可拖动；
///   同步 Scene 视图 3D 叠加预览。
/// - 右侧参数：levelId / chapterId / 章节内顺序 / gridCount(20–60) / 难度档 / Seed / 主题 / enabled。
/// - 底部操作：生成预览 / 更换 Seed / 校验关卡 / 玩家误差模拟 / 试玩当前关 / 保存草稿 / 烘焙并导出 JSON。
/// - 试玩经 SessionState 传递关卡（不生成/不保存临时正式资产）；烘焙走发布管线
///   （草稿→生成→阻断校验→1000 次模拟→烘焙→重导入校验→SHA-256），全包原子替换、旧包保留可回滚。
/// </summary>
public class LevelEditorWindow : EditorWindow
{
    private static readonly string[] TierNames =
        { "简单", "普通", "困难", "专家", "大师" };

    private LevelGenerator _generator;
    private Vector2 _paramsScroll;
    private int _selectedTile = 6;
    private readonly LevelValidator _validator = new LevelValidator();
    private readonly LevelSimulator _simulator = new LevelSimulator();

    private List<LevelDefinition> _drafts = new List<LevelDefinition>();
    private LevelDefinition _selected;
    private bool _dirty;
    private GeneratedLevel _preview;
    private ValidationReport _report;
    private LevelSimulator.SimResult _sim;
    private string _bakeLog = "";

    private Vector2 _listScroll;
    private Vector2 _reportScroll;
    private int _chapterFilter = -1;      // -1 = 全部
    private int _tierFilter = -1;        // -1 = 全部
    private string _packVersionField = "";
    private bool _scenePreviewFold = true;

    [MenuItem("Game/Level Editor")]
    public static void Open()
    {
        var win = GetWindow<LevelEditorWindow>("关卡编辑器");
        win.minSize = new Vector2(1180f, 640f);
        win.Reload();
    }

    [MenuItem("LevelSystem/1. Ensure Economy Config")]
    public static void EnsureEconomyConfig()
    {
        const string path = "Assets/GameRes/Config/LevelEconomyConfig.asset";
        var cfg = AssetDatabase.LoadAssetAtPath<LevelEconomyConfig>(path);
        if (cfg == null)
        {
            if (!System.IO.Directory.Exists("Assets/GameRes/Config"))
                System.IO.Directory.CreateDirectory("Assets/GameRes/Config");
            cfg = CreateInstance<LevelEconomyConfig>();
            AssetDatabase.CreateAsset(cfg, path);
            Debug.Log("[LevelSystem] 已创建全局经济配置 LevelEconomyConfig.asset");
        }
        Selection.activeObject = cfg;
        EditorGUIUtility.PingObject(cfg);
    }

    // ---------- 生命周期 ----------

    private void OnEnable()
    {
        Reload();
        EnsureEconomyConfig();
    }

    private string _configJson;
    private void OnInspectorUpdate()
    {
        string json = JsonUtility.ToJson(SpecialTileEditorConfig.LoadSettings());
        if (json == _configJson) return;
        _configJson = json;
        Regenerate();
    }

    private void OnDisable()
    {
        LevelScenePreview.SetLevel(null);
    }

    private void Reload()
    {
        _drafts = LevelDraftStore.LoadAll();
        if (_selected != null)
        {
            var fresh = _drafts.FirstOrDefault(d => d.LevelId == _selected.LevelId);
            _selected = fresh; // 丢弃已删除项
        }
        if (_selected == null) _selected = _drafts.FirstOrDefault();
        RefreshPreview();
    }

    /// <summary>从当前编辑态重新生成预览（未保存的修改立即生效）</summary>
    private void Regenerate(bool reselectScene = true)
    {
        if (_selected == null) { _preview = null; _report = null; _sim = default; return; }
        _generator = new LevelGenerator(SpecialTileEditorConfig.LoadSettings());
        _preview = _generator.Generate(_selected, DifficultyProfiles.Get(_selected.Difficulty));
        if (_preview == null)
        {
            _report = _generator.LastReport;
            _sim = default;
        }
        else
        {
            _report = _validator.Validate(_preview);
            _sim = default; // 数值变动后旧模拟结果作废
        }
        if (reselectScene) LevelScenePreview.SetLevel(_preview);
        Repaint();
    }

    private void RefreshPreview()
    {
        // 列表重载后仅同步已保存关卡对应的预览（编辑未保存项由显式操作重建）
        Regenerate(reselectScene: false);
        LevelScenePreview.SetLevel(_preview);
    }

    // ---------- 布局 ----------

    private void OnGUI()
    {
        DrawToolbar();

        using (new EditorGUILayout.HorizontalScope())
        {
            DrawListPanel(250f);
            DrawPreviewPanel();
            DrawParamsPanel(320f);
        }
        DrawBottomPanel();
    }

    private void DrawToolbar()
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            // 章节筛选
            var chapters = _drafts.Select(d => d.ChapterId).Distinct().OrderBy(c => c).ToList();
            var chapterOptions = new List<string> { "全部章节" };
            chapterOptions.AddRange(chapters.Select(c => $"章节 {c}"));
            int chapterIdx = _chapterFilter < 0 ? 0 : chapters.IndexOf(_chapterFilter) + 1;
            int newIdx = EditorGUILayout.Popup(chapterIdx, chapterOptions.ToArray(),
                GUILayout.Width(110f));
            _chapterFilter = newIdx == 0 ? -1 : chapters[newIdx - 1];

            // 难度筛选
            var tierOptions = new List<string> { "全部难度" };
            tierOptions.AddRange(TierNames);
            _tierFilter = EditorGUILayout.Popup(_tierFilter + 1, tierOptions.ToArray(),
                GUILayout.Width(100f)) - 1;

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("新建", EditorStyles.toolbarButton)) OnCreate();
            if (GUILayout.Button("复制", EditorStyles.toolbarButton)) OnDuplicate();
            GUI.enabled = _selected != null;
            if (GUILayout.Button(_selected != null && _selected.Enabled ? "停用" : "启用", EditorStyles.toolbarButton))
                OnToggleEnabled();
            GUI.enabled = true;

            GUILayout.Space(12f);
            var index = LevelBakePipeline.LoadIndex();
            GUILayout.Label(index != null
                    ? $"当前包：v{index.latest}（{index.entryCount} 版 · {CountEnabledOfLatest(index)} 关启用）"
                    : "当前包：无（未烘焙）",
                EditorStyles.miniLabel);
        }
    }

    private static int CountEnabledOfLatest(PackIndexData index)
    {
        var pack = index != null ? LevelBakePipeline.LoadPackForPreview(index.latest) : null;
        return pack?.LevelCount ?? 0;
    }

    // ---------- 左：关卡列表 ----------

    private void DrawListPanel(float width)
    {
        using (new EditorGUILayout.VerticalScope(GUILayout.Width(width)))
        {
            _listScroll = EditorGUILayout.BeginScrollView(_listScroll);
            var filtered = FilteredDrafts();
            foreach (var def in filtered)
            {
                bool selected = _selected != null && def.LevelId == _selected.LevelId;
                var style = selected ? EditorStyles.whiteBoldLabel : EditorStyles.label;
                using (new EditorGUILayout.HorizontalScope())
                {
                    Color keep = GUI.color;
                    if (!def.Enabled) GUI.color = new Color(1f, 1f, 1f, 0.45f);
                    if (GUILayout.Button($"#{def.LevelId:D3}  {TierNames[(int)def.Difficulty]}  {def.GridCount}格",
                        selected ? EditorStyles.whiteMiniLabel : EditorStyles.miniLabel,
                        GUILayout.Width(150f)))
                    {
                        _selected = def;
                        _dirty = false;
                        Regenerate();
                    }
                    GUILayout.Label($"ch{def.ChapterId}-{def.OrderInChapter}", EditorStyles.miniLabel, GUILayout.Width(52f));
                    GUILayout.Label(def.Enabled ? "" : "停用", EditorStyles.miniLabel, GUILayout.Width(30f));
                    GUI.color = keep;
                }
                EditorGUILayout.Space(1f);
            }
            if (filtered.Count == 0)
                EditorGUILayout.HelpBox("没有符合筛选的草稿。\n新建一个关卡开始设计。", MessageType.Info);
            EditorGUILayout.EndScrollView();
        }
    }

    private List<LevelDefinition> FilteredDrafts()
        => _drafts.Where(d =>
               (_chapterFilter < 0 || d.ChapterId == _chapterFilter) &&
               (_tierFilter < 0 || (int)d.Difficulty == _tierFilter))
           .ToList();

    // ---------- 中：路线预览 ----------

    private void DrawPreviewPanel()
    {
        using (new EditorGUILayout.VerticalScope(GUILayout.ExpandWidth(true)))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("路线预览（只读 · 俯视）", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                _scenePreviewFold = GUILayout.Toggle(_scenePreviewFold, "Scene 3D 叠加", EditorStyles.miniButton);
                LevelScenePreview.Enabled = _scenePreviewFold;
                if (GUILayout.Button("Frame 到 Scene", EditorStyles.miniButton)) FrameSceneToRoute();
            }

            var rect = GUILayoutUtility.GetRect(10f, 10f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            DrawRouteCanvas(rect);

            // 模拟摘要
            if (_sim.Runs > 0 && _preview != null)
            {
                float target = DifficultyProfiles.TargetFirstTryRate(_preview.Definition.Difficulty);
                var line = $"误差模拟 ×{_sim.Runs}：首尝通关 {(_sim.ClearRate * 100f):F1}%（目标 ≥{target:P0}）" +
                           $" · 平均到达 {_sim.AvgMaxIndex:F1}/{_preview.TileCount - 1}";
                EditorGUILayout.HelpBox(line + (_sim.ClearRate < target ? "\n⚠ 低于档位目标，请斟酌发布（不阻断）" : ""), MessageType.None);
            }
        }
    }

    /// <summary>2D 俯视路线画布：编号 / 方向 / 距离(蓄力ms) / 类型 / 触发范围（格面即落点判定范围）</summary>
    private void DrawRouteCanvas(Rect rect)
    {
        if (Event.current.type == EventType.Repaint)
            EditorGUI.DrawRect(rect, new Color(0.13f, 0.13f, 0.16f));

        if (_preview == null || _preview.Tiles == null || _preview.Tiles.Count == 0)
        {
            GUI.Label(rect, _selected == null
                    ? "选择或新建一个关卡"
                    : "点击右侧「生成预览」生成路线",
                new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleCenter });
            return;
        }

        var tiles = _preview.Tiles;
        // 世界包围盒 → 画布变换
        float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
        foreach (var t in tiles)
        {
            minX = Mathf.Min(minX, t.X - t.Size); maxX = Mathf.Max(maxX, t.X + t.Size);
            minZ = Mathf.Min(minZ, t.Z - t.Size); maxZ = Mathf.Max(maxZ, t.Z + t.Size);
        }
        float pad = 0.6f;
        minX -= pad; maxX += pad; minZ -= pad; maxZ += pad;
        float scale = Mathf.Min(rect.width / (maxX - minX), rect.height / (maxZ - minZ));
        Vector2 ToCanvas(float x, float z)
            => new Vector2(rect.x + (x - minX) * scale, rect.y + rect.height - (z - minZ) * scale); // 俯视：+Z 朝上

        // 连线 + 蓄力距离标注
        for (int j = 0; j < tiles.Count - 1; j++)
        {
            var a = ToCanvas(tiles[j].X, tiles[j].Z);
            var b = ToCanvas(tiles[j + 1].X, tiles[j + 1].Z);
            var mid = (a + b) * 0.5f;
            var dir = (b - a).normalized;
            var perp = new Vector2(-dir.y, dir.x);
            var shift = perp * 10f * (j % 2 == 0 ? 1f : -1f);
            Handles.BeginGUI();
            Handles.color = new Color(0.6f, 0.6f, 0.68f, 0.9f);
            Handles.DrawLine(new Vector3(a.x + shift.x, a.y + shift.y), new Vector3(b.x + shift.x, b.y + shift.y));
            Handles.EndGUI();
            var labelRect = new Rect(mid.x + shift.x - 24f, mid.y + shift.y - 9f, 48f, 18f);
            float charge = LevelMath.ChargeMsAtDist(Mathf.Max(0.01f, Vector2.Distance(
                new Vector2(tiles[j].X, tiles[j].Z), new Vector2(tiles[j + 1].X, tiles[j + 1].Z))));
            GUI.Label(labelRect, $"{charge:F0}ms", new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(0.72f, 0.74f, 0.85f) },
                fontSize = 9,
            });
        }

        // 瓦片（触发范围 = 格面矩形）+ 标注
        var numStyle = new GUIStyle(EditorStyles.miniBoldLabel) { alignment = TextAnchor.MiddleCenter, fontSize = 9 };
        var tagStyle = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.UpperCenter, fontSize = 9 };
        for (int i = 0; i < tiles.Count; i++)
        {
            var t = tiles[i];
            var c = ToCanvas(t.X, t.Z);
            float s = t.Size * scale;
            var tileRect = new Rect(c.x - s * 0.5f, c.y - s * 0.5f, s, s);
            if (Event.current.type == EventType.MouseDown && tileRect.Contains(Event.current.mousePosition))
            { _selectedTile = i; Event.current.Use(); Repaint(); }
            Color color = i == _selectedTile ? Color.yellow : LevelScenePreview.KindColor(t);
            Color fill = color;
            fill.a = t.Kind == TileKind.Normal ? 0.22f : 0.55f;
            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(tileRect, fill);
                var outline = new Color(color.r, color.g, color.b, 0.95f);
                var prev = Handles.color;
                Handles.BeginGUI();
                Handles.color = outline;
                Handles.DrawPolyLine(
                    new Vector3(tileRect.xMin, tileRect.yMin),
                    new Vector3(tileRect.xMax, tileRect.yMin),
                    new Vector3(tileRect.xMax, tileRect.yMax),
                    new Vector3(tileRect.xMin, tileRect.yMax),
                    new Vector3(tileRect.xMin, tileRect.yMin));
                Handles.EndGUI();
                Handles.color = prev;
            }

            numStyle.normal.textColor = t.Kind == TileKind.Normal
                ? new Color(0.8f, 0.82f, 0.9f) : Color.white;
            GUI.Label(tileRect, i.ToString(), numStyle);
            string tag = LevelScenePreview.TileTag(t);
            var tagRect = new Rect(tileRect.x, tileRect.yMax - 2f, tileRect.width, 14f);
            if (!string.IsNullOrEmpty(tag))
            {
                tagStyle.normal.textColor = color;
                GUI.Label(tagRect, tag, tagStyle);
            }
            // 完美落点判定圈（触发范围参考）
            if (Event.current.type == EventType.Repaint && t.Size * scale > 26f)
            {
                float r = t.Size * JumpConfig.PerfectRadiusScale * 0.5f * scale;
                var prev = Handles.color;
                Handles.BeginGUI();
                Handles.color = new Color(1f, 0.85f, 0.35f, 0.4f);
                Handles.DrawWireDisc(new Vector3(c.x, c.y), Vector3.forward, Mathf.Max(2f, r));
                Handles.EndGUI();
                Handles.color = prev;
            }
        }

        // 图例
        var legend = new Rect(rect.x + 6f, rect.y + 4f, 330f, 40f);
        GUI.Label(new Rect(legend.x, legend.y, 330f, 16f),
            "起点 / 普通 / 冰滑 / 塌陷 / 冲刺 / 终点",
            new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = new Color(0.8f, 0.82f, 0.9f) } });
        GUI.Label(new Rect(legend.x, legend.y + 15f, 330f, 16f),
            $"gridCount {_preview.TileCount} · layoutHash {_preview.LayoutHash.Substring(0, Mathf.Min(12, _preview.LayoutHash.Length))}… · 预估通关 {_preview.PredictedClearSeconds:F0}s",
            new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = new Color(0.65f, 0.67f, 0.78f) } });
    }

    private void FrameSceneToRoute()
    {
        if (_preview == null || _preview.Tiles.Count == 0) return;
        var view = SceneView.lastActiveSceneView;
        if (view == null) return;
        var bounds = new Bounds();
        bool init = false;
        foreach (var t in _preview.Tiles)
        {
            var p = new Vector3(t.X, 0f, t.Z);
            if (!init) { bounds = new Bounds(p, Vector3.one * t.Size); init = true; }
            else bounds.Encapsulate(new Bounds(p, Vector3.one * t.Size));
        }
        view.Frame(bounds, false);
    }

    // ---------- 右：参数 ----------

    private void DrawParamsPanel(float width)
    {
        using (new EditorGUILayout.VerticalScope(GUILayout.Width(width)))
        {
            if (_selected == null)
            {
                EditorGUILayout.HelpBox("先在左侧选择或新建关卡。", MessageType.Info);
                return;
            }
            var def = _selected;
            _paramsScroll = EditorGUILayout.BeginScrollView(_paramsScroll);

            EditorGUILayout.LabelField($"关卡 #{def.LevelId}{(_dirty ? "  *未保存" : "")}", EditorStyles.boldLabel);
            EditorGUILayout.Space(2f);

            EditorGUI.BeginChangeCheck();
            int levelId = EditorGUILayout.IntField("levelId", def.LevelId);
            int chapterId = EditorGUILayout.IntField("chapterId", def.ChapterId);
            using (new EditorGUILayout.HorizontalScope())
            {
                int order = EditorGUILayout.IntField("章节内顺序", def.OrderInChapter);
                if (GUILayout.Button("↑", GUILayout.Width(24f))) order = SwapOrder(def, -1);
                if (GUILayout.Button("↓", GUILayout.Width(24f))) order = SwapOrder(def, +1);
                if (order != def.OrderInChapter) def.OrderInChapter = Mathf.Max(1, order);
            }
            int gridCount = EditorGUILayout.IntSlider("gridCount（20–60）", def.GridCount, 20, 60);
            int tier = EditorGUILayout.Popup("difficultyTier", (int)def.Difficulty, TierNames);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("seed", GUILayout.Width(60f));
                string seedText = EditorGUILayout.TextField(def.Seed.ToString());
                uint seed;
                if (!uint.TryParse(seedText, out seed)) seed = def.Seed;
                if (GUILayout.Button("随机", GUILayout.Width(44f))) seed = NewRandomSeed();
                def.Seed = seed;
            }
            int themeId = EditorGUILayout.Popup("themeId", Mathf.Clamp(def.ThemeId, 0, JumpConfig.LevelThemeNames.Length - 1),
                JumpConfig.LevelThemeNames);
            bool enabled = EditorGUILayout.Toggle("enabled", def.Enabled);
            if (EditorGUI.EndChangeCheck())
            {
                _dirty = true;
                def.LevelId = levelId > 0 ? levelId : def.LevelId;
                def.ChapterId = Mathf.Max(1, chapterId);
                def.GridCount = gridCount;
                def.Difficulty = (DifficultyTier)tier;
                def.ThemeId = themeId;
                def.Enabled = enabled;
                Regenerate();
            }

            EditorGUILayout.Space(6f);
            DrawSpecialControls(def);
            EditorGUILayout.LabelField("操作", EditorStyles.boldLabel);
            if (GUILayout.Button("生成预览（重新生成路线）", BigButton)) Regenerate();
            if (GUILayout.Button("更换 Seed 并重新生成", BigButton))
            {
                def.Seed = NewRandomSeed();
                _dirty = true;
                Regenerate();
            }
            GUI.enabled = _preview != null;
            if (GUILayout.Button("校验关卡（发布阻断条件）", BigButton))
                _report = _validator.Validate(_preview);
            GUI.enabled = _preview != null && _report != null && _report.Pass;
            if (GUILayout.Button("玩家误差模拟（1000 次）", BigButton))
            {
                _sim = _simulator.Simulate(_preview, _simulator.DefaultRuns,
                    Pcg32.StableHash($"sim/{def.LevelId}/{def.Seed}"));
                Repaint();
            }
            if (GUILayout.Button("试玩当前关（进主场景，不落资产）", BigButton)) OnPlaytest();
            GUI.enabled = true;
            EditorGUILayout.Space(4f);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("保存草稿", GUILayout.Height(30f))) OnSaveDraft();
                GUILayout.Space(4f);
                if (GUILayout.Button("删除", GUILayout.Height(30f))) OnDelete();
            }
            EditorGUILayout.Space(6f);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("包版本", GUILayout.Width(50f));
                _packVersionField = EditorGUILayout.TextField(SuggestNextVersion());
            }
            if (GUILayout.Button("烘焙并导出 JSON（发布管线）", BigButton)) OnBake();
            if (GUILayout.Button("回滚到…（旧包保留）", GUILayout.Height(24f))) OnRollback();
            EditorGUILayout.EndScrollView();
        }
    }


    private void DrawSpecialControls(LevelDefinition def)
    {
        EditorGUILayout.LabelField("特殊格配置（ID 稳定，不按数组位置）", EditorStyles.boldLabel);
        if (GUILayout.Button("编辑全局等级表")) Selection.activeObject = SpecialTileEditorConfig.LoadAsset();
        if (GUILayout.Button("恢复当前关卡难度默认值"))
        { def.SpecialRules = null; _dirty = true; Regenerate(); }
        var rules = LevelPackIo.Clone(def.SpecialRules ?? DifficultyProfiles.DefaultRules(def.Difficulty)).ToList();
        var config = SpecialTileEditorConfig.LoadSettings();
        if (config == null)
        { EditorGUILayout.HelpBox("全局等级表为空；请在配置资产中恢复 Settings 后继续。", MessageType.Error); return; }
        if (rules.Any(r => r == null || r.Levels == null || r.Levels.Any(w => w == null)) ||
            (def.Overrides?.Any(o => o == null) ?? false))
        {
            EditorGUILayout.HelpBox("权重或覆盖含空项，发布已阻断。可恢复默认权重或明确清除空项。", MessageType.Error);
            if (GUILayout.Button("清除空权重/覆盖项"))
            {
                def.SpecialRules = rules.Where(r => r != null).ToArray();
                foreach (var rule in def.SpecialRules) rule.Levels = (rule.Levels ?? new EffectLevelWeight[0]).Where(w => w != null).ToArray();
                def.Overrides = (def.Overrides ?? new SpecialTileOverride[0]).Where(o => o != null).ToArray();
                _dirty = true; Regenerate();
            }
            return;
        }
        EditorGUI.BeginChangeCheck();
        foreach (var kind in new[] { TileKind.Ice, TileKind.Collapse, TileKind.Sprint })
        {
            var rule = rules.FirstOrDefault(r => r.Kind == kind);
            if (rule == null) { rule = new SpecialTileRule { Kind = kind }; rules.Add(rule); }
            rule.Weight = EditorGUILayout.IntField(SpecialTiles.Name(kind) + " 类型权重", rule.Weight);
            var weights = (rule.Levels ?? new EffectLevelWeight[0]).ToList();
            foreach (int id in config.LevelIds(kind).Concat(weights.Select(w => w.LevelId)).Distinct().OrderBy(i => i))
            {
                var entry = weights.FirstOrDefault(w => w.LevelId == id);
                int weight = EditorGUILayout.IntField("    L" + id + " 权重", entry?.Weight ?? 0);
                if (entry != null) entry.Weight = weight;
                else if (weight != 0) weights.Add(new EffectLevelWeight(id, weight));
            }
            rule.Levels = weights.ToArray();
        }
        if (EditorGUI.EndChangeCheck())
        { def.SpecialRules = rules.ToArray(); _dirty = true; Regenerate(); }
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("逐格覆盖（0 为起点；安全区不能设特殊格）");
        _selectedTile = EditorGUILayout.IntField("选中格索引", _selectedTile);
        var overrides = (def.Overrides ?? new SpecialTileOverride[0]).ToList();
        var selected = overrides.FirstOrDefault(o => o.Index == _selectedTile);
        var tile = _preview?.Tiles.FirstOrDefault(t => t.Index == _selectedTile);
        var chosen = selected?.Kind ?? tile?.Kind ?? TileKind.Normal;
        var kinds = new[] { TileKind.Normal, TileKind.Ice, TileKind.Collapse, TileKind.Sprint };
        int choice = System.Array.IndexOf(kinds, chosen);
        EditorGUI.BeginChangeCheck();
        choice = EditorGUILayout.Popup("类型", Mathf.Max(0, choice), kinds.Select(SpecialTiles.Name).ToArray());
        chosen = kinds[choice];
        int grade = 0;
        if (SpecialTiles.IsSpecial(chosen))
        {
            var ids = config.LevelIds(chosen);
            int prev = selected?.EffectLevelId ?? tile?.EffectLevelId ?? 1;
            int idx = EditorGUILayout.Popup("等级 ID", Mathf.Max(0, System.Array.IndexOf(ids, prev)),
                ids.Select(id => "L" + id).ToArray());
            if (ids.Length > 0) grade = ids[Mathf.Clamp(idx, 0, ids.Length - 1)];
        }
        bool edited = EditorGUI.EndChangeCheck();
        if (GUILayout.Button("应用选中格覆盖") || edited)
        {
            overrides.RemoveAll(o => o.Index == _selectedTile);
            overrides.Add(new SpecialTileOverride { Index = _selectedTile, Kind = chosen, EffectLevelId = grade });
            def.Overrides = overrides.OrderBy(o => o.Index).ToArray(); _dirty = true; Regenerate();
        }
        foreach (var entry in overrides.ToArray())
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField($"格 {entry.Index}: {SpecialTiles.Name(entry.Kind)} L{entry.EffectLevelId}");
                if (GUILayout.Button("移除", GUILayout.Width(48)))
                { def.Overrides = overrides.Where(o => o != entry).ToArray(); _dirty = true; Regenerate(); break; }
            }
        }
    }

    private GUIStyle BigButton => new GUIStyle(EditorStyles.miniButton) { fixedHeight = 0f, fontSize = 12, padding = new RectOffset(8, 8, 6, 6) };

    private static uint NewRandomSeed()
        => (uint)UnityEngine.Random.Range(0, int.MaxValue);

    private int SwapOrder(LevelDefinition def, int dir)
    {
        var siblings = _drafts.Where(d => d.ChapterId == def.ChapterId).OrderBy(d => d.OrderInChapter).ToList();
        int idx = siblings.FindIndex(d => d.LevelId == def.LevelId);
        int target = idx + dir;
        if (target < 0 || target >= siblings.Count) return def.OrderInChapter;
        var other = siblings[target];
        int tmp = other.OrderInChapter;
        other.OrderInChapter = def.OrderInChapter;
        def.OrderInChapter = tmp;
        LevelDraftStore.Save(other);
        _dirty = true;
        _drafts = LevelDraftStore.LoadAll();
        return def.OrderInChapter;
    }

    // ---------- 底：校验报告 ----------

    private void DrawBottomPanel()
    {
        using (new EditorGUILayout.VerticalScope(GUILayout.Height(150f)))
        {
            EditorGUILayout.LabelField("校验报告 / 发布日志", EditorStyles.boldLabel);
            _reportScroll = EditorGUILayout.BeginScrollView(_reportScroll);
            if (_report != null)
            {
                foreach (var issue in _report.Issues)
                {
                    var style = new GUIStyle(EditorStyles.miniLabel);
                    style.normal.textColor = issue.Severity == ValidationSeverity.Error
                        ? new Color(1f, 0.35f, 0.35f)
                        : issue.Severity == ValidationSeverity.Warning
                            ? new Color(1f, 0.8f, 0.3f)
                            : new Color(0.7f, 0.72f, 0.8f);
                    EditorGUILayout.LabelField(
                        $"{(issue.Severity == ValidationSeverity.Error ? "✖ 阻断" : issue.Severity == ValidationSeverity.Warning ? "⚠ 警告" : "ℹ")} [{issue.Code}] {issue.Message}",
                        style);
                }
                EditorGUILayout.LabelField(
                    _report.Pass ? $"通过：0 阻断 · {_report.WarningCount} 警告" : $"未通过：{_report.ErrorCount} 阻断 · {_report.WarningCount} 警告",
                    new GUIStyle(EditorStyles.miniBoldLabel)
                    { normal = { textColor = _report.Pass ? new Color(0.5f, 0.9f, 0.55f) : new Color(1f, 0.4f, 0.4f) } });
            }
            if (!string.IsNullOrEmpty(_bakeLog))
                EditorGUILayout.HelpBox(_bakeLog, MessageType.None);
            EditorGUILayout.EndScrollView();
        }
    }

    // ---------- 操作 ----------

    private void OnCreate()
    {
        var def = new LevelDefinition
        {
            LevelId = LevelDraftStore.NextLevelId(),
            ChapterId = 1,
            OrderInChapter = LevelDraftStore.NextOrderInChapter(1),
            GridCount = 24,
            Difficulty = DifficultyTier.Easy,
            Seed = NewRandomSeed(),
            ThemeId = (LevelDraftStore.NextLevelId() - 1) % JumpConfig.LevelThemeNames.Length,
            Enabled = true,
        };
        LevelDraftStore.Save(def);
        _drafts = LevelDraftStore.LoadAll();
        _selected = def;
        _dirty = false;
        Regenerate();
    }

    private void OnDuplicate()
    {
        if (_selected == null) return;
        var def = new LevelDefinition
        {
            LevelId = LevelDraftStore.NextLevelId(),
            ChapterId = _selected.ChapterId,
            OrderInChapter = LevelDraftStore.NextOrderInChapter(_selected.ChapterId),
            GridCount = _selected.GridCount,
            Difficulty = _selected.Difficulty,
            Seed = NewRandomSeed(), // 复制换 Seed：同参数不同布局；同 Seed 同布局
            ThemeId = _selected.ThemeId,
            Enabled = _selected.Enabled,
            SpecialRules = LevelPackIo.Clone(_selected.SpecialRules),
            Overrides = LevelPackIo.Clone(_selected.Overrides),
        };
        LevelDraftStore.Save(def);
        _drafts = LevelDraftStore.LoadAll();
        _selected = def;
        _dirty = false;
        Regenerate();
    }

    private void OnToggleEnabled()
    {
        if (_selected == null) return;
        _selected.Enabled = !_selected.Enabled;
        LevelDraftStore.Save(_selected);
        _drafts = LevelDraftStore.LoadAll();
    }

    private void OnSaveDraft()
    {
        if (_selected == null) return;
        if (LevelDraftStore.Exists(_selected.LevelId) && !DraftIds().Contains(_selected.LevelId))
        {
            EditorUtility.DisplayDialog("levelId 冲突", $"levelId {_selected.LevelId} 已被其他草稿占用", "返回");
            return;
        }
        LevelDraftStore.Save(_selected);
        _drafts = LevelDraftStore.LoadAll();
        _selected = _drafts.FirstOrDefault(d => d.LevelId == _selected.LevelId);
        _dirty = false;
        _bakeLog = "";
        Regenerate();
    }

    private List<int> DraftIds() => _drafts.Select(d => d.LevelId).ToList();

    private void OnDelete()
    {
        if (_selected == null) return;
        if (!EditorUtility.DisplayDialog("删除草稿",
                $"确定删除关卡 #{_selected.LevelId}？\n已烘焙的包文件不受影响（旧包保留）。",
                "删除", "取消")) return;
        LevelDraftStore.Delete(_selected.LevelId);
        _selected = null;
        _preview = null;
        _report = null;
        Reload();
    }

    private void OnPlaytest()
    {
        if (_preview == null) Regenerate();
        if (_preview == null)
        {
            EditorUtility.DisplayDialog("试玩失败", "生成失败：请更换 Seed。", "知道了");
            return;
        }
        _report = _validator.Validate(_preview);
        if (!_report.Pass)
        {
            EditorUtility.DisplayDialog("试玩阻断", "校验存在阻断项（见底部报告），请先修复再试玩。", "返回");
            return;
        }
        // 试玩经 SessionState 传草稿：不生成/不保存任何临时正式关卡资产
        LevelRuntime.RequestPlaytest(LevelPackIo.ToJson(LevelPackIo.ToPackedLevel(_preview)));
        if (EditorApplication.isPlaying)
        {
            if (JumpGame.Instance != null) JumpGame.Instance.Restart(); // 复用现场：重启即注入草稿
            else JumpGame.StartGame();                                  // 在播但未开局（主页）：免费直入
        }
        else
        {
            EditorApplication.isPlaying = true; // Boot → 主页打开 → 免体力自动开局
        }
    }

    private string SuggestNextVersion()
    {
        if (!string.IsNullOrEmpty(_packVersionField)) return _packVersionField;
        var index = LevelBakePipeline.LoadIndex();
        if (index == null || string.IsNullOrEmpty(index.latest) || index.schemaVersion != 2) return "2.0.0";
        var parts = index.latest.Split('.');
        if (parts.Length == 3 && int.TryParse(parts[2], out int minor)) return $"{parts[0]}.{parts[1]}.{minor + 1}";
        return index.latest + ".1";
    }

    private void OnBake()
    {
        if (_dirty) OnSaveDraft(); // 发布流程第一步：保存草稿
        var version = SuggestNextVersion();
        var result = LevelBakePipeline.BakeAll(version, step =>
            EditorUtility.DisplayProgressBar("烘焙关卡包", step, 0.5f));
        EditorUtility.ClearProgressBar();

        _bakeLog = result.Success
            ? $"✅ 包 v{result.PackVersion} 发布成功：{result.LevelCount} 关 · SHA-256 {result.PackSha256.Substring(0, 16)}…\n" +
              string.Join("\n", result.SimSummary) + "\n" + string.Join(" → ", result.Steps)
            : "❌ 发布失败：\n" + string.Join("\n", result.Failures);
        Debug.Log($"[LevelEditor] 烘焙结果 v{version}：{(result.Success ? "成功" : "失败")}\n{_bakeLog}");
        if (!result.Success)
            EditorUtility.DisplayDialog("发布失败", string.Join("\n", result.Failures), "知道了");
        _packVersionField = ""; // 下次烘焙重新推荐递增版本
        Repaint();
    }

    private void OnRollback()
    {
        var index = LevelBakePipeline.LoadIndex();
        if (index == null || index.entries.Length == 0)
        {
            EditorUtility.DisplayDialog("回滚", "没有可回滚的历史版本。", "知道了");
            return;
        }
        var menu = new GenericMenu();
        foreach (var e in index.entries.Where(entry => entry.schemaVersion == 2).OrderByDescending(e => e.buildTimeUtc))
        {
            bool isCurrent = e.version == index.latest;
            menu.AddItem(new GUIContent($"v{e.version}（{e.levelCount} 关 · {e.buildTimeUtc}）{(isCurrent ? "· 当前" : "")}"),
                isCurrent, () =>
                {
                    if (LevelBakePipeline.RollbackTo(e.version))
                        _bakeLog = $"已回滚：latest → v{e.version}（新对局使用回滚版本；进行中的对局不受影响）";
                    Repaint();
                });
        }
        menu.ShowAsContext();
    }
}
