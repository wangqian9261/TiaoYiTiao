using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 跳一跳主控：世界创建 / 方块生成回收 / 蓄力与跳跃 / 落点判定 / 计分与砖上物品 / 陷阱砖 /
/// 镜头跟随 / 死亡与重开 / GM 指令。
/// 砖上物品（转盘/积分增益/金币袋/碎片/护盾/冲刺）：踩上停留 1.5s 自动触发，无需操作。
/// 陷阱砖（打滑/幻影/迷雾减速/震荡/燃烧/反向蓄力/封印）：落地即触发；全部带预警外观、可规避、无秒杀。
/// 入口：PanelHome“开始”按钮或 GM 指令 → StartGame()；回主页 → ExitToHome()（销毁世界）。
/// 不依赖 GameLauncher / Procedure；所有状态自管理，事件经 EventCenter 派发。
/// </summary>
public class JumpGame : MonoBehaviour
{
    public static JumpGame Instance { get; private set; }

    public JumpState State { get; private set; } = JumpState.Idle;
    public int Score { get; private set; }
    /// <summary>是否蓄力过至少一次（HUD 开局引导用）</summary>
    public bool EverCharged { get; private set; }

    public int CurrentLevel => _level;
    public int LevelTargetBlocks => _levelTarget;
    /// <summary>本局已前进块数（当前所站块 Index，起点 0）</summary>
    public int LevelProgress => _current != null ? _current.Index : 0;

    /// <summary>当前所站砖携带的未触发物品（无则 None）</summary>
    public JumpItemKind CurrentItem
        => _current != null && !_current.ItemConsumed ? _current.ItemKind : JumpItemKind.None;

    /// <summary>物品停留触发进度 0~1；不适用时返回 -1</summary>
    public float ItemStayProgress
    {
        get
        {
            if (State != JumpState.Ready || CurrentItem == JumpItemKind.None) return -1f;
            return Mathf.Clamp01(_itemTimer / JumpConfig.ItemStaySeconds);
        }
    }

    /// <summary>本局积分倍率加成（0..ScoreBoostMax；积分增益砖逐层叠加）</summary>
    public float ScoreMultiplier => _scoreMult;
    /// <summary>本局完美落点额外分（积分增益砖触发后为 ScoreBoostPerfectBonus）</summary>
    public int PerfectExtra => _perfectExtra;
    /// <summary>剩余护盾层数（护盾砖叠加，坠落自动消耗复活）</summary>
    public int ShieldCount => _shieldCount;
    /// <summary>本局收益是否已翻倍（幸运转盘奖励）</summary>
    public bool RunRewardDoubled => _runDouble;

    /// <summary>当前所站砖携带的未触发陷阱（无则 None）</summary>
    public JumpTrapKind CurrentTrap
        => _current != null && !_current.TrapConsumed ? _current.TrapKind : JumpTrapKind.None;
    /// <summary>燃烧剩余秒数（未在燃烧倒计时中返回 -1）</summary>
    public float BurnCountdown
    {
        get
        {
            if (_burnBlock == null || (State != JumpState.Ready && State != JumpState.Charging)) return -1f;
            return _burnRemain;
        }
    }
    /// <summary>剩余减速蓄力次数（迷雾砖效果）</summary>
    public int SlowChargeRemaining => _slowChargeCount;
    /// <summary>下一次跳跃是否反向蓄力（反向砖效果）</summary>
    public bool ReverseNext => _reverseNext;

    // ---------- 有限关卡模式（关卡编辑器烘焙包驱动；_plan==null 时回退旧无限模式） ----------
    /// <summary>当前对局的关卡计划（null = 旧无限模式）</summary>
    public LevelRunPlan Plan => _plan;
    /// <summary>关卡显示名（计划=烘焙包名；旧模式=GetLevelName）</summary>
    public string CurrentLevelName
        => _plan != null ? _plan.DisplayName : JumpConfig.GetLevelName(_level);
    /// <summary>本关累计金币（结算统一入账；转盘"本关金币翻倍"覆盖）</summary>
    public int RunCoins => _runCoins;
    /// <summary>转盘"本关金币收益翻倍"是否激活</summary>
    public bool RunCoinDouble => _coinDouble;
    /// <summary>幻影塌陷剩余秒数（未激活返回 -1）</summary>
    public float PhantomCountdown
    {
        get
        {
            if (_phantomBlock == null || (State != JumpState.Ready && State != JumpState.Charging)) return -1f;
            return _phantomRemain;
        }
    }
    /// <summary>陷阱倒计时是否冻结（转盘弹窗/切后台：暂停蓄力、陷阱计时和关卡逻辑）</summary>
    public bool TrapTimeFrozen => _wheelOpen || _appPaused || _userPaused;
    public bool UserPaused => _userPaused;
    public bool AppPaused => _appPaused;
    public int Combo => _combo;
    public int RunVersion => _presentationVersion;
    public bool IsRunActive(int version) => this != null && Instance == this
        && version == _presentationVersion && State != JumpState.Idle && State != JumpState.Over;
    public float ChargeElapsed => State == JumpState.Charging ? Mathf.Max(0f, Time.time - _chargeStart) : 0f;
    public float ChargePhase => Mathf.Repeat(ChargeElapsed / (JumpConfig.ChargeCycleSeconds * _chargeScale), 1f);
    public float ChargePower => GetPower();
    public Vector3 TrapAnchor => _burnBlock != null ? _burnBlock.TopCenter : _phantomBlock != null ? _phantomBlock.TopCenter : Vector3.zero;
    public float TrapDuration => _burnBlock != null ? _burnTotal : Mathf.Max(.01f, _phantomTotal);

    /// <summary>The displayed sector uses exactly the distance/half-width used by EvaluateLanding.</summary>
    public bool TryGetChargeTarget(out Vector2 phaseRange)
    {
        phaseRange = Vector2.zero;
        if (_current == null || _next == null) return false;
        float distance = Vector3.Dot(_next.TopCenter - _current.TopCenter, TravelAxis(_current.Index));
        float near = distance - _next.Size * .5f, far = distance + _next.Size * .5f;
        if (far < JumpConfig.MinJumpDist || near > JumpConfig.MaxJumpDist) return false;
        float lo = Mathf.InverseLerp(JumpConfig.MinJumpDist, JumpConfig.MaxJumpDist, near);
        float hi = Mathf.InverseLerp(JumpConfig.MinJumpDist, JumpConfig.MaxJumpDist, far);
        phaseRange = _reverseNext ? new Vector2(1f - hi, 1f - lo) : new Vector2(lo, hi);
        return true;
    }

    public void CancelCharge()
    {
        if (State != JumpState.Charging) return;
        State = JumpState.Ready;
        if (_character != null) _character.ResetVisual();
        _swallowNextRelease = true;
    }

    public void SetPaused(bool paused)
    {
        _userPaused = paused;
        ApplyTimeFreeze();
    }

    private bool _userPaused, _ownsTimePause;
    private bool _wheelLoading, _runTransitioning;
    private float _savedTimeScale = 1f, _burnTotal;
    private Action _onWxShow;

    private void ApplyTimeFreeze()
    {
        if (TrapTimeFrozen && !_ownsTimePause)
        {
            _savedTimeScale = Time.timeScale;
            _ownsTimePause = true;
            Time.timeScale = 0f;
        }
        else if (!TrapTimeFrozen) ReleaseTimeFreeze();
    }

    private void ReleaseTimeFreeze()
    {
        if (!_ownsTimePause) return;
        Time.timeScale = _savedTimeScale;
        _ownsTimePause = false;
    }

    private async UniTask WaitForPresentationResumeAsync(int version)
    {
        while (this != null && Instance == this && version == _presentationVersion && TrapTimeFrozen)
            await UniTask.Yield();
    }
    /// <summary>转盘弹窗已关闭（PanelLuckyWheel.OnClose 回调：解除冻结）</summary>
    public void NotifyWheelClosed()
    {
        _wheelOpen = false;
        ApplyTimeFreeze();
    }

    private readonly List<JumpBlock> _blocks = new List<JumpBlock>();
    private JumpBlock _current;   // 角色所站块
    private JumpBlock _next;      // 下一目标块
    private JumpCharacter _character;
    private JumpFx _fx;
    private Camera _cam;
    private MonoDriver _driver;

    private float _chargeStart;
    private bool _swallowNextRelease; // 微信切后台取消蓄力后，吞掉一次松手
    private float _itemTimer;
    private int _combo;
    private int _maxCombo;
    private int _perfectCount;
    private Color _lastBlockColor;
    private bool _poolReady;
    private bool _booted;

    // 砖上物品（本局状态）
    private readonly Dictionary<JumpItemKind, GameObject> _itemModels = new Dictionary<JumpItemKind, GameObject>(); // 模型prefab缓存（开局装载，OnDestroy 释放）
    private float _scoreMult;          // 本局积分倍率加成（0..1）
    private int _perfectExtra;         // 完美落点额外分（积分增益后 = ScoreBoostPerfectBonus）
    private int _shieldCount;          // 护盾层数
    private bool _runDouble;           // 本局收益翻倍（转盘奖励）
    private JumpItemKind _gmItemKind; // GM 强制第 3 块（index=2）物品（测试通道；None=随机）

    // 陷阱砖（本局状态）
    private readonly Dictionary<JumpTrapKind, GameObject> _trapModels = new Dictionary<JumpTrapKind, GameObject>(); // 标记模型缓存
    private int _slowChargeCount;       // 迷雾：剩余减速蓄力次数
    private float _chargeScale = 1f;    // 本次蓄力耗时倍率（迷雾激活时 = SlowChargeFactor）
    private bool _reverseNext;          // 反向：下一次蓄力 power 反转
    private JumpBlock _burnBlock;       // 燃烧：倒计时所在块（null=无）
    private float _burnRemain;          // 燃烧：剩余秒（remain 制：转盘/切后台冻结推进）
    private int _phantomCount;          // 幻影：本关已刷数量（≤2）
    private int _lastPhantomIndex = -10; // 幻影：上一个幻影块 index（绝不连续）
    private JumpTrapKind _gmTrapKind;  // GM 强制第 4 块（index=3）陷阱（测试通道；None=随机）

