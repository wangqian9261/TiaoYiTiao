using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>跳一跳：统一功能格、手动跳跃、确定性关卡和通关连跳奖励。</summary>
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

    public LevelRunPlan Plan => _plan;
    public string CurrentLevelName => _plan != null ? _plan.DisplayName : JumpConfig.GetLevelName(_level);
    public int RunCoins => _runCoins;
    public TileKind CurrentTileKind => _current != null ? _current.Kind : TileKind.Normal;
    public int CurrentEffectLevelId => _current != null ? _current.EffectLevelId : 0;
    public float CollapseCountdown => _current != null && _current.Kind == TileKind.Collapse &&
        (State == JumpState.Ready || State == JumpState.Charging)
        ? Mathf.Max(0, _plan.Source.Effects.GetCollapse(_current.EffectLevelId).Seconds - _effectTimer.Elapsed) : -1f;
    public Vector3 EffectAnchor => _current != null ? _current.TopCenter : Vector3.zero;
    public float EffectDuration => CurrentTileKind == TileKind.Collapse
        ? _plan.Source.Effects.GetCollapse(_current.EffectLevelId).Seconds : 0f;
    public bool TimeFrozen => _appPaused || _userPaused;
    public bool UserPaused => _userPaused;
    public bool AppPaused => _appPaused;
    public int Combo => _combo;
    public int RunVersion => _presentationVersion;
    public bool IsRunActive(int version) => this != null && Instance == this
        && version == _presentationVersion && State != JumpState.Idle && State != JumpState.Over;
    public float ChargeElapsed => State == JumpState.Charging ? Mathf.Max(0f, Time.time - _chargeStart) : 0f;
    public float ChargePhase => Mathf.Repeat(ChargeElapsed / (JumpConfig.ChargeCycleSeconds), 1f);
    public float ChargePower => GetPower();

    /// <summary>The displayed sector uses exactly the distance/half-width used by EvaluateLanding.</summary>
    public bool TryGetChargeTarget(out Vector2 phaseRange)
    {
        phaseRange = Vector2.zero;
        if (_current == null || _next == null) return false;
        float distance = Vector3.Dot(_next.TopCenter - _character.transform.position, TravelAxis(_current.Index));
        float near = distance - _next.Size * .5f, far = distance + _next.Size * .5f;
        if (far < JumpConfig.MinJumpDist || near > JumpConfig.MaxJumpDist) return false;
        float lo = Mathf.InverseLerp(JumpConfig.MinJumpDist, JumpConfig.MaxJumpDist, near);
        float hi = Mathf.InverseLerp(JumpConfig.MinJumpDist, JumpConfig.MaxJumpDist, far);
        phaseRange = new Vector2(lo, hi);
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
    private bool _runTransitioning;
    private float _savedTimeScale = 1f;
    private Action _onWxShow;

    private void ApplyTimeFreeze()
    {
        if (TimeFrozen && !_ownsTimePause)
        {
            _savedTimeScale = Time.timeScale;
            _ownsTimePause = true;
            Time.timeScale = 0f;
        }
        else if (!TimeFrozen) ReleaseTimeFreeze();
    }

    private void ReleaseTimeFreeze()
    {
        if (!_ownsTimePause) return;
        Time.timeScale = _savedTimeScale;
        _ownsTimePause = false;
    }

    private async UniTask WaitForPresentationResumeAsync(int version)
    {
        while (this != null && Instance == this && version == _presentationVersion && TimeFrozen)
            await UniTask.Yield();
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
    private int _combo;
    private int _maxCombo;
    private int _perfectCount;
    private Color _lastBlockColor;
    private bool _poolReady;
    private bool _booted;

    private LevelRunPlan _plan;
    private LevelEconomyConfig _economy;
    private int _runCoins;
    private bool _appPaused, _settled;
    private readonly LevelTileEffectResolver _effectTimer = new LevelTileEffectResolver();

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
        if (this == null) return;
        _plan = await LevelRuntime.ResolvePlanForNewRunAsync();
        if (this == null) return;
        if (_plan?.IsPlaytest != true && (LevelRuntime.Status == LevelRuntime.PackStatus.SchemaUnsupported || LevelRuntime.Status == LevelRuntime.PackStatus.Corrupt))
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
            var plan = await LevelRuntime.ResolvePlanForNewRunAsync();
            if (this == null || Instance != this || version != _presentationVersion) return;
            _userPaused = false;
            ApplyTimeFreeze();
            if (plan?.IsPlaytest != true && (LevelRuntime.Status == LevelRuntime.PackStatus.SchemaUnsupported || LevelRuntime.Status == LevelRuntime.PackStatus.Corrupt))
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
        _swallowNextRelease = false;
        EverCharged = false;
        _runCoins = 0;
        _settled = false;
        _effectTimer.Reset();

        if (_plan != null)
        {
            // ---- 有限关卡：整关全量铺烘焙瓦片（0..终点；尺寸/位置/类型/等级按格烘焙）----
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

    private void SpawnBlockFromPlanTile(int i)
    {
        var block = CreateBlockObject(_plan.Position(i));
        block.Setup(i, _plan.Sizes[i], _plan.Kinds[i], _plan.EffectLevelIds[i], TravelAxis(i));
        if (_plan.Kinds[i] == TileKind.Sprint)
        {
            int steps = _plan.Source.Effects.GetSprint(_plan.EffectLevelIds[i]).Steps;
            var path = new Vector3[steps + 1];
            for (int j = 0; j <= steps; j++) path[j] = _plan.Position(i + j);
            block.ShowSprintPath(path);
        }
        _blocks.Add(block);
    }

    /// <summary>从 fromIndex 起跳的行进轴（有限关卡=计划烘焙轴；旧模式=之字交替）</summary>
    private Vector3 TravelAxis(int fromIndex)
        => _plan != null
            ? (_plan.TravelAxis(fromIndex) == TileAxis.X ? Vector3.right : Vector3.forward)
            : AxisOf(fromIndex);

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

        var block = CreateBlockObject(pos);
        block.Setup(index, size, goal ? TileKind.End : index == 0 ? TileKind.Start : TileKind.Normal, 0, AxisOf(index));
        _blocks.Add(block);
        return block;
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
        if (this == null || Instance != this || State != JumpState.Ready || TimeFrozen) return;
        if (InputMgr.Instance.RaycastUI(args.Position) != null) return; // 按在 UI（按钮/遮罩/控制台）上不蓄力
        BeginCharge();
    }

    private void BeginCharge()
    {
        if (State != JumpState.Ready || TimeFrozen) return;
        _swallowNextRelease = false;
        State = JumpState.Charging;
        _chargeStart = Time.time;
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
        if (args.Canceled || TimeFrozen || ChargeElapsed >= JumpConfig.ChargeCancelSeconds ||
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
        return p;
    }

    private void OnWxHide()
    {
        if (this == null || Instance != this) return;
        OnApplicationPause(true);
    }

    // ---------- 跳跃与判定 ----------

    private void DoJump(float power)
    {
        Vector3 axis = TravelAxis(_current.Index);
        Vector3 from = _character.transform.position;
        float dist = Mathf.Lerp(JumpConfig.MinJumpDist, JumpConfig.MaxJumpDist, power);
        Vector3 to = _current.TopCenter + axis * (Vector3.Dot(from - _current.TopCenter, axis) + dist);
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
            gain = Mathf.Min(1 << Mathf.Min(_combo, 5), JumpConfig.PerfectComboCap); // 2/4/8/16/32…
            JumpAudio.Play(JumpConfig.SfxPerfect, 1f);
            _fx.Stars(landPos);
            EventCenter.Dispatch(JumpEvents.Perfect, _combo);
        }
        else
        {
            _combo = 0;
            gain = JumpConfig.BaseLandScore;
        }
        Score += gain;

        if (Mathf.Abs(off) > half * JumpConfig.WobbleStartScale)
            _character.PlayWobble(); // 惊险边缘摇晃（仍存活）

        // 推进世界（整关已全量加载：只推进游标，不再生成）
        _current = block;
        _effectTimer.Reset();
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
        if (block.Kind == TileKind.Sprint)
            SprintAsync(_plan.Source.Effects.GetSprint(block.EffectLevelId)).Forget();
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
        _combo = 0;
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
        if (_settled) return;
        _settled = true;
        State = JumpState.Over;

        if (_plan != null)
        {
            var reward = _economy.Calculate(cleared, _plan.IsPlaytest, _plan.ClearCoinMult, _maxCombo);
            int coinsGained = reward.Total;
            _runCoins = coinsGained;
            if (coinsGained > 0) PlayerMgr.Instance.AddCoins(coinsGained);

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
                BaseCoins = reward.BaseCoins,
                ComboCoins = reward.ComboCoins,
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

    // ---------- 特殊格：自动蓄力、连续安全小跳 ----------
    private async UniTaskVoid SprintAsync(SprintLevel effect)
    {
        int version = _presentationVersion;
        int destination = _current.Index + effect.Steps;
        State = JumpState.Airborne;
        for (float t = 0; t < effect.ChargeSeconds; t += Time.deltaTime)
        {
            if (!IsRunActive(version) || State != JumpState.Airborne) return;
            _character.SetCharge(Mathf.Clamp01(t / effect.ChargeSeconds));
            await UniTask.Yield();
        }
        await WaitForPresentationResumeAsync(version);
        if (!IsRunActive(version)) return;
        _current.SetPathVisible(false);
        while (_current.Index < destination)
        {
            var target = FindBlock(_current.Index + 1);
            if (target == null || target.Kind != TileKind.Normal)
            { Debug.LogError("[JumpGame] 无效冲刺通道，停止本局"); FinishRun(); return; }
            Vector3 from = _character.transform.position;
            Vector3 to = target.TopCenter;
            _character.FaceAxis(TravelAxis(_current.Index));
            _character.PlayLaunch(effect.HopSeconds);
            JumpAudio.Play(JumpConfig.SfxFly, .65f);
            for (float t = 0; t < effect.HopSeconds; t += Time.deltaTime)
            {
                if (!IsRunActive(version) || State != JumpState.Airborne) return;
                float p = Mathf.Clamp01(t / effect.HopSeconds);
                _character.transform.position = Vector3.Lerp(from, to, p) + Vector3.up * (1.2f * 4 * p * (1 - p));
                await UniTask.Yield();
            }
            await WaitForPresentationResumeAsync(version);
            if (!IsRunActive(version) || State != JumpState.Airborne) return;
            _character.SnapTo(to);
            _character.PlayLand();
            _fx.Dust(to);
            JumpAudio.Play(JumpConfig.SfxLand, .8f);
            _current = target;
            _next = FindBlock(target.Index + 1);
            DispatchScore(0, false, to);
        }
        _effectTimer.Reset();
        _character.FaceAxis(TravelAxis(_current.Index));
        _swallowNextRelease = true;
        State = JumpState.Ready;
    }

    private void TickSpecialTile(float deltaTime)
    {
        if (_plan == null || _current == null || deltaTime <= 0) return;
        if (_current.Kind == TileKind.Ice)
        {
            var grade = _plan.Source.Effects.GetIce(_current.EffectLevelId);
            float slide = _effectTimer.AdvanceIce(grade, deltaTime);
            _character.transform.position += TravelAxis(_current.Index) * slide;
            Vector3 local = _character.transform.position - _current.TopCenter;
            if (LevelTileEffectResolver.Outside(local.x, local.z, _current.Size))
                DieAsync(_character.transform.position, TravelAxis(_current.Index)).Forget();
        }
        else if (_current.Kind == TileKind.Collapse)
        {
            var grade = _plan.Source.Effects.GetCollapse(_current.EffectLevelId);
            bool collapsed = _effectTimer.AdvanceCollapse(grade, deltaTime);
            _current.SetCollapseProgress(_effectTimer.Elapsed / grade.Seconds);
            if (collapsed)
            {
                _current.Collapse();
                DieAsync(_character.transform.position, TravelAxis(_current.Index)).Forget();
            }
        }
    }

    // ---------- 每帧驱动 ----------

    private void OnTick()
    {
        if (this == null || _character == null) return;
        if (TimeFrozen) return;
        float dt = Time.deltaTime;

        if (State == JumpState.Charging)
        {
            if (ChargeElapsed >= JumpConfig.ChargeCancelSeconds) CancelCharge();
            else _character.SetCharge(GetPower());
        }

        if (State == JumpState.Ready || State == JumpState.Charging)
            TickSpecialTile(dt);

        _fx.SlideTrail(_character.transform.position,
            CurrentTileKind == TileKind.Ice && _effectTimer.Elapsed > _plan.Source.Effects.GetIce(CurrentEffectLevelId).StartDelay
            && (State == JumpState.Ready || State == JumpState.Charging));

        UpdateCamera(dt);
    }

    /// <summary>切后台：冻结特殊格计时和自动跳跃（OnTick 检查 _appPaused）</summary>
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
    }

    // ---------- GM 指令 ----------

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void RegisterGm()
    {
        DebugConsoleMgr.RegisterCommand("jump",
            "jump start|hold|release|jump <p>|die|score <n>|level [<n>]|best|home（跳一跳调试）",
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
                else if ((LevelRuntime.Status == LevelRuntime.PackStatus.SchemaUnsupported || LevelRuntime.Status == LevelRuntime.PackStatus.Corrupt))
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
