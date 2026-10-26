using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 全局飘字UI：各系统经 EventCenter 派发 EventKeys.FloatText(string) 显示提示文字。
/// 特性：常驻不销毁（自建 DontDestroyOnLoad 顶层 Canvas，不进 UIMgr 面板栈）；
/// 一次只显示一条（FIFO 排队逐条显示，队列上限 MaxQueue 溢出丢最旧）；
/// 排重=冷却窗口（同一文本 CooldownSeconds 内只受理一次，按入队时刻记时，
/// 防队列积压时同文本连刷两遍）；条目走空闲栈对象池复用（一次一条，常态仅 1 个条目）。
/// 主线程单线程使用（对齐 EventCenter 派发线程约定），队列/字典不加锁。
/// </summary>
public class FloatTextMgr : PureSingleton<FloatTextMgr>
{
    /// <summary>同文本冷却窗口（秒）：窗口内重复文本直接丢弃</summary>
    public const float CooldownSeconds = 3f;
    /// <summary>待显示队列上限，溢出丢最旧（防极端刷屏无界增长）</summary>
    public const int MaxQueue = 8;
    /// <summary>单条显示总时长：前 60% 原地停留，后 40% 上飘淡出（同战斗 Toast 节奏）</summary>
    public const float ShowDuration = 1.4f;
    /// <summary>淡出段上飘像素</summary>
    public const float RisePixels = 60f;

    private Canvas _canvas;
    private readonly Queue<string> _pending = new Queue<string>();
    private readonly Dictionary<string, float> _lastAccept = new Dictionary<string, float>();
    private readonly Stack<GameObject> _pool = new Stack<GameObject>();
    private int _poolCreated;
    private bool _pumping;

    // ---------- 调试/断言只读口 ----------
    public int PendingCount => _pending.Count;
    public bool IsShowing { get; private set; }
    public string CurrentText { get; private set; }
    public int PoolCreatedCount => _poolCreated;
    public int CooldownEntryCount => _lastAccept.Count;

    public int InactivePoolCount
    {
        get
        {
            int n = 0;
            foreach (var go in _pool)
                if (go != null) n++;
            return n;
        }
    }

    protected override void OnInit()
    {
        if (_canvas != null) return;
        BuildRoot();
        EventCenter.AddListener<string>(EventKeys.FloatText, Enqueue);
        RunPumpAsync().Forget();
    }

    /// <summary>常驻根：ScreenSpaceOverlay Canvas，CanvasTop(50) 之上、DebugConsole(100) 之下</summary>
    private void BuildRoot()
    {
        var root = new GameObject("FloatTextCanvas", typeof(Canvas), typeof(CanvasScaler));
        Object.DontDestroyOnLoad(root);
        _canvas = root.GetComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 60;
        var scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080, 1920);
        scaler.matchWidthOrHeight = 0.5f;
    }

    /// <summary>事件入口：空文本忽略 → 冷却窗口排重 → 队列上限丢最旧 → 入队</summary>
    private void Enqueue(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        float now = Time.unscaledTime;
        if (_lastAccept.TryGetValue(text, out float last) && now - last < CooldownSeconds)
            return; // 冷却窗口内同文本已受理过，丢弃

        // 清理过期冷却项，防字典无界增长（文本种类有限，代价极小）
        List<string> expired = null;
        foreach (var kv in _lastAccept)
        {
            if (now - kv.Value >= CooldownSeconds)
                (expired ??= new List<string>()).Add(kv.Key);
        }
        if (expired != null)
            for (int i = 0; i < expired.Count; i++)
                _lastAccept.Remove(expired[i]);

        _lastAccept[text] = now;

        while (_pending.Count >= MaxQueue)
            _pending.Dequeue(); // 溢出丢最旧
        _pending.Enqueue(text);
    }

    /// <summary>显示泵：一次只显示一条，逐条 await 完整动画后再取下一条</summary>
    private async UniTaskVoid RunPumpAsync()
    {
        if (_pumping) return;
        _pumping = true;
        try
        {
            while (Application.isPlaying)
            {
                while (_pending.Count == 0)
                {
                    if (!Application.isPlaying) return;
                    await UniTask.Yield();
                }
                string msg = _pending.Dequeue();
                await ShowAsync(msg);
            }
        }
        finally
        {
            _pumping = false;
        }
    }

    /// <summary>显示一条：池取条目 → 置文案 → 停留+上飘淡出 → 回池</summary>
    private async UniTask ShowAsync(string text)
    {
        var go = GetPooledItem();
        if (go == null) return;
        var rt = (RectTransform)go.transform;
        var cg = go.GetComponent<CanvasGroup>();
        rt.anchoredPosition = Vector2.zero;
        go.GetComponent<Text>().text = text;
        cg.alpha = 1f;
        IsShowing = true;
        CurrentText = text;

        const float fadeStart = 0.6f;
        float t = 0f;
        while (t < ShowDuration)
        {
            if (!Application.isPlaying) break;
            t += Time.unscaledDeltaTime;
            float p = Mathf.Clamp01(t / ShowDuration);
            if (p >= fadeStart)
            {
                float fp = (p - fadeStart) / (1f - fadeStart);
                rt.anchoredPosition = Vector2.up * (RisePixels * fp);
                cg.alpha = 1f - fp;
            }
            await UniTask.Yield();
        }

        if (go != null)
        {
            cg.alpha = 1f;
            go.SetActive(false);
            _pool.Push(go);
        }
        IsShowing = false;
        CurrentText = null;
    }

    /// <summary>池取条目：空闲栈复用（跳过脏引用），无空闲则程序化新建</summary>
    private GameObject GetPooledItem()
    {
        while (_pool.Count > 0)
        {
            var candidate = _pool.Pop();
            if (candidate != null)
            {
                candidate.SetActive(true);
                return candidate;
            }
        }

        var go = new GameObject("FloatTextItem", typeof(RectTransform), typeof(Text), typeof(CanvasGroup));
        var rt = (RectTransform)go.transform;
        rt.SetParent(_canvas.transform, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(900f, 80f);
        var text = go.GetComponent<Text>();
        text.font = GetFont();
        text.fontSize = 44;
        text.color = new Color(1f, 0.85f, 0.35f); // 品牌金（同战斗 Toast）
        text.alignment = TextAnchor.MiddleCenter;
        text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        var shadow = go.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.6f);
        shadow.effectDistance = new Vector2(2f, -2f);
        _poolCreated++;
        return go;
    }

    private static Font GetFont()
    {
        try { return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); }
        catch { return Resources.GetBuiltinResource<Font>("Arial.ttf"); }
    }

    // ---------- GM ----------

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void RegisterGm()
    {
        DebugConsoleMgr.RegisterCommand("toast",
            "toast <文本>（全局飘字事件测试）| toast info（队列/池/冷却统计）", OnGmCommand);
    }

    private static void OnGmCommand(string args)
    {
        Instance.Init(); // 幂等兜底：防极早期派发时未初始化
        if (string.IsNullOrEmpty(args) || args == "info")
        {
            var m = Instance;
            Debug.Log($"[ToastGM] 显示中:{(m.IsShowing ? $"\"{m.CurrentText}\"" : "无")} | "
                + $"队列 {m.PendingCount}/{MaxQueue} | 池 新建{m.PoolCreatedCount}·空闲{m.InactivePoolCount} | "
                + $"冷却表 {m.CooldownEntryCount}");
            return;
        }
        EventCenter.Dispatch(EventKeys.FloatText, args); // 走纯事件链路
    }
}