    // 有限关卡模式（本局状态）
    private LevelRunPlan _plan;             // 烘焙关卡计划（一次对局持有同一对象——回滚语义基石）
    private ITileEffectResolver _resolver;        // 落格效果解析（数值出口：全局经济配置 + effectSeed）
    private LevelEconomyConfig _economy;          // 全局经济配置缓存
    private int _runCoins;                         // 本关金币累计（结算统一入账）
    private bool _coinDouble;                      // 转盘：本关金币收益翻倍（含触发前已得）
    private bool _wheelOpen;                       // 转盘弹窗打开中（冻结陷阱计时/物品停留）
    private bool _appPaused;                       // 切后台（OnApplicationPause）
    private JumpBlock _phantomBlock;               // 幻影：倒计时所在块
    private float _phantomRemain;                   // 幻影：剩余秒
    private float _phantomTotal;                    // 幻影：倒计时总长（闪烁进度归一）

    // 关卡
    private int _level = 1;                                        // 当前关卡（ResetWorld 从存档刷新）
    private int _levelTarget = JumpConfig.GetLevelTargetBlocks(1); // 本关目标块数（HUD 先于世界构建可见）
    private int _levelStart;                                        // 难度起始偏移（借既有曲线）

    // 镜头
    private Vector3 _camPos;
    private Vector3 _lookPos;   // 平滑注视点：与位置同频插值，消除落地时 LookAt 的旋转突甩
    private float _camDip;
    private float _dipTime = -1f; // 落地微沉正弦脉冲计时（<0 未激活）
    private float _camShake;

    // 事件处理器缓存（OnDestroy 反注册）
    private Action<DragArgs> _onDragStart;
    private Action<DragArgs> _onDragEnd;
    private Action _onWxHide;
    private Action _onTick;

    // ---------- 入口 ----------

    public static void StartGame()
    {
        if (!Application.isPlaying) return; // Runtime entry must never create a world in edit mode.
        if (Instance != null)
        {
            if (Instance._booted) Instance.Restart();
            return;
        }
        var go = new GameObject("JumpGameRoot");
        Instance = go.AddComponent<JumpGame>();
        Instance.BootAsync().Forget();
    }

    /// <summary>回主页：销毁世界 + 关闭游戏UI + 打开 Home（Instance 为空时只做 UI 收尾）</summary>
    public static async UniTaskVoid ExitToHome()
    {
        var inst = Instance;
        if (inst != null)
        {
            inst.CancelCharge();
            inst._runTransitioning = true;
            inst._presentationVersion++;
            inst.State = JumpState.Idle;
            await inst.CloseWheelForTransitionAsync();
            inst.ReleaseTimeFreeze();
            inst.ClearWorld(); // 池实例挂在 DontDestroyOnLoad 的 PoolRoot 下，必须显式回收（否则孤儿方块残留场景）
            Destroy(inst.gameObject); // OnDestroy 反注册事件
        }

        UIMgr.Instance.ClosePanel(JumpConfig.ResultPanelKey, false).Forget();
        UIMgr.Instance.ClosePanel(JumpConfig.HudPanelKey, false).Forget();
        await UIMgr.Instance.OpenPanel(JumpConfig.HomePanelKey, UILayer.Page);

        // 开局切过 BGM 才需要切回
        if (AudioMgr.Instance.CurrentBgmKey == JumpConfig.BgmKey)
            AudioMgr.Instance.PlayBgm(JumpConfig.HomeBgmKey).Forget();
    }

