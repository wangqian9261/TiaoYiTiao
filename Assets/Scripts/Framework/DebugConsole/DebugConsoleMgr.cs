using System.Collections.Generic;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 调试控制台：悬浮日志窗（环形缓冲）+ GM 命令行，代码构建UI、无资源依赖。
/// Release 打包自动关闭（仅 编辑器 / Development 构建创建）；
/// 业务可经 RegisterCommand 扩展指令；Execute 与控制台输入同源。
/// </summary>
public static class DebugConsoleMgr
{
    /// <summary>如需 Release 也启用，改 true 重编（不建议线上开启）</summary>
    public const bool EnabledInRelease = false;

    public static bool Enabled
    {
        get
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            return true;
#else
            return EnabledInRelease;
#endif
        }
    }

    private const int BufferLines = 200;

    private static readonly Queue<string> _lines = new Queue<string>(BufferLines);
    private static readonly Dictionary<string, string> _cmdDesc = new Dictionary<string, string>();
    private static readonly Dictionary<string, System.Action<string>> _cmdHandlers = new Dictionary<string, System.Action<string>>();
    private static readonly List<GameObject> _demoCubes = new List<GameObject>();

    private static Canvas _canvas;
    private static GameObject _panel;
    private static Text _logText;
    private static InputField _input;

    /// <summary>初始化（GameLauncher 调用；非调试构建为空操作）</summary>
    public static void Init()
    {
        if (!Enabled) return;
        if (_canvas != null) return;
        BuildUi();
        Application.logMessageReceived += OnLogReceived;
        RegisterBuiltInCommands();
        Debug.Log("[DebugConsole] 就绪：点左上角【≡】开关面板，输入 help 查看全部指令");
    }

    /// <summary>注册 GM 指令（业务扩展入口）</summary>
    public static void RegisterCommand(string cmd, string desc, System.Action<string> handler)
    {
        _cmdDesc[cmd] = desc;
        _cmdHandlers[cmd] = handler;
    }

    /// <summary>执行指令行：`cmd arg1 arg2`（控制台输入与代码调用完全同源）</summary>
    public static void Execute(string line)
    {
        line = line.Trim();
        if (line.Length == 0) return;
        int space = line.IndexOf(' ');
        string cmd = space < 0 ? line : line.Substring(0, space);
        string args = space < 0 ? "" : line.Substring(space + 1).Trim();

        if (_cmdHandlers.TryGetValue(cmd, out var handler))
        {
            Debug.Log($"[GM] > {line}");
            try
            {
                handler(args);
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
            }
        }
        else
        {
            Debug.Log($"[GM] 未知指令: {cmd}，输入 help 查看全部指令");
        }
    }

    // ================= UI 构建 =================

    private static void BuildUi()
    {
        var root = new GameObject("DebugConsoleCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Object.DontDestroyOnLoad(root);
        _canvas = root.GetComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 100; // UIRoot 的 CanvasTop(50) 之上
        var scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080, 1920);
        scaler.matchWidthOrHeight = 0.5f;

        // 开关按钮（常驻左上角）
        var toggle = CreateButton(root.transform, "ToggleBtn", "≡", new Vector2(60, 1850), new Vector2(100, 100));
        toggle.GetComponent<Button>().onClick.AddListener(TogglePanel);

        // 面板（默认隐藏）
        _panel = new GameObject("ConsolePanel", typeof(RectTransform), typeof(Image));
        _panel.transform.SetParent(root.transform, false);
        var panelRect = (RectTransform)_panel.transform;
        panelRect.anchorMin = new Vector2(0, 0.25f);
        panelRect.anchorMax = new Vector2(1, 1);
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;
        var panelImg = _panel.GetComponent<Image>();
        panelImg.color = new Color(0f, 0f, 0f, 0.82f);

        // 日志文本
        var logGo = new GameObject("LogText", typeof(RectTransform), typeof(Text));
        logGo.transform.SetParent(_panel.transform, false);
        var logRect = (RectTransform)logGo.transform;
        logRect.anchorMin = new Vector2(0, 0.12f);
        logRect.anchorMax = Vector2.one;
        logRect.offsetMin = new Vector2(10, 4);
        logRect.offsetMax = new Vector2(-10, -4);
        _logText = logGo.GetComponent<Text>();
        SetupText(_logText, 26, new Color(0.85f, 0.95f, 0.8f), TextAnchor.UpperLeft);

        // 命令输入行
        var inputGo = new GameObject("CmdInput", typeof(RectTransform), typeof(Image), typeof(InputField));
        inputGo.transform.SetParent(_panel.transform, false);
        var inputRect = (RectTransform)inputGo.transform;
        inputRect.anchorMin = new Vector2(0, 0);
        inputRect.anchorMax = new Vector2(0.78f, 0.1f);
        inputRect.offsetMin = new Vector2(10, 10);
        inputRect.offsetMax = new Vector2(0, -10);
        inputGo.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.15f);

        _input = inputGo.GetComponent<InputField>();
        _input.textComponent = CreateInputText(inputGo.transform);
        _input.placeholder = CreatePlaceholder(inputGo.transform);
        _input.onSubmit.AddListener(_ => SubmitInput());

        var runBtn = CreateButton(_panel.transform, "RunBtn", "RUN", new Vector2(0, 0), new Vector2(0, 0));
        var runRect = (RectTransform)runBtn.transform;
        runRect.anchorMin = new Vector2(0.78f, 0f);
        runRect.anchorMax = new Vector2(1f, 0.1f);
        runRect.anchoredPosition = Vector2.zero;
        runRect.offsetMin = new Vector2(6, 10);
        runRect.offsetMax = new Vector2(-10, -10);
        runBtn.GetComponent<Button>().onClick.AddListener(SubmitInput);

        _panel.SetActive(false);

        // 开关按钮置顶：面板展开时按钮仍可见可点（否则面板盖住后无法收起）
        toggle.transform.SetAsLastSibling();
    }

    private static Text CreateInputText(Transform parent)
    {
        var go = new GameObject("Text", typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        var rect = (RectTransform)go.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(12, 2);
        rect.offsetMax = new Vector2(-10, -2);
        var t = go.GetComponent<Text>();
        SetupText(t, 28, Color.white, TextAnchor.MiddleLeft);
        return t;
    }

    private static Text CreatePlaceholder(Transform parent)
    {
        var go = new GameObject("Placeholder", typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        var rect = (RectTransform)go.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(12, 2);
        rect.offsetMax = new Vector2(-10, -2);
        var t = go.GetComponent<Text>();
        SetupText(t, 28, new Color(1f, 1f, 1f, 0.35f), TextAnchor.MiddleLeft);
        t.text = "输入指令后点 RUN";
        return t;
    }

    private static void SubmitInput()
    {
        if (_input == null || string.IsNullOrEmpty(_input.text)) return;
        string cmd = _input.text;
        _input.text = "";
        if (_input.IsActive()) _input.ActivateInputField(); // 回车提交后保持焦点
        Execute(cmd);
    }

    private static void TogglePanel()
    {
        if (_panel == null) return;
        _panel.SetActive(!_panel.activeSelf);
        if (_panel.activeSelf) RefreshLogText();
    }

    private static void OnLogReceived(string condition, string stackTrace, LogType type)
    {
        if (_lines.Count >= BufferLines) _lines.Dequeue();
        string tag = type == LogType.Log
            ? ""
            : type == LogType.Warning
                ? "<color=yellow>[W] </color>"
                : "<color=red>[E] </color>";
        _lines.Enqueue(tag + condition);
        if (_panel != null && _panel.activeSelf) RefreshLogText();
    }

    private static void RefreshLogText()
    {
        if (_logText == null) return;
        var sb = new StringBuilder();
        foreach (var line in _lines)
        {
            sb.AppendLine(line);
        }
        _logText.text = sb.ToString();
    }

    // ================= 内置指令 =================

    private static void RegisterBuiltInCommands()
    {
        RegisterCommand("help", "列出全部指令", _ =>
        {
            var sb = new StringBuilder();
            sb.AppendLine("== GM 指令列表 ==");
            foreach (var kv in _cmdDesc)
                sb.AppendLine($"  {kv.Key,-10} {kv.Value}");
            Debug.Log(sb.ToString());
        });

        RegisterCommand("clear", "清空日志", _ =>
        {
            _lines.Clear();
            RefreshLogText();
        });

        RegisterCommand("mem", "内存与资源统计", _ =>
        {
            UIMgr.Instance.PrintUIMemoryLog();
            Debug.Log(PoolMgr.Instance.GetStats().TrimEnd());
            Debug.Log(WeChatService.GetStorageReport());
            Debug.Log($"ResMgr模式={ResMgr.Instance.LoadMode}，托管内存≈{System.GC.GetTotalMemory(false) / 1024 / 1024}MB");
        });

        RegisterCommand("res", "res info / res warm boot|battle|<包名>", args =>
        {
            var parts = args.Split(' ');
            string sub = parts.Length > 0 ? parts[0].ToLowerInvariant() : "info";
            if (sub == "warm")
            {
                string target = parts.Length > 1 ? parts[1] : "";
                string[] names;
                if (target == "boot") names = BundleMap.BootGroup;
                else if (target == "battle") names = BundleMap.BattleGroup;
                else if (target.Length > 0) names = new[] { target };
                else
                {
                    Debug.Log("[GM] 用法: res warm boot|battle|<包名>");
                    return;
                }
                ResMgr.Instance.PreloadBundlesAsync(names).Forget();
                Debug.Log($"[GM] 已发起预热: {string.Join(",", names)}");
                return;
            }

            // info（默认）：当前模式 + 已加载 AB 包
            var sb = new StringBuilder();
            sb.AppendLine($"[GM] ResMgr模式={ResMgr.Instance.LoadMode}，AB冷载计数={ResMgr.Instance.BundleLoader.ColdLoadCount}");
            var bundles = ResMgr.Instance.BundleLoader.LoadedBundles;
            if (bundles.Count == 0)
                sb.AppendLine("  （无已加载 AB 包——编辑器模拟模式或尚未触发 AB 加载）");
            foreach (var kv in bundles)
                sb.AppendLine($"  - {kv.Key}: {kv.Value.GetAllAssetNames().Length} assets");
            Debug.Log(sb.ToString());
        });

        RegisterCommand("fps", "帧率与时间信息", _ =>
        {
            Debug.Log($"targetFrameRate={Application.targetFrameRate} timeScale={Time.timeScale} 运行时长={Time.unscaledTime:F1}s");
        });

        RegisterCommand("time", "time <scale> 设置timeScale", args =>
        {
            Time.timeScale = Mathf.Clamp(StringUtil.ToFloat(args, 1f), 0f, 5f);
            Debug.Log($"[GM] timeScale={Time.timeScale}");
        });

        RegisterCommand("audio", "audio bgm|sfx <key> / audio mute 0|1", args =>
        {
            var parts = args.Split(' ');
            if (parts.Length == 0) return;
            if (parts[0] == "mute")
                AudioMgr.Instance.SetMuted(StringUtil.ToBool(parts.Length > 1 ? parts[1] : "1"));
            else if (parts[0] == "bgm" && parts.Length > 1)
                AudioMgr.Instance.PlayBgm(parts[1]).Forget();
            else if (parts[0] == "sfx" && parts.Length > 1)
                AudioMgr.Instance.PlaySfx(parts[1]);
        });

        RegisterCommand("pool", "pool spawn|despawn|clear|stats <n>（cube池演示）", args =>
        {
            var parts = args.Split(' ');
            if (parts.Length == 0) return;
            switch (parts[0])
            {
                case "stats":
                    Debug.Log(PoolMgr.Instance.GetStats().TrimEnd());
                    return;
                case "spawn":
                    SpawnDemoCubesAsync(Mathf.Clamp(StringUtil.ToInt(parts.Length > 1 ? parts[1] : "1", 1), 1, 30)).Forget();
                    return;
                case "despawn":
                    int n = Mathf.Clamp(StringUtil.ToInt(parts.Length > 1 ? parts[1] : "1", 1), 1, _demoCubes.Count);
                    for (int i = 0; i < n; i++)
                    {
                        int last = _demoCubes.Count - 1;
                        PoolMgr.Instance.Despawn("cube", _demoCubes[last]);
                        _demoCubes.RemoveAt(last);
                    }
                    Debug.Log($"[GM] 回收 {n} 个，剩余活跃 {PoolMgr.Instance.GetActiveCount("cube")}");
                    return;
                case "clear":
                    foreach (var g in _demoCubes) PoolMgr.Instance.Despawn("cube", g);
                    _demoCubes.Clear();
                    PoolMgr.Instance.ClearPool("cube");
                    Debug.Log("[GM] cube 池已清空");
                    return;
            }
        });

        RegisterCommand("wx", "wx hide|show|share|login|reward ok|fail|capsule on|off（编辑器Mock演练）", args =>
        {
            var parts = args.Split(' ');
            if (parts.Length == 0) return;
            switch (parts[0])
            {
                case "hide":
                    if (!WeChatService.IsWeChatEnv) MockLifecycle.SimulateHide();
                    break;
                case "show":
                    if (!WeChatService.IsWeChatEnv) MockLifecycle.SimulateShow();
                    break;
                case "share":
                    WeChatService.Share.Share(new ShareContent { Title = "Framework Demo", Query = "from=gm" });
                    break;
                case "login":
                    GmLoginAsync().Forget();
                    break;
                case "reward":
                    MockAds.NextOk = parts.Length < 2 || parts[1] != "fail";
                    GmRewardAsync().Forget();
                    break;
                case "capsule": // 模拟/关闭微信胶囊（主页顶部避让演练：切开后重开主页生效）
                    MockSystemInfo.SimulateCapsule = parts.Length < 2 || parts[1] != "off";
                    Debug.Log($"[GM] 胶囊模拟 {(MockSystemInfo.SimulateCapsule ? "开启（重开主页生效）" : "关闭")}");
                    break;
            }
        });

        RegisterCommand("save", "写入演示存档（混淆）", _ =>
        {
            DataMgr.Instance.Save("gm.demo", new DemoPlayerSave { level = 9, coin = 123, playerName = "gm" }, true);
            Debug.Log("[GM] 已写存档 gm.demo（XOR混淆）");
        });

        RegisterCommand("load", "读取演示存档", _ =>
        {
            var d = DataMgr.Instance.Load<DemoPlayerSave>("gm.demo", true);
            Debug.Log(d == null ? "[GM] 无存档（先 save）" : $"[GM] 读档成功: level={d.level} coin={d.coin} name={d.playerName}");
        });

        RegisterCommand("cfg", "cfg <key> 查看配置JSON（默认 Config/demo）", args =>
        {
            GmLoadCfgAsync(string.IsNullOrEmpty(args) ? "Config/demo" : args).Forget();
        });

        RegisterCommand("ui", "ui home|reward 打开主页/演示奖励面板", args =>
        {
            if (args == "reward")
                UIMgr.Instance.OpenPanel("Prefabs/Panel_Reward", UILayer.Popup).Forget();
            else
                UIMgr.Instance.OpenPanel("Prefabs/UIHomeView", UILayer.Page).Forget();
        });
    }

    private static async UniTask SpawnDemoCubesAsync(int count)
    {
        await PoolMgr.Instance.RegisterPoolAsync("cube", "Prefabs/PoolCube"); // 幂等
        for (int i = 0; i < count; i++)
        {
            var g = PoolMgr.Instance.Spawn("cube", new Vector3(
                RandomUtil.Range(-3f, 3f), RandomUtil.Range(0.5f, 4f), RandomUtil.Range(0f, 5f)));
            if (g != null) _demoCubes.Add(g);
        }
        Debug.Log($"[GM] 生成 {count} 个方块（活跃 {PoolMgr.Instance.GetActiveCount("cube")}，pool stats 查看）");
    }

    private static async UniTask GmLoginAsync()
    {
        var r = await WeChatService.Login.LoginAsync();
        Debug.Log($"[GM] 登录 Ok={r.Ok} Code={r.Code} Err={r.Error}");
    }

    private static async UniTask GmRewardAsync()
    {
        var r = await WeChatService.Ads.ShowRewardVideoAsync("gm_test_unit");
        Debug.Log($"[GM] 激励视频 完整观看={r.Ok} 拉起={r.Shown}" + (r.Ok ? " → 可发奖" : " → 不发奖"));
    }

    private static async UniTask GmLoadCfgAsync(string key)
    {
        var t = await ResMgr.Instance.LoadAssetAsync<TextAsset>(key);
        Debug.Log(t == null ? $"[GM] 配置缺失: {key}" : $"[GM] {key} => {t.text}");
    }

    // ================= UI 小工具 =================

    private static GameObject CreateButton(Transform parent, string name, string label, Vector2 pos, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var rect = (RectTransform)go.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = pos;
        rect.sizeDelta = size;
        go.GetComponent<Image>().color = new Color(0.2f, 0.25f, 0.3f, 0.9f);

        var textGo = new GameObject("Text", typeof(RectTransform), typeof(Text));
        textGo.transform.SetParent(go.transform, false);
        var textRect = (RectTransform)textGo.transform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;
        var text = textGo.GetComponent<Text>();
        SetupText(text, 32, Color.white, TextAnchor.MiddleCenter);
        text.text = label;
        return go;
    }

    private static void SetupText(Text t, int size, Color color, TextAnchor align)
    {
        try
        {
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }
        catch
        {
            t.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        }
        t.fontSize = size;
        t.color = color;
        t.alignment = align;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
    }
}