    private async UniTaskVoid BootAsync()
    {
        try
        {
            await InitAsync();
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
    }

    private async UniTask InitAsync()
    {
        _cam = Camera.main;
        if (_cam == null)
        {
            Debug.LogError("[JumpGame] 场景缺少主相机，无法开局");
            return;
        }

        // 环境：场景已应用天空盒则用天空盒；未应用时纯色远景兜底（与雾同色）
        _cam.clearFlags = RenderSettings.skybox != null ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;
        _cam.backgroundColor = JumpConfig.FogColor;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Exponential;
        RenderSettings.fogColor = JumpConfig.FogColor;
        RenderSettings.fogDensity = JumpConfig.FogDensity;

        // 方块池（模板缺失时自动退化为直建直毁）
        await PoolMgr.Instance.RegisterPoolAsync(JumpConfig.BlockPoolKey, JumpConfig.BlockPrefabResKey, 4);
        _poolReady = PoolMgr.Instance.GetInactiveCount(JumpConfig.BlockPoolKey) >= 0;
        if (!_poolReady)
            Debug.LogWarning("[JumpGame] 方块池模板缺失，回退为直建直毁（请执行菜单 JumpGame/1. Create Assets）");
        if (this == null) return; // 等待期间被销毁（回主页等）

        // 砖上物品模型预载（六种一次载入缓存；缺失仅无悬浮演出，不阻塞）
        for (int k = (int)JumpItemKind.Wheel; k <= (int)JumpItemKind.Sprint; k++)
        {
            var kind = (JumpItemKind)k;
            string itemKey = JumpConfig.GetItemResKey(kind);
            var itemPrefab = await ResMgr.Instance.LoadAssetAsync<GameObject>(itemKey);
            if (this == null) return;
            if (itemPrefab != null) _itemModels[kind] = itemPrefab;
            else Debug.LogWarning($"[JumpGame] 物品模型缺失: {itemKey}（该物品将无悬浮演出）");
        }

        // 陷阱标记模型预载（七种；缺失仅无标记，不阻塞）
        for (int k = (int)JumpTrapKind.Slippery; k <= (int)JumpTrapKind.Seal; k++)
        {
            var kind = (JumpTrapKind)k;
            string trapKey = JumpConfig.GetTrapResKey(kind);
            var trapPrefab = await ResMgr.Instance.LoadAssetAsync<GameObject>(trapKey);
            if (this == null) return;
            if (trapPrefab != null) _trapModels[kind] = trapPrefab;
            else Debug.LogWarning($"[JumpGame] 陷阱标记模型缺失: {trapKey}（该陷阱将无标记）");
        }

        // 角色与粒子
        _character = JumpCharacter.Create(transform);
        _fx = JumpFx.Create(transform);

        // 音频探测 + BGM 切换
        await JumpAudio.ProbeAsync();
        JumpAudio.SwitchBgmAsync().Forget();
        if (this == null) return;

        // 事件
        _onDragStart = OnDragStart;
        _onDragEnd = OnDragEnd;
        _onWxHide = OnWxHide;
        _onWxShow = () => OnApplicationPause(false);
        _onTick = OnTick;
        EventCenter.AddListener<DragArgs>(EventKeys.DragStart, _onDragStart);
        EventCenter.AddListener<DragArgs>(EventKeys.DragEnd, _onDragEnd);
        EventCenter.AddListener(EventKeys.WXHide, _onWxHide);
        EventCenter.AddListener(EventKeys.WXShow, _onWxShow);
        _driver = MonoDriver.Instance;
        _driver.UpdateTick += _onTick;

        // HUD 与首页
        if (await UIMgr.Instance.OpenPanel(JumpConfig.HudPanelKey, UILayer.Top) == null)
            Debug.LogWarning("[JumpGame] HUD 面板缺失（请执行菜单 JumpGame/1），游戏仍可进行");
        if (this == null) return;
        await UIMgr.Instance.ClosePanel(JumpConfig.HomePanelKey, false);
        if (this == null) return;

        // 有限关卡：经济配置 + 本局计划解析（编辑器试玩注入优先；无包回退旧无限模式）
        _economy = await LevelRuntime.LoadEconomyAsync();
        _resolver = new LevelTileEffectResolver(_economy);
        if (this == null) return;
        _plan = await LevelRuntime.ResolvePlanForNewRunAsync();
        if (this == null) return;
        if (LevelRuntime.Status == LevelRuntime.PackStatus.SchemaUnsupported)
        {
            // 不支持的关卡包结构：阻止开局并提示更新（策划案）
            Debug.LogError("[JumpGame] 关卡包版本不受支持：已阻止开局，请更新游戏");
            _booted = true;
            return;
        }

        ResetWorld();
        _booted = true;
        EventCenter.Dispatch(JumpEvents.Started);
        Debug.Log("[JumpGame] 开局完成：长按蓄力，松手起跳");
    }

    // ---------- 世界 ----------

    /// <summary>
    /// 重开一局（世界复位，HUD 保留）。有限关卡重新解析计划：通关推进顺序位、
    /// 包回滚后新对局即用回滚版本——进行中的对局持有原计划对象不受影响。
    /// </summary>
    public void Restart() => RestartAsync().Forget();

    private async UniTaskVoid RestartAsync()
    {
        if (_runTransitioning) return;
        _runTransitioning = true;
        CancelCharge();
        int version = ++_presentationVersion;
        State = JumpState.Idle; // Invalidate old actions before any await or pool reuse.
        try
        {
            await CloseWheelForTransitionAsync();
            var plan = await LevelRuntime.ResolvePlanForNewRunAsync();
            if (this == null || Instance != this || version != _presentationVersion) return;
            _userPaused = false;
            ApplyTimeFreeze();
            if (LevelRuntime.Status == LevelRuntime.PackStatus.SchemaUnsupported)
            {
                Debug.LogError("[JumpGame] 关卡包版本不受支持：已阻止开局，请更新游戏");
                ClearWorld();
                return;
            }
            _plan = plan;
            ResetWorld();
        }
        finally { if (this != null) _runTransitioning = false; }
    }

    /// <summary>清空世界（回收全部池化方块，Index 归零前的显式收尾；ExitToHome 调用）</summary>
    private void ClearWorld()
    {
        _presentationVersion++;
        State = JumpState.Idle; // 使在途跳跃/动画循环失效
        foreach (var b in _blocks) RecycleBlock(b);
        _blocks.Clear();
        _current = null;
        _next = null;
    }

    private int _presentationVersion;

    private void ResetWorld()
    {
        _userPaused = false;
        _wheelOpen = false;
        ApplyTimeFreeze();
        _presentationVersion++;
        State = JumpState.Idle; // 先置 Idle 使在途动画/跳跃循环失效
        foreach (var b in _blocks) RecycleBlock(b);
        _blocks.Clear();
        _current = null;
        _next = null;

        Score = 0;
        _combo = 0;
        _maxCombo = 0;
        _perfectCount = 0;
        _itemTimer = 0f;
        _swallowNextRelease = false;
        EverCharged = false;
        _scoreMult = 0f;
        _perfectExtra = 0;
        _shieldCount = 0;
        _runDouble = false;
        _slowChargeCount = 0;
        _chargeScale = 1f;
        _reverseNext = false;
        _burnBlock = null;
        _burnRemain = 0f;
        _phantomBlock = null;
        _phantomRemain = 0f;
        _phantomTotal = 0f;
        _phantomCount = 0;
        _lastPhantomIndex = -10;
        _runCoins = 0;
        _coinDouble = false;
        _wheelOpen = false;

        if (_plan != null)
        {
            // ---- 有限关卡：整关全量铺烘焙瓦片（0..终点；尺寸/位置/物品/陷阱按格烘焙）----
            _level = _plan.Seq;
            _levelTarget = _plan.EndIndex;
            _levelStart = 0;
            for (int i = 0; i < _plan.TileCount; i++) SpawnBlockFromPlanTile(i);

            _current = _blocks[0];
            _next = _blocks.Count > 1 ? _blocks[1] : null;
            _character.ResetVisual();
            _character.SnapTo(_current.TopCenter);
            _character.FaceAxis(TravelAxis(0));

            State = JumpState.Ready;
            SnapCamera();
            DispatchScore(0, false, _current.TopCenter);
            Debug.Log($"[JumpGame] 有限关卡开局：{_plan.DisplayName}（{_plan.TileCount} 格 · 目标 {_plan.TargetJumps} 跳）");
            return;
        }

        // ---- 旧无限模式回退（无烘焙包）----
        // 关卡：从存档读当前关（默认 1），推导目标块数与难度起始偏移
        var save = DataMgr.Instance.Load<JumpSaveData>(JumpSaveData.SaveKey, true);
        _level = Mathf.Max(1, save?.level ?? 1);
        _levelTarget = JumpConfig.GetLevelTargetBlocks(_level);
        _levelStart = JumpConfig.GetLevelStartIndex(_level);

        // 整关全量生成：进关即铺完整浮岛链（0..终点块），关卡一览无余
        SpawnBlock(0, null);
        for (int i = 1; i <= _levelTarget; i++) SpawnBlock(i, _blocks[i - 1]);

        _current = _blocks[0];
        _next = _blocks.Count > 1 ? _blocks[1] : null;
        _character.ResetVisual();
        _character.SnapTo(_current.TopCenter);
        _character.FaceAxis(AxisOf(0));

        State = JumpState.Ready;
        SnapCamera();
        DispatchScore(0, false, _current.TopCenter);
    }

    // ---------- 有限关卡：世界构建与轴 ----------

    /// <summary>按烘焙瓦片铺块：位置为烘焙终点（含震荡横移）——震荡格初位于理想落点，触发陷阱时滑到位</summary>
    private void SpawnBlockFromPlanTile(int i)
    {
        bool goal = i == _plan.EndIndex;
        var trap = (JumpTrapKind)_plan.Traps[i];
        var item = (JumpItemKind)_plan.Rewards[i];
        Vector3 pos = _plan.Position(i);
        if (i > 0 && Mathf.Abs(_plan.ShakeOffsets[i]) > 0.0005f)
            pos -= LateralOf(_plan.ArrivalAxis[i]) * _plan.ShakeOffsets[i]; // 初位 = 烘焙终点 - 横移

        var block = CreateBlockObject(pos);
        Color color = goal ? JumpConfig.GoalBlockColor
            : trap != JumpTrapKind.None ? JumpConfig.GetTrapBlockColor(trap)
            : PickBlockColor();
        block.Setup(i, _plan.Sizes[i], color, trap == JumpTrapKind.Phantom, !goal && trap == JumpTrapKind.None);
        block.ItemKind = item;
        block.TrapKind = trap;
        if (item != JumpItemKind.None)
            block.ShowItemModel(_itemModels.TryGetValue(item, out var prefab) ? prefab : null);
        else if (trap != JumpTrapKind.None)
            block.ShowItemModel(_trapModels.TryGetValue(trap, out var tPrefab) ? tPrefab : null);
        _blocks.Add(block);
    }

    /// <summary>从 fromIndex 起跳的行进轴（有限关卡=计划烘焙轴；旧模式=之字交替）</summary>
    private Vector3 TravelAxis(int fromIndex)
        => _plan != null
            ? (_plan.TravelAxis(fromIndex) == TileAxis.X ? Vector3.right : Vector3.forward)
            : AxisOf(fromIndex);

    /// <summary>横轴（垂直于行进轴；震荡烘焙偏移方向）</summary>
    private static Vector3 LateralOf(TileAxis axis)
        => axis == TileAxis.X ? Vector3.forward : Vector3.right;

    /// <summary>第 jumpIndex 跳的行进轴（0 起，之字交替 +X/+Z）</summary>
    private static Vector3 AxisOf(int jumpIndex)
        => jumpIndex % 2 == 0 ? Vector3.right : Vector3.forward;

    private JumpBlock SpawnBlock(int index, JumpBlock prev)
    {
        bool goal = index == _levelTarget; // 终点块：金色，通关判定锚点（无物品无陷阱）
        float size = JumpConfig.GetBlockSize(index + _levelStart);
        Vector3 pos = Vector3.zero;
        if (prev != null)
        {
            Vector3 axis = AxisOf(prev.Index);
            float gap = JumpConfig.GetGap(prev.Index + _levelStart);
            float d = Mathf.Clamp(
                prev.Size * 0.5f + gap + size * 0.5f,
                JumpConfig.MinJumpDist, JumpConfig.MaxCenterDist);
            pos = prev.TopCenter + axis * d;
        }

        // 分配：陷阱优先、物品互斥（一块只带一种）；GM 强制通道各自把关
        JumpTrapKind trap = RollTrapFor(index, goal);
        JumpItemKind item = trap == JumpTrapKind.None ? RollItemFor(index, goal) : JumpItemKind.None;

        var block = CreateBlockObject(pos);
        Color color = goal ? JumpConfig.GoalBlockColor
            : trap != JumpTrapKind.None ? JumpConfig.GetTrapBlockColor(trap)
            : PickBlockColor();
        block.Setup(index, size, color, trap == JumpTrapKind.Phantom, !goal && trap == JumpTrapKind.None);
        block.ItemKind = item;
        block.TrapKind = trap;
        if (item != JumpItemKind.None)
            block.ShowItemModel(_itemModels.TryGetValue(item, out var prefab) ? prefab : null);
        else if (trap != JumpTrapKind.None)
            block.ShowItemModel(_trapModels.TryGetValue(trap, out var tPrefab) ? tPrefab : null);
        _blocks.Add(block);
        return block;
    }

    /// <summary>陷阱滚动：起手区/终点块/L1 零陷阱（新手保护）；幻影≤2 且绝不连续；反向仅高阶关；GM 强制第 4 块优先</summary>
    private JumpTrapKind RollTrapFor(int index, bool goal)
    {
        if (goal || index < JumpConfig.TrapFirstIndex || index >= _levelTarget) return JumpTrapKind.None;
        if (index == 3 && _gmTrapKind != JumpTrapKind.None) return _gmTrapKind; // GM 通道越过所有门槛（测试用）
        if (_level < JumpConfig.TrapMinLevel) return JumpTrapKind.None;
        if (RandomUtil.Range(0, 100) >= JumpConfig.TrapSpawnChance * 100f) return JumpTrapKind.None;

        var kind = JumpConfig.RollTrapKind();
        if (kind == JumpTrapKind.Reverse && _level < JumpConfig.ReverseMinLevel) return JumpTrapKind.None;
        if (kind == JumpTrapKind.Phantom)
        {
            if (_phantomCount >= JumpConfig.PhantomMaxPerLevel) return JumpTrapKind.None;
            if (index == _lastPhantomIndex + 1) return JumpTrapKind.None; // 绝不连续刷
            _phantomCount++;
            _lastPhantomIndex = index;
        }
        return kind;
    }

    /// <summary>物品滚动：起手区/终点块不刷；GM 强制第 3 块优先</summary>
    private JumpItemKind RollItemFor(int index, bool goal)
    {
        if (goal || index < JumpConfig.ItemFirstIndex || index >= _levelTarget) return JumpItemKind.None;
        if (index == JumpConfig.ItemFirstIndex && _gmItemKind != JumpItemKind.None) return _gmItemKind;
        if (RandomUtil.Range(0, 100) < JumpConfig.ItemSpawnChance * 100f) return JumpConfig.RollItemKind();
        return JumpItemKind.None;
    }

    private Color PickBlockColor()
    {
        var palette = JumpConfig.BlockPalette;
        Color c = palette[RandomUtil.Range(0, palette.Length)];
        if (palette.Length > 1 && c == _lastBlockColor)
            c = palette[(Array.IndexOf(palette, c) + 1) % palette.Length]; // 避免连续同色
        _lastBlockColor = c;
        return c;
    }

    private JumpBlock CreateBlockObject(Vector3 pos)
    {
        if (_poolReady)
        {
            GameObject go = PoolMgr.Instance.Spawn(JumpConfig.BlockPoolKey, pos, Quaternion.identity);
            return go != null ? go.GetComponent<JumpBlock>() : null;
        }
        // 池模板缺失兜底：直建
        var root = new GameObject("JumpBlock");
        root.transform.position = pos;
        var block = root.AddComponent<JumpBlock>();
        block.BuildPlatform();
        return block;
    }

    private void RecycleBlock(JumpBlock block)
    {
        if (block == null) return;
        if (_poolReady) PoolMgr.Instance.Despawn(JumpConfig.BlockPoolKey, block.gameObject);
        else Destroy(block.gameObject);
    }

    /// <summary>按 Index 取已生成的方块（整关全量加载后无渐进生成，落地推进只做查找）</summary>
    private JumpBlock FindBlock(int index)
    {
        for (int i = 0; i < _blocks.Count; i++)
        {
            var b = _blocks[i];
            if (b != null && b.Index == index) return b;
        }
        return null;
    }

    // ---------- 输入 ----------

    private void OnDragStart(DragArgs args)
    {
        if (this == null || Instance != this || State != JumpState.Ready || TrapTimeFrozen) return;
        if (InputMgr.Instance.RaycastUI(args.Position) != null) return; // 按在 UI（按钮/遮罩/控制台）上不蓄力
        BeginCharge();
    }

    private void BeginCharge()
    {
        if (State != JumpState.Ready || TrapTimeFrozen) return;
        _swallowNextRelease = false;
        State = JumpState.Charging;
        _chargeStart = Time.time;
        _chargeScale = _slowChargeCount > 0 ? JumpConfig.SlowChargeFactor : 1f; // 迷雾：本次蓄力减速
        _itemTimer = 0f; // 蓄力即中断物品停留
        EverCharged = true;
    }

    private void OnDragEnd(DragArgs args)
    {
        if (this == null || Instance != this) return;
        if (_swallowNextRelease)
        {
            _swallowNextRelease = false; // 切后台取消后吞掉这次松手
            return;
        }
        if (State != JumpState.Charging) return;
        if (args.Canceled || TrapTimeFrozen || ChargeElapsed >= JumpConfig.ChargeCancelSeconds ||
            (UIBattleView.Active != null && UIBattleView.Active.IsInCancelZone(args.Position)))
        {
            CancelCharge();
            return;
        }
        DoJump(GetPower());
    }

    private float GetPower()
    {
        if (State != JumpState.Charging) return 0f;
        float p = ChargePhase;
        return _reverseNext ? 1f - p : p; // 反向蓄力陷阱：短按=跳得远，长按=跳得近
    }

    private void OnWxHide()
    {
        if (this == null || Instance != this) return;
        OnApplicationPause(true);
    }

    // ---------- 跳跃与判定 ----------

    private void DoJump(float power)
    {
        // 陷阱状态随起跳消耗：迷雾-1 次；反向只作用一次（power 已在 GetPower 内反转）
        if (_chargeScale > 1f && _slowChargeCount > 0) _slowChargeCount--;
        _reverseNext = false;

        Vector3 axis = TravelAxis(_current.Index);
        Vector3 from = _character.transform.position;
        float dist = Mathf.Lerp(JumpConfig.MinJumpDist, JumpConfig.MaxJumpDist, power);
        Vector3 to = _current.TopCenter + axis * dist;
        JumpAsync(from, to, dist, power, axis).Forget();
    }

    private async UniTaskVoid JumpAsync(Vector3 from, Vector3 to, float dist, float power, Vector3 axis)
    {
        int version = _presentationVersion;
        State = JumpState.Airborne;
        _character.FaceAxis(axis);
        float duration = JumpConfig.AirTimeBase + JumpConfig.AirTimePerPower * power;
        _character.PlayLaunch(duration);
        JumpAudio.Play(JumpConfig.SfxFly, 0.75f);

        float height = JumpConfig.JumpHeightBase + JumpConfig.JumpHeightPerDist * dist;
        float t = 0f;
        while (t < duration)
        {
            if (!IsRunActive(version) || State != JumpState.Airborne) return; // 重开/退出打断
            t += Time.deltaTime;
            float p = Mathf.Clamp01(t / duration);
            Vector3 pos = Vector3.Lerp(from, to, p);
            pos.y = height * 4f * p * (1f - p); // 起终点都在 y=0 平面
            _character.transform.position = pos;
            await UniTask.Yield();
        }
        await WaitForPresentationResumeAsync(version);
        if (!IsRunActive(version) || State != JumpState.Airborne) return;
        EvaluateLanding(to, axis);
    }

    private void EvaluateLanding(Vector3 landPos, Vector3 axis)
    {
        // 双块判定：优先下一块；跳太短落回原块也存活（手残容错）
        float halfNext = _next.Size * 0.5f;
        float offNext = Vector3.Dot(landPos - _next.TopCenter, axis);
        bool onNext = Mathf.Abs(offNext) <= halfNext;

        float halfCur = _current.Size * 0.5f;
        float offCur = Vector3.Dot(landPos - _current.TopCenter, axis);
        bool onCur = !onNext && Mathf.Abs(offCur) <= halfCur;

        if (onNext) { LandOnBlock(landPos, _next, offNext, halfNext, true); return; }
        if (onCur) { LandOnBlock(landPos, _current, offCur, halfCur, false); return; }
        if (_shieldCount > 0) { ShieldRevive(); return; } // 护盾：掉落免疫，自动复活不中断
        DieAsync(landPos, axis).Forget();
    }

    private void LandOnBlock(Vector3 landPos, JumpBlock block, float off, float half, bool advance)
    {
        _character.transform.position = landPos;
        _character.PlayLand();
        _fx.Dust(landPos);
        JumpAudio.Play(JumpConfig.SfxLand, 1f);
        WeChatService.Vibrate(false);
        _dipTime = 0f; // 启动落地微沉脉冲（正弦起落，不再单帧瞬跳）

        if (!advance)
        {
            // 落回原块：容错存活，不计分、连击保留
            State = JumpState.Ready;
            return;
        }

        bool perfect = Mathf.Abs(off) <= half * JumpConfig.PerfectRadiusScale;
        int gain;
        if (perfect)
        {
            _combo++;
            _maxCombo = Mathf.Max(_maxCombo, _combo);
            _perfectCount++;
            gain = Mathf.Min(1 << _combo, JumpConfig.PerfectComboCap); // 2/4/8/16/32…
            JumpAudio.Play(JumpConfig.SfxPerfect, 1f);
            _fx.Stars(landPos);
            EventCenter.Dispatch(JumpEvents.Perfect, _combo);
        }
        else
        {
            _combo = 0;
            gain = JumpConfig.BaseLandScore;
        }
        // 积分增益砖倍率 + 完美额外分 + 转盘双倍收益（顺序：基础→倍率加成→双倍）
        gain = Mathf.RoundToInt(gain * (1f + _scoreMult)) + (perfect ? _perfectExtra : 0);
        if (_runDouble) gain *= 2;
        Score += gain;

        if (Mathf.Abs(off) > half * JumpConfig.WobbleStartScale)
            _character.PlayWobble(); // 惊险边缘摇晃（仍存活）

        // 推进世界（整关已全量加载：只推进游标，不再生成）
        _current = block;
        _itemTimer = 0f;
        if (_burnBlock != null && _burnBlock != block) _burnBlock = null; // 及时起跳逃离燃烧砖：倒计时解除
        if (_phantomBlock != null && _phantomBlock != block)
            _phantomBlock = null; // 幻影：倒计时内跳走即规避（离开后塌陷不再追杀）
        _next = FindBlock(block.Index + 1);
        _character.FaceAxis(TravelAxis(_current.Index));
        DispatchScore(gain, perfect, landPos);

        // 踩中终点块：通关结算（终点后无块，超跳者坠雾）
        if (block.Index >= _levelTarget)
        {
            LevelClearAsync().Forget();
            return;
        }

        State = JumpState.Ready;
        TriggerTrap(block); // 陷阱砖：落地即触发（终点块无陷阱；未触发过的才生效）
    }

    private void DispatchScore(int gain, bool perfect, Vector3 pos)
    {
        EventCenter.Dispatch(JumpEvents.ScoreChanged, new JumpScoreArgs
        {
            Score = Score,
            Gain = gain,
            Perfect = perfect,
            Combo = _combo,
            WorldPos = pos + Vector3.up * 0.6f
        });
    }

    // ---------- 死亡与结算 ----------

    private async UniTaskVoid DieAsync(Vector3 fromPos, Vector3 axis)
    {
        int version = _presentationVersion;
        State = JumpState.Dead;
        _character.ResetVisual();
        _character.PlayFall();
        JumpAudio.Play(JumpConfig.SfxFall, 1f);
        WeChatService.Vibrate(true);
        _camShake = JumpConfig.CamShakeDeath;

        const float duration = 0.55f;
        Vector3 end = fromPos + axis * 0.6f;
        end.y = JumpConfig.DeathFallY;
        float t = 0f;
        while (t < duration)
        {
            // 重开/退出打断；State 离开 Dead（ResetWorld 会先置 Idle）同样中止——
            // 否则旧局的坠落动画会把结果页弹进新局
            if (!IsRunActive(version) || State != JumpState.Dead) return;
            t += Time.deltaTime;
            float p = Mathf.Clamp01(t / duration);
            Vector3 pos = Vector3.Lerp(fromPos, end, p);
            pos.y += 1.0f * 4f * p * (1f - p); // 前段微抛，后段坠入雾中
            _character.transform.position = pos;
            await UniTask.Yield();
        }
        await WaitForPresentationResumeAsync(version);
        if (!IsRunActive(version) || State != JumpState.Dead) return;
        FinishRun();
    }

    private void FinishRun(bool cleared = false)
    {
        State = JumpState.Over;

        if (_plan != null)
        {
            // ---- 有限关卡结算：金币统一入账（含通关金币 × 档位倍率）+ 单关最高分 + 累计通关数 ----
            if (cleared && _economy != null && !_plan.IsPlaytest)
                AddRunCoins(Mathf.RoundToInt(_economy.clearCoinBase * _plan.ClearCoinMult));
            int coinsGained = _runCoins;
            if (coinsGained > 0 && !_plan.IsPlaytest)
                PlayerMgr.Instance.AddCoins(coinsGained); // 试玩局不落任何进度与经济

            var result = LevelRuntime.ReportRun(_plan, Score, coinsGained, cleared);
            var rec = LevelRuntime.GetRecord(_plan.LevelId);

            var args = new JumpResultArgs
            {
                Score = Score,
                Best = rec.BestScore,
                NewBest = result?.NewBest ?? false,
                MaxCombo = _maxCombo,
                PerfectCount = _perfectCount,
                Level = _plan.Seq,
                LevelName = _plan.DisplayName,
                LevelProgress = _current != null ? _current.Index : 0,
                LevelTarget = _plan.TargetJumps,
                LevelCleared = cleared,
                CoinsGained = coinsGained,
                LevelId = _plan.LevelId,
                NextLevelName = cleared ? LevelRuntime.NextLevelDisplayName(_plan.Seq) : null,
            };
            EventCenter.Dispatch(JumpEvents.Died, args);
            if (cleared) EventCenter.Dispatch(JumpEvents.LevelCleared, _plan.LevelId);
            UIMgr.Instance.OpenPanel(JumpConfig.ResultPanelKey, UILayer.Page, args).Forget();
            Debug.Log($"[JumpGame] 单局结束：{(cleared ? $"{args.LevelName} 通关" : "本关结束")}，得分 {Score}，本关最高 {args.Best}{(args.NewBest ? "（新纪录）" : "")}，金币 +{coinsGained}");
            return;
        }

        // ---- 旧无限模式结算（无烘焙包回退） ----
        int levelPlayed = _level;
        var save = DataMgr.Instance.Load<JumpSaveData>(JumpSaveData.SaveKey, true) ?? new JumpSaveData();
        bool newBest = Score > save.best;
        if (newBest) save.best = Score;
        save.playCount++;
        save.totalScore += Score;
        if (cleared) save.level = levelPlayed + 1;
        DataMgr.Instance.Save(JumpSaveData.SaveKey, save, true);

        var argsLegacy = new JumpResultArgs
        {
            Score = Score,
            Best = save.best,
            NewBest = newBest,
            MaxCombo = _maxCombo,
            PerfectCount = _perfectCount,
            Level = levelPlayed,
            LevelName = JumpConfig.GetLevelName(levelPlayed),
            LevelProgress = _current != null ? _current.Index : 0,
            LevelTarget = _levelTarget,
            LevelCleared = cleared,
        };
        EventCenter.Dispatch(JumpEvents.Died, argsLegacy);
        if (cleared) EventCenter.Dispatch(JumpEvents.LevelCleared, levelPlayed);
        UIMgr.Instance.OpenPanel(JumpConfig.ResultPanelKey, UILayer.Page, argsLegacy).Forget();
        Debug.Log($"[JumpGame] 单局结束：{(cleared ? $"{argsLegacy.LevelName} 通关" : "游戏结束")}，得分 {Score}，最高 {save.best}{(newBest ? "（新纪录）" : "")}");
    }

    /// <summary>通关演出：撒花 + 震动，短暂窗口后弹结算页（下一关已写入存档）</summary>
    private async UniTaskVoid LevelClearAsync()
    {
        State = JumpState.Over;
        int presentationVersion = ++_presentationVersion;
        _character.ResetVisual();
        _character.PlayCelebrate();
        _fx.Confetti(_current.TopCenter + Vector3.up * 0.5f);
        JumpAudio.Play(JumpConfig.SfxBonus, 1f);
        WeChatService.Vibrate(false);
        await UniTask.Delay((int)(JumpCharacterAnimation.CelebrateDuration * 1000f));
        await WaitForPresentationResumeAsync(presentationVersion);
        if (this == null || Instance != this || State != JumpState.Over
            || presentationVersion != _presentationVersion) return;
        FinishRun(true);
    }

    // ---------- 砖上物品（停留 1.5s 自动触发） ----------

    /// <summary>触发砖上物品效果（统一演出：收模型/撒花/音效/震动；效果按类型分发）</summary>
    private void TriggerItem(JumpItemKind kind)
    {
        _itemTimer = 0f;
        _current.PlayItemCollect();
        JumpAudio.Play(JumpConfig.SfxBonus, 1f);
        WeChatService.Vibrate(false);
        _fx.Confetti(_current.TopCenter + Vector3.up * 0.5f);
        EventCenter.Dispatch(JumpEvents.ItemCollected, kind);

        if (_plan != null)
        {
            TriggerPlannedItem(kind); // 有限关卡：数值经效果解析器（全局经济配置 + effectSeed）
            return;
        }

        switch (kind)
        {
            case JumpItemKind.Wheel:
                // 弹窗挂 Popup 层：全局遮罩自动阻断蓄力输入，关闭后恢复操作
                OpenWheelAsync().Forget();
                break;
            case JumpItemKind.ScoreBoost:
                _scoreMult = Mathf.Min(JumpConfig.ScoreBoostMax, _scoreMult + JumpConfig.ScoreBoostPerStack);
                _perfectExtra = JumpConfig.ScoreBoostPerfectBonus;
                EventCenter.Dispatch(JumpEvents.Toast,
                    $"积分倍率 +{(int)(JumpConfig.ScoreBoostPerStack * 100f)}%（当前 +{(int)(_scoreMult * 100f)}%）");
                break;
            case JumpItemKind.CoinBag:
                int coins = ApplyRunMult(JumpConfig.CoinBagFixed + RandomUtil.Range(0, JumpConfig.CoinBagRandom + 1));
                PlayerMgr.Instance.AddCoins(coins);
                _runCoins += coins; // Legacy mode already credits wallet; HUD only records this run.
                EventCenter.Dispatch(JumpEvents.Toast, $"金币 +{coins}");
                break;
            case JumpItemKind.Shard:
                int shards = ApplyRunMult(JumpConfig.ShardAmount + RandomUtil.Range(0, JumpConfig.ShardRandom + 1));
                PlayerMgr.Instance.AddShards(shards);
                EventCenter.Dispatch(JumpEvents.Toast, $"皮肤碎片 +{shards}");
                break;
            case JumpItemKind.Shield:
                _shieldCount++;
                EventCenter.Dispatch(JumpEvents.Toast, $"护盾 ×{_shieldCount} 已就绪");
                break;
            case JumpItemKind.Sprint:
                SprintAsync().Forget();
                break;
        }
    }

    /// <summary>
    /// 有限关卡：按效果解析器结算砖上奖励（数值出口=全局经济配置+烘焙 effectSeed）。
    /// 金币砖/护盾转换金币/转盘金币走本关累计（结算统一入账）；转盘打开期间冻结陷阱计时。
    /// </summary>
    private void TriggerPlannedItem(JumpItemKind kind)
    {
        var res = _resolver.ResolveLanding(_plan.Tile(_current.Index), new RunStateSnapshot
        {
            ShieldCount = _shieldCount,
            CoinDoubleActive = _coinDouble,
            ScoreMult = _scoreMult,
        });
        switch (kind)
        {
            case JumpItemKind.Wheel:
                OpenWheelAsync().Forget();
                break;
            case JumpItemKind.ScoreBoost:
                _scoreMult = Mathf.Min(res.ScoreMultCap, _scoreMult + res.ScoreMultAdd);
                _perfectExtra = JumpConfig.ScoreBoostPerfectBonus;
                EventCenter.Dispatch(JumpEvents.Toast,
                    $"积分倍率 +{(int)(res.ScoreMultAdd * 100f)}%（当前 +{(int)(_scoreMult * 100f)}%）");
                break;
            case JumpItemKind.CoinBag:
                AddRunCoins(res.Coins);
                EventCenter.Dispatch(JumpEvents.Toast, $"金币 +{res.Coins}");
                break;
            case JumpItemKind.Shard:
                PlayerMgr.Instance.AddShards(res.Shards);
                EventCenter.Dispatch(JumpEvents.Toast, $"皮肤碎片 +{res.Shards}");
                break;
            case JumpItemKind.Shield:
                if (res.ShieldConvertCoins)
                {
                    // 护盾存在时再次踩中：转换为金币（策划案）
                    AddRunCoins(res.Coins);
                    EventCenter.Dispatch(JumpEvents.Toast, $"护盾已就绪：转换为金币 +{res.Coins}");
                }
                else
                {
                    _shieldCount++;
                    EventCenter.Dispatch(JumpEvents.Toast, $"护盾 ×{_shieldCount} 已就绪");
                }
                break;
            case JumpItemKind.Sprint:
                SprintPlanAsync(res.SprintLandIndex).Forget();
                break;
        }
    }

    /// <summary>本关金币累计（结算统一入账；翻倍激活时新入账自动 ×2）</summary>
    public void AddRunCoins(int amount)
    {
        if (amount <= 0) return;
        _runCoins += _coinDouble ? amount * 2 : amount;
    }

    /// <summary>转盘"本关金币收益翻倍"：覆盖通关金币与全部金币砖收益（含触发转盘前已得——累计制回溯）</summary>
    public void SetRunCoinDouble()
    {
        if (_coinDouble) return;
        _coinDouble = true;
        _runCoins *= 2;
        EventCenter.Dispatch(JumpEvents.Toast, $"本关金币收益 ×2！（累计 {_runCoins}）");
    }

    /// <summary>护盾生效：坠落自动复活回最近安全格（所站块顶心），不中断本局</summary>
    private void ShieldRevive()
    {
        _shieldCount--;
        _character.ResetVisual();
        _character.SnapTo(_current.TopCenter);
        _character.FaceAxis(TravelAxis(_current.Index));
        _fx.Stars(_current.TopCenter);
        JumpAudio.Play(JumpConfig.SfxLand, 1f);
        WeChatService.Vibrate(false);
        State = JumpState.Ready;
        EventCenter.Dispatch(JumpEvents.Toast, "护盾生效，自动复活！");
    }

    /// <summary>终点冲刺：连续小跳掠过 3 块（不产出得分），落在块上照常推进/触发下一物品</summary>
    private async UniTaskVoid SprintAsync()
    {
        int version = _presentationVersion;
        int from = _current.Index;
        int to = Mathf.Min(from + JumpConfig.SprintSkipBlocks, _levelTarget);
        State = JumpState.Airborne; // 占用状态阻断输入；逐跳推进游标让镜头平滑跟随
        for (int i = from + 1; i <= to; i++)
        {
            var b = FindBlock(i);
            if (b == null) break;
            await HopAsync(_character.transform.position, b.TopCenter);
            if (!IsRunActive(version) || State != JumpState.Airborne) return; // 重开/退出打断
            _character.transform.position = b.TopCenter;
            _fx.Dust(b.TopCenter);
            _current = b;
            _next = FindBlock(b.Index + 1);
            _character.FaceAxis(TravelAxis(_current.Index));
            DispatchScore(0, false, b.TopCenter); // 刷新 HUD 关卡进度
        }
        if (_current.Index >= _levelTarget)
        {
            LevelClearAsync().Forget(); // 冲刺直达终点：通关
            return;
        }
        State = JumpState.Ready;
    }

    /// <summary>冲刺单跳：短弧滑行（纯推进演出）</summary>
    private async UniTask HopAsync(Vector3 from, Vector3 to)
    {
        int version = _presentationVersion;
        _character.PlayLaunch(JumpConfig.SprintHopSeconds);
        float t = 0f;
        while (t < JumpConfig.SprintHopSeconds)
        {
            if (!IsRunActive(version) || State != JumpState.Airborne) return;
            t += Time.deltaTime;
            float p = Mathf.Clamp01(t / JumpConfig.SprintHopSeconds);
            Vector3 pos = Vector3.Lerp(from, to, p);
            pos.y = JumpConfig.SprintHopHeight * 4f * p * (1f - p);
            _character.transform.position = pos;
            await UniTask.Yield();
        }
        await WaitForPresentationResumeAsync(version);
        if (IsRunActive(version) && State == JumpState.Airborne)
            _character.PlayLand();
    }

    /// <summary>
    /// 有限关卡冲刺（策划案）：不可打断的 1.2s 演出，自动越过后面 3 格落到第 4 格；
    /// 被跳过格不触发、不计分；落点为普通安全格（生成期保证），按普通落地推进并给基础分（不判完美）。
    /// </summary>
    private async UniTaskVoid SprintPlanAsync(int landIndex)
    {
        int version = _presentationVersion;
        int from = _current.Index;
        int to = Mathf.Clamp(landIndex, from + 1, _plan.EndIndex - 1); // 生成期保证不越终点
        State = JumpState.Airborne; // 占用状态阻断输入（重开/退出仍可打断演出循环）
        _itemTimer = 0f;

        // 先推进游标：镜头随冲刺落点平滑跟随
        var landBlock = FindBlock(to);
        _current = landBlock;
        _next = FindBlock(to + 1);
        _character.FaceAxis(TravelAxis(from));
        _character.PlayLaunch(JumpConfig.LevelSprintSeconds);
        JumpAudio.Play(JumpConfig.SfxFly, 0.9f);

        Vector3 start = _character.transform.position;
        Vector3 target = landBlock != null ? landBlock.TopCenter : _plan.Position(to);
        float t = 0f;
        while (t < JumpConfig.LevelSprintSeconds)
        {
            if (!IsRunActive(version) || State != JumpState.Airborne) return;
            t += Time.deltaTime;
            float p = Mathf.Clamp01(t / JumpConfig.LevelSprintSeconds);
            Vector3 pos = Vector3.Lerp(start, target, p);
            pos.y = JumpConfig.LevelSprintHeight * 4f * p * (1f - p);
            _character.transform.position = pos;
            await UniTask.Yield();
        }
        if (!IsRunActive(version) || State != JumpState.Airborne) return;

        await WaitForPresentationResumeAsync(version);
        if (!IsRunActive(version) || State != JumpState.Airborne) return;
        // 落地：普通推进（被跳过格不触发不计分；演出落地不判完美、清连击）
        _character.transform.position = target;
        _character.PlayLand();
        _fx.Dust(target);
        JumpAudio.Play(JumpConfig.SfxLand, 1f);
        WeChatService.Vibrate(false);
        _dipTime = 0f;
        _combo = 0;
        int gain = Mathf.RoundToInt(JumpConfig.BaseLandScore * (1f + _scoreMult));
        Score += gain;
        _character.FaceAxis(TravelAxis(_current.Index));
        DispatchScore(gain, false, target);
        if (_current.Index >= _levelTarget)
        {
            LevelClearAsync().Forget(); // 防御（生成期保证冲刺不直达终点）
            return;
        }
        State = JumpState.Ready;
    }

    /// <summary>幸运转盘奖励：本局收益翻倍（积分与金币产出 ×2）</summary>
    public void SetRunRewardDoubled()
    {
        if (_runDouble) return;
        _runDouble = true;
        EventCenter.Dispatch(JumpEvents.Toast, "本局收益 ×2！");
    }

    /// <summary>本局金币产出套用倍率（双倍收益时 ×2；金币袋/转盘金币统一入口）</summary>
    public int ApplyRunMult(int amount)
        => _runDouble ? amount * 2 : amount;

    // ---------- 陷阱砖（落地即触发；全部带预警、可规避、无秒杀） ----------

    /// <summary>触发当前块陷阱（若携带且未触发过）：收标记、低音警示，效果按类型分发</summary>
    private void TriggerTrap(JumpBlock block)
    {
        if (block == null || block.TrapConsumed) return;
        var kind = block.TrapKind;
        if (kind == JumpTrapKind.None) return;
        block.TrapConsumed = true;
        block.PlayItemCollect(); // 收走标记模型（复用物品容器）
        WeChatService.Vibrate(false);

        if (_plan != null)
        {
            TriggerPlannedTrap(block, kind); // 有限关卡：效果参数由解析器给出（烘焙 effectSeed）
            return;
        }

        switch (kind)
        {
            case JumpTrapKind.Slippery:
                JumpAudio.Play(JumpConfig.SfxLand, 0.6f);
                EventCenter.Dispatch(JumpEvents.Toast, "小心打滑！");
                SlipperySlideAsync(block, RandomUtil.Range(0f, 360f),
                    block.Size * JumpConfig.SlipperySlideRatio).Forget();
                break;
            case JumpTrapKind.Phantom:
                JumpAudio.Play(JumpConfig.SfxFall, 1f);
                CollapseAsync(block, "护盾抵挡了幻影砖！").Forget();
                break;
            case JumpTrapKind.SlowCharge:
                JumpAudio.Play(JumpConfig.SfxLand, 0.6f);
                _slowChargeCount = JumpConfig.SlowChargeJumps;
                EventCenter.Dispatch(JumpEvents.Toast,
                    $"蓄力减速！接下来 {JumpConfig.SlowChargeJumps} 次蓄力变慢");
                break;
            case JumpTrapKind.Shake:
                JumpAudio.Play(JumpConfig.SfxLand, 0.6f);
                ShakeNextBlock(block);
                break;
            case JumpTrapKind.Burn:
                JumpAudio.Play(JumpConfig.SfxFall, 0.6f);
                _burnBlock = block;
                _burnRemain = JumpConfig.BurnSeconds;
                _burnTotal = _burnRemain;
                EventCenter.Dispatch(JumpEvents.Toast, "灼烧！快起跳！");
                break;
            case JumpTrapKind.Reverse:
                JumpAudio.Play(JumpConfig.SfxLand, 0.6f);
                _reverseNext = true;
                EventCenter.Dispatch(JumpEvents.Toast, "下一次蓄力反转：短按跳远！");
                break;
            case JumpTrapKind.Seal:
                JumpAudio.Play(JumpConfig.SfxLand, 0.6f);
                SealItems();
                EventCenter.Dispatch(JumpEvents.Toast, "封印！本关剩余物品已隐匿");
                break;
        }
    }

    /// <summary>
    /// 有限关卡陷阱（策划案数值）：打滑 0.12m 定向（effectSeed 方向）；幻影 1.5s 闪烁后塌陷（跳走可规避）；
    /// 减速刷新为 3 次不叠加；震荡滑向烘焙错位终点（生成期已过可达性校验）；燃烧 2.5s；反向/封印同旧。
    /// </summary>
    private void TriggerPlannedTrap(JumpBlock block, JumpTrapKind kind)
    {
        var res = _resolver.ResolveLanding(_plan.Tile(block.Index), null);
        switch (kind)
        {
            case JumpTrapKind.Slippery:
                JumpAudio.Play(JumpConfig.SfxLand, 0.6f);
                EventCenter.Dispatch(JumpEvents.Toast, "小心打滑！");
                SlipperySlideAsync(block, res.SlipperyAngleDeg, res.SlipperyOffset).Forget();
                break;
            case JumpTrapKind.Phantom:
                JumpAudio.Play(JumpConfig.SfxFall, 1f);
                _phantomBlock = block;
                _phantomRemain = res.PhantomCollapseSeconds;
                _phantomTotal = Mathf.Max(0.01f, res.PhantomCollapseSeconds);
                block.SetPhantomBlink(0f);
                EventCenter.Dispatch(JumpEvents.Toast, "幻影砖塌陷中！快跳走！");
                break;
            case JumpTrapKind.SlowCharge:
                JumpAudio.Play(JumpConfig.SfxLand, 0.6f);
                _slowChargeCount = res.SlowChargeJumps; // 再触发只刷新为 3 次，不叠加
                EventCenter.Dispatch(JumpEvents.Toast,
                    $"蓄力减速！接下来 {res.SlowChargeJumps} 次蓄力变慢");
                break;
            case JumpTrapKind.Shake:
                JumpAudio.Play(JumpConfig.SfxLand, 0.6f);
                ShakePlannedSlideAsync(block).Forget();
                break;
            case JumpTrapKind.Burn:
                JumpAudio.Play(JumpConfig.SfxFall, 0.6f);
                _burnBlock = block;
                _burnRemain = res.BurnSeconds;
                _burnTotal = _burnRemain;
                EventCenter.Dispatch(JumpEvents.Toast, "灼烧！快起跳！");
                break;
            case JumpTrapKind.Reverse:
                JumpAudio.Play(JumpConfig.SfxLand, 0.6f);
                _reverseNext = true;
                EventCenter.Dispatch(JumpEvents.Toast, "下一次蓄力反转：短按跳远！");
                break;
            case JumpTrapKind.Seal:
                JumpAudio.Play(JumpConfig.SfxLand, 0.6f);
                SealItems();
                EventCenter.Dispatch(JumpEvents.Toast, "封印！本关剩余物品已隐匿");
                break;
        }
    }

    /// <summary>打滑：定向小幅滑动（目标点钳制在块顶面内——保留 ≥65% 支撑面积，绝不滑向坠落）</summary>
    private async UniTaskVoid SlipperySlideAsync(JumpBlock block, float angleDeg, float dist)
    {
        int version = _presentationVersion;
        float angle = angleDeg * Mathf.Deg2Rad;
        var dir = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
        Vector3 from = _character.transform.position;
        Vector3 target = from + dir * dist;

        // 限块内：滑移终点钳制到顶面安全范围内
        Vector3 local = target - block.TopCenter;
        float lim = Mathf.Max(0.1f, block.Size * 0.5f - 0.15f);
        local.x = Mathf.Clamp(local.x, -lim, lim);
        local.z = Mathf.Clamp(local.z, -lim, lim);
        target = block.TopCenter + local;

        float t = 0f;
        while (t < JumpConfig.SlipperySlideSeconds)
        {
            if (!IsRunActive(version) || State != JumpState.Ready) return; // 起跳/重开打断滑动
            t += Time.deltaTime;
            float p = Mathf.Clamp01(t / JumpConfig.SlipperySlideSeconds);
            _character.transform.position = Vector3.Lerp(from, target, p);
            await UniTask.Yield();
        }
    }

    /// <summary>塌陷（幻影落地 / 燃烧超时）：云簇缩没——有护盾消耗一层传至下一块（进度照常+1）；无护盾坠落</summary>
    private async UniTaskVoid CollapseAsync(JumpBlock block, string shieldToast)
    {
        int version = _presentationVersion;
        State = JumpState.Airborne; // 占用状态阻断输入
        WeChatService.Vibrate(true);

        float t = 0f;
        const float dur = 0.45f;
        var cloud = block.CloudRoot;
        Vector3 scale0 = cloud != null ? cloud.localScale : Vector3.one;
        while (t < dur && cloud != null)
        {
            if (!IsRunActive(version)) return;
            t += Time.deltaTime;
            cloud.localScale = scale0 * (1f - Mathf.Clamp01(t / dur) * 0.9f);
            await UniTask.Yield();
        }
        await WaitForPresentationResumeAsync(version);
        if (!IsRunActive(version)) return;
        if (cloud != null) cloud.gameObject.SetActive(false);

        if (_shieldCount > 0)
        {
            _shieldCount--;
            var next = FindBlock(block.Index + 1);
            if (next == null) { FinishRun(); return; } // 保险（幻影/燃烧不刷终点）
            _character.ResetVisual();
            _character.SnapTo(next.TopCenter);
            _current = next;
            _next = FindBlock(next.Index + 1);
            _character.FaceAxis(AxisOf(_current.Index));
            _fx.Stars(next.TopCenter);
            JumpAudio.Play(JumpConfig.SfxLand, 1f);
            EventCenter.Dispatch(JumpEvents.Toast, shieldToast);
            if (_current.Index >= _levelTarget)
            {
                LevelClearAsync().Forget(); // 传至终点：通关
                return;
            }
            State = JumpState.Ready;
        }
        else
        {
            DieAsync(_character.transform.position, TravelAxis(block.Index)).Forget();
        }
    }

    /// <summary>震荡：下一块沿横轴轻微偏移滑行（心距可达性保护——偏移若破坏必达则反向/减半）</summary>
    private void ShakeNextBlock(JumpBlock block)
    {
        var next = FindBlock(block.Index + 1);
        if (next == null) return;
        Vector3 axis = AxisOf(block.Index); // 当前跳行进轴
        Vector3 lateral = axis == Vector3.right ? Vector3.forward : Vector3.right; // 横轴（=下一跳行进轴，之字交替）
        float sign = RandomUtil.Range(0, 2) == 0 ? -1f : 1f;
        float delta = JumpConfig.ShakeOffset * sign;

        // 可达性保护：偏移会改变"下一块→再下一块"的轴距，须保持在可跳区间
        var after = FindBlock(next.Index + 1);
        if (after != null)
        {
            Vector3 nextAxis = AxisOf(next.Index);
            float curDist = Mathf.Abs(Vector3.Dot(after.TopCenter - next.TopCenter, nextAxis));
            bool Ok(float d)
            {
                float axisDelta = Vector3.Dot(lateral * d, nextAxis);
                return curDist + axisDelta <= JumpConfig.MaxCenterDist - 0.05f
                    && curDist + axisDelta >= JumpConfig.MinJumpDist + 0.05f;
            }
            if (!Ok(delta)) delta = -delta;
            if (!Ok(delta)) delta *= 0.5f;
        }
        ShakeSlideAsync(next, lateral * delta).Forget();
        EventCenter.Dispatch(JumpEvents.Toast, "震荡！下一块错位了");
    }

    /// <summary>震荡滑行：下一块 0.3s 平滑滑到错位位置（标记是子物体随行）</summary>
    private async UniTaskVoid ShakeSlideAsync(JumpBlock next, Vector3 offset)
    {
        int version = _presentationVersion;
        Vector3 from = next.transform.position;
        Vector3 to = from + offset;
        float t = 0f;
        while (t < JumpConfig.ShakeSlideSeconds)
        {
            if (!IsRunActive(version) || next == null) return;
            t += Time.deltaTime;
            next.transform.position = Vector3.Lerp(from, to, Mathf.Clamp01(t / JumpConfig.ShakeSlideSeconds));
            await UniTask.Yield();
        }
        await WaitForPresentationResumeAsync(version);
        if (!IsRunActive(version) || next == null) return;
        next.transform.position = to;
    }

    /// <summary>封印：本关全部剩余物品轮廓化——隐藏颜色与发光、实际奖励不变（考验记忆与熟练度）</summary>
    private void SealItems()
    {
        foreach (var b in _blocks)
        {
            if (b == null || b.ItemKind == JumpItemKind.None || b.ItemConsumed) continue;
            b.SealItemVisuals();
        }
    }

    /// <summary>有限关卡震荡：下一块滑向烘焙错位终点（0.15–0.25m 横移；生成期已过可达性校验）</summary>
    private async UniTaskVoid ShakePlannedSlideAsync(JumpBlock block)
    {
        int version = _presentationVersion;
        var next = FindBlock(block.Index + 1);
        if (next == null) return;
        int i = next.Index;
        float offset = _plan.ShakeOffsets[i];
        if (Mathf.Abs(offset) <= 0.0005f) return;
        var lateral = LateralOf(_plan.ArrivalAxis[i]);
        Vector3 from = next.transform.position;
        Vector3 to = from + lateral * offset;
        EventCenter.Dispatch(JumpEvents.Toast, "震荡！下一块错位了");
        float t = 0f;
        while (t < JumpConfig.ShakeSlideSeconds)
        {
            if (!IsRunActive(version) || next == null) return;
            t += Time.deltaTime;
            next.transform.position = Vector3.Lerp(from, to, Mathf.Clamp01(t / JumpConfig.ShakeSlideSeconds));
            await UniTask.Yield();
        }
        await WaitForPresentationResumeAsync(version);
        if (!IsRunActive(version) || next == null) return;
        next.transform.position = to;
    }

    // ---------- 每帧驱动 ----------

    private void OnTick()
    {
        if (this == null || _character == null) return;
        if (TrapTimeFrozen) return;
        float dt = Time.deltaTime;

        if (State == JumpState.Charging)
        {
            if (ChargeElapsed >= JumpConfig.ChargeCancelSeconds) CancelCharge();
            else _character.SetCharge(GetPower());
        }

        // 暂停、转盘和切后台期间：物品停留与全部陷阱倒计时冻结（策划案）
        if (State == JumpState.Ready || State == JumpState.Charging)
        {
            var item = CurrentItem;
            if (State == JumpState.Ready && item != JumpItemKind.None)
            {
                _itemTimer += dt;
                if (_itemTimer >= JumpConfig.ItemStaySeconds)
                    TriggerItem(item); // 停留自动触发：无脑领奖
            }

            // 燃烧倒计时：超时未起跳 → 塌落（护盾可挡；起跳则在 LandOnBlock 解除）
            if (_burnBlock != null)
            {
                _burnRemain -= dt;
                if (_burnRemain <= 0f)
                {
                    var burnBlock = _burnBlock;
                    _burnBlock = null;
                    JumpAudio.Play(JumpConfig.SfxFall, 1f);
                    CollapseAsync(burnBlock, "护盾抵挡了燃烧塌落！").Forget();
                }
            }

            // 幻影闪烁倒计时：超时未离开 → 塌落（护盾可挡；跳走则计时解除）
            if (_phantomBlock != null)
            {
                _phantomRemain -= dt;
                float progress = 1f - Mathf.Clamp01(_phantomRemain / _phantomTotal);
                _phantomBlock.SetPhantomBlink(progress);
                if (_phantomRemain <= 0f)
                {
                    var pb = _phantomBlock;
                    _phantomBlock = null;
                    JumpAudio.Play(JumpConfig.SfxFall, 1f);
                    CollapseAsync(pb, "护盾抵挡了幻影塌落！").Forget();
                }
            }
        }

        UpdateCamera(dt);
    }

    /// <summary>切后台：冻结全部陷阱倒计时与物品停留（OnTick 检查 _appPaused）</summary>
    private void OnApplicationPause(bool pause)
    {
        if (pause) CancelCharge();
        _appPaused = pause;
        ApplyTimeFreeze();
    }

    private void OnApplicationFocus(bool focused)
    {
        OnApplicationPause(!focused);
    }

    private async UniTaskVoid OpenWheelAsync()
    {
        if (_wheelOpen || _wheelLoading || _runTransitioning) return;
        int version = _presentationVersion;
        _wheelLoading = true;
        _wheelOpen = true;
        ApplyTimeFreeze();
        try
        {
            var panel = await UIMgr.Instance.OpenPanel(JumpConfig.WheelPanelKey, UILayer.Popup,
                new PanelLuckyWheel.RunContext(this, version));
            if (!IsRunActive(version))
                await UIMgr.Instance.ClosePanel(JumpConfig.WheelPanelKey, true);
            else if (panel == null) NotifyWheelClosed();
        }
        finally { _wheelLoading = false; }
    }

    private async UniTask CloseWheelForTransitionAsync()
    {
        // Opening may still be loading its prefab. Drain it before a new run can open a wheel.
        while (_wheelLoading) await UniTask.Yield();
        await UIMgr.Instance.ClosePanel(JumpConfig.WheelPanelKey, true);
        _wheelOpen = false;
    }

    private void UpdateCamera(float dt)
    {
        if (_cam == null) return;
        // 帧间隔平滑：WebGL 偶发掉帧会让指数插值出现可见顿挫
        float sdt = Time.smoothDeltaTime > 0f ? Time.smoothDeltaTime : dt;
        Vector3 anchor = LookTargetPos(); // 注视点 = 锚点 + 对角线前移（角色压至约 1/4 屏高）
        Vector3 target = anchor + JumpConfig.CamOffset;
        if (State != JumpState.Dead && State != JumpState.Over)
        {
            float k = 1f - Mathf.Exp(-JumpConfig.CamFollowLerp * sdt);
            _camPos = Vector3.Lerp(_camPos, target, k);
            _lookPos = Vector3.Lerp(_lookPos, anchor, k); // 注视点与位置同频插值：消除落地旋转突甩
        }

        // 落地微沉：正弦脉冲（起落皆平滑）
        if (_dipTime >= 0f)
        {
            _dipTime += sdt;
            if (_dipTime >= JumpConfig.CamLandDipTime)
            {
                _dipTime = -1f;
                _camDip = 0f;
            }
            else
            {
                _camDip = JumpConfig.CamLandDip * Mathf.Sin(Mathf.PI * (_dipTime / JumpConfig.CamLandDipTime));
            }
        }

        _camShake = Mathf.MoveTowards(_camShake, 0f, (JumpConfig.CamShakeDeath / JumpConfig.CamShakeTime) * sdt);

        Vector3 shake = _camShake > 0f ? UnityEngine.Random.insideUnitSphere * _camShake : Vector3.zero;
        _cam.transform.position = _camPos + Vector3.down * _camDip + shake;
        _cam.transform.LookAt(_lookPos);
    }

    private Vector3 AnchorPos()
    {
        if (_current == null) return Vector3.zero;
        if (_next == null) return _current.TopCenter;
        return (_current.TopCenter + _next.TopCenter) * 0.5f;
    }

    /// <summary>镜头注视目标：锚点沿路径对角线前移（CamLookAhead）→ 角色落在屏幕约 1/4 高处；
    /// 前移量对位置与注视点同步生效，_camPos=_lookPos+CamOffset 不变式保持，旋转恒定不回甩。</summary>
    private Vector3 LookTargetPos()
        => AnchorPos() + JumpConfig.CamLookAheadDir * JumpConfig.CamLookAhead;

    private void SnapCamera()
    {
        _camPos = LookTargetPos() + JumpConfig.CamOffset;
        _lookPos = LookTargetPos();
        _camDip = 0f;
        _dipTime = -1f;
        _camShake = 0f;
        if (_cam == null) return;
        _cam.transform.position = _camPos;
        _cam.transform.LookAt(_lookPos);
    }

    private void OnDestroy()
    {
        ReleaseTimeFreeze();
        if (Instance == this) Instance = null;
        if (_onDragStart != null) EventCenter.RemoveListener<DragArgs>(EventKeys.DragStart, _onDragStart);
        if (_onDragEnd != null) EventCenter.RemoveListener<DragArgs>(EventKeys.DragEnd, _onDragEnd);
        if (_onWxHide != null) EventCenter.RemoveListener(EventKeys.WXHide, _onWxHide);
        if (_onWxShow != null) EventCenter.RemoveListener(EventKeys.WXShow, _onWxShow);
        if (_driver != null && _onTick != null) _driver.UpdateTick -= _onTick;
        // 物品/陷阱模型预载引用随实例释放（引用计数归零由 ResMgr 统一回收）
        foreach (var kv in _itemModels) ResMgr.Instance.ReleaseAsset(JumpConfig.GetItemResKey(kv.Key));
        _itemModels.Clear();
        foreach (var kv in _trapModels) ResMgr.Instance.ReleaseAsset(JumpConfig.GetTrapResKey(kv.Key));
        _trapModels.Clear();
    }

    // ---------- GM 指令 ----------

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void RegisterGm()
    {
        DebugConsoleMgr.RegisterCommand("jump",
            "jump start|hold|release|jump <p>|die|score <n>|level [<n>]|item <kind>|trap <kind>|best|home（跳一跳调试）",
            OnGmCommand);
    }

    private static void OnGmCommand(string args)
    {
        string[] parts = (args ?? "").Split(' ');
        string cmd = parts.Length > 0 ? parts[0].ToLowerInvariant() : "";
        switch (cmd)
        {
            case "":
            case "start":
            case "reset":
                if (Instance == null) StartGame();
                else Instance.Restart();
                break;
            case "hold": // 模拟按下开始蓄力
                if (Instance != null && Instance.State == JumpState.Ready) Instance.BeginCharge();
                break;
            case "release": // 模拟松手（按真实蓄力时长起跳）
                if (Instance != null && Instance.State == JumpState.Charging)
                    Instance.DoJump(Instance.GetPower());
                break;
            case "jump": // 直接以指定蓄力 0~1 起跳
                if (Instance != null && parts.Length > 1 && float.TryParse(parts[1], out float p))
                    Instance.GmJump(p);
                break;
            case "die": // 强制坠落（触发结果页）
                if (Instance != null) Instance.ForceDie();
                break;
            case "score":
                if (Instance != null && parts.Length > 1 && int.TryParse(parts[1], out int s))
                    Instance.GmSetScore(s);
                break;
            case "level": // 查询/设置关卡（设档即时生效：在局内则重开）
                if (parts.Length > 1 && int.TryParse(parts[1], out int lv))
                {
                    var ls = DataMgr.Instance.Load<JumpSaveData>(JumpSaveData.SaveKey, true) ?? new JumpSaveData();
                    ls.level = Mathf.Max(1, lv);
                    DataMgr.Instance.Save(JumpSaveData.SaveKey, ls, true);
                    if (Instance != null) Instance.Restart();
                    Debug.Log($"[JumpGM] 关卡设为 {ls.level}（{JumpConfig.GetLevelName(ls.level)}）目标 {JumpConfig.GetLevelTargetBlocks(ls.level)} 块");
                }
                else
                {
                    var ls2 = DataMgr.Instance.Load<JumpSaveData>(JumpSaveData.SaveKey, true);
                    Debug.Log(ls2 == null
                        ? $"[JumpGM] 无存档（默认 {JumpConfig.GetLevelName(1)}）"
                        : $"[JumpGM] 当前第 {ls2.level} 关（{JumpConfig.GetLevelName(ls2.level)}）目标 {JumpConfig.GetLevelTargetBlocks(ls2.level)} 块");
                }
                break;
            case "item": // 强制第 3 块刷指定物品（wheel|boost|coin|shard|shield|sprint|none），即时重开生效
                if (parts.Length > 1 && Enum.TryParse(parts[1], true, out JumpItemKind forced))
                {
                    if (Instance == null)
                    {
                        Debug.LogWarning("[JumpGM] 请先 jump start 开局再强制物品");
                        break;
                    }
                    Instance._gmItemKind = forced;
                    Instance.Restart();
                    Debug.Log($"[JumpGM] 第 3 块物品强制为 {forced}（{JumpConfig.GetItemName(forced)}）");
                }
                else Debug.LogWarning("[JumpGM] 用法: jump item <Wheel|ScoreBoost|CoinBag|Shard|Shield|Sprint|None>");
                break;
            case "trap": // 强制第 4 块刷指定陷阱（slippery|phantom|slowcharge|shake|burn|reverse|seal|none），即时重开生效
                if (parts.Length > 1 && Enum.TryParse(parts[1], true, out JumpTrapKind forcedTrap))
                {
                    if (Instance == null)
                    {
                        Debug.LogWarning("[JumpGM] 请先 jump start 开局再强制陷阱");
                        break;
                    }
                    Instance._gmTrapKind = forcedTrap;
                    Instance.Restart();
                    Debug.Log($"[JumpGM] 第 4 块陷阱强制为 {forcedTrap}（{JumpConfig.GetTrapName(forcedTrap)}）");
                }
                else Debug.LogWarning("[JumpGM] 用法: jump trap <Slippery|Phantom|SlowCharge|Shake|Burn|Reverse|Seal|None>");
                break;
            case "best":
                var save = DataMgr.Instance.Load<JumpSaveData>(JumpSaveData.SaveKey, true);
                Debug.Log(save == null
                    ? "[JumpGM] 无存档"
                    : $"[JumpGM] 最高 {save.best} · 局数 {save.playCount} · 累计 {save.totalScore}");
                break;
            case "home":
                ExitToHome().Forget();
                break;
            default:
                Debug.LogWarning($"[JumpGM] 未知子命令: {cmd}");
                break;
        }
    }

    internal void GmJump(float power)
    {
        if (State != JumpState.Ready) return;
        DoJump(Mathf.Clamp01(power));
    }

    internal void GmSetScore(int score)
    {
        Score = score;
        DispatchScore(0, false, _character != null ? _character.transform.position : Vector3.zero);
    }

    internal void ForceDie()
    {
        if (State != JumpState.Ready && State != JumpState.Charging) return;
        DieAsync(_character.transform.position, TravelAxis(_current.Index)).Forget();
    }

    // ---------- GM 指令：有限关卡（level） ----------

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void RegisterGmLevel()
    {
        DebugConsoleMgr.RegisterCommand("level",
            "level [info]|play <levelId>|setseq <n>|next|reset|pack（有限关卡调试）",
            OnGmLevelCommand);
    }

    private static void OnGmLevelCommand(string args)
    {
        string[] parts = (args ?? "").Split(' ');
        string cmd = parts.Length > 0 ? parts[0].ToLowerInvariant() : "";
        switch (cmd)
        {
            case "":
            case "info":
            {
                var sb = new System.Text.StringBuilder();
                sb.Append($"[LevelGM] 包状态 {LevelRuntime.Status}");
                if (LevelRuntime.HasFiniteLevels)
                {
                    sb.Append($" · v{LevelRuntime.Pack.Version} · {LevelRuntime.Pack.LevelCount} 关");
                    sb.Append($" · 当前第 {LevelRuntime.CurrentSeq} 关 {LevelRuntime.Pack.Levels[LevelRuntime.CurrentSeq - 1].Definition.LevelId}号");
                    sb.Append($" · 累计通关 {LevelRuntime.CumulativeClears}");
                    if (Instance != null && Instance._plan != null)
                        sb.Append($" · 本局 {Instance._plan.DisplayName}{(Instance._plan.IsPlaytest ? "（试玩草稿）" : "")}");
                }
                else if (LevelRuntime.Status == LevelRuntime.PackStatus.SchemaUnsupported)
                {
                    sb.Append(" · 关卡包结构版本超前：开局已被阻断，请更新游戏");
                }
                Debug.Log(sb.ToString());
                break;
            }
            case "play":
            {
                if (parts.Length < 2 || !int.TryParse(parts[1], out int id))
                {
                    Debug.LogWarning("[LevelGM] 用法: level play <levelId>");
                    break;
                }
                if (!LevelRuntime.HasFiniteLevels)
                {
                    Debug.LogWarning("[LevelGM] 无可用关卡包");
                    break;
                }
                int seq = -1;
                for (int i = 0; i < LevelRuntime.Pack.Levels.Count; i++)
                    if (LevelRuntime.Pack.Levels[i].Definition.LevelId == id) { seq = i + 1; break; }
                if (seq < 0)
                {
                    Debug.LogWarning($"[LevelGM] 包内没有关卡 {id}");
                    break;
                }
                LevelRuntime.SetSeq(seq);
                if (Instance != null) Instance.Restart();
                else JumpGame.StartGame();
                Debug.Log($"[LevelGM] 已切到关卡 {id}（第 {seq} 关）");
                break;
            }
            case "setseq":
            {
                if (parts.Length < 2 || !int.TryParse(parts[1], out int seq))
                {
                    Debug.LogWarning("[LevelGM] 用法: level setseq <n>");
                    break;
                }
                LevelRuntime.SetSeq(Mathf.Max(1, seq));
                if (Instance != null) Instance.Restart();
                Debug.Log($"[LevelGM] 顺序位设为 {Mathf.Max(1, seq)}");
                break;
            }
            case "next":
            {
                if (!LevelRuntime.HasFiniteLevels) break;
                LevelRuntime.SetSeq(LevelRuntime.CurrentSeq + 1);
                if (Instance != null) Instance.Restart();
                Debug.Log($"[LevelGM] 跳到第 {LevelRuntime.CurrentSeq} 关");
                break;
            }
            case "reset":
                LevelRuntime.ResetProgress();
                Debug.Log("[LevelGM] 有限关卡进度已清零（seq=1 · 累计通关清零 · 单关最高分清空）");
                break;
            case "pack":
            {
                if (!LevelRuntime.HasFiniteLevels)
                {
                    Debug.Log("[LevelGM] 无可用关卡包（Missing/SchemaUnsupported/Corrupt）");
                    break;
                }
                var sb2 = new System.Text.StringBuilder($"[LevelGM] 包 v{LevelRuntime.Pack.Version}：\n");
                for (int i = 0; i < LevelRuntime.Pack.Levels.Count; i++)
                {
                    var d = LevelRuntime.Pack.Levels[i].Definition;
                    sb2.AppendLine($"  第{i + 1,2}关 #{d.LevelId:D3} ch{d.ChapterId}-{d.OrderInChapter} " +
                        $"{DifficultyProfiles.Get(d.Difficulty).DisplayName} {d.GridCount}格 seed={d.Seed}" +
                        $"{(LevelRuntime.GetRecord(d.LevelId).Cleared ? " ✅已通" : "")}");
                }
                Debug.Log(sb2.ToString());
                break;
            }
            default:
                Debug.LogWarning($"[LevelGM] 未知子命令: {cmd}");
                break;
        }
    }
}
