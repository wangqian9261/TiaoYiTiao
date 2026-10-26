using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 轻量流程状态机管理器（仿 UGF Procedure）：显式注册、显式切换，不使用反射。
/// 固定流程：Launch → CheckVersion → UpdateManifest → Preload → Login → Game。
/// 规则：
/// - ChangeProcedure 只登记目标状态，实际切换延迟到本帧 OnUpdate 结束后执行，每帧最多切换一次；
/// - 切换顺序固定：旧状态 OnLeave → 更新当前状态引用 → 新状态 OnEnter → 派发 ProcedureEvents.Changed；
/// - 重复注册、切换到未注册状态、初始化前切换、待切换请求未消费时重复请求，均抛 InvalidOperationException；
/// - Dispose 离开当前状态并清空全部状态与待切换请求。
/// 初始化：GameLauncher 末尾调用 ProcedureMgr.Instance.Init()（PureSingleton 幂等）；
/// 每帧推进由 MonoDriver.UpdateTick 驱动（GameLauncher 仅存在于启动场景，不能挂其 Update）。
/// </summary>
public class ProcedureMgr : PureSingleton<ProcedureMgr>
{
    private readonly Dictionary<Type, ProcedureBase> _procedures = new Dictionary<Type, ProcedureBase>();
    private MonoDriver _driver;
    private ProcedureBase _currentProcedure;
    private ProcedureBase _nextProcedure;
    private bool _initialized;

    /// <summary>当前流程状态（未初始化时为 null）</summary>
    public ProcedureBase CurrentProcedure => _currentProcedure;

    /// <summary>当前流程状态类名（未初始化时为空串）</summary>
    public string CurrentProcedureName =>
        _currentProcedure != null ? _currentProcedure.GetType().Name : string.Empty;

    /// <summary>当前是否处于指定流程状态</summary>
    public bool IsCurrent<T>() where T : ProcedureBase => _currentProcedure is T;

    /// <summary>
    /// 登记下一个流程状态：仅登记，延迟到本帧 OnUpdate 结束后切换（每帧最多一次）。
    /// 未初始化、上次请求未消费、目标未注册时抛 InvalidOperationException。
    /// </summary>
    public void ChangeProcedure<T>() where T : ProcedureBase
    {
        if (!_initialized)
            throw new InvalidOperationException("[ProcedureMgr] 尚未初始化，禁止切换流程状态");
        if (_nextProcedure != null)
            throw new InvalidOperationException(
                $"[ProcedureMgr] 待切换请求尚未消费（{_nextProcedure.GetType().Name}），禁止重复请求切换");
        if (!_procedures.TryGetValue(typeof(T), out ProcedureBase next))
            throw new InvalidOperationException($"[ProcedureMgr] 流程状态未注册: {typeof(T).Name}");
        _nextProcedure = next;
    }

    /// <summary>推进一帧：先 OnUpdate 当前状态，再消费待切换请求（由 MonoDriver.UpdateTick 驱动）</summary>
    public void Tick(float deltaTime)
    {
        if (!_initialized)
            throw new InvalidOperationException("[ProcedureMgr] 尚未初始化，禁止 Tick");
        _currentProcedure?.OnUpdate(this, deltaTime);
        if (_nextProcedure != null)
        {
            ProcedureBase next = _nextProcedure;
            _nextProcedure = null; // 先清空，OnLeave/OnEnter 内的再次登记留到下一帧生效
            SwitchTo(next);
        }
    }

    /// <summary>离开当前状态并清空全部状态与待切换请求（Application.quitting 时自动调用）</summary>
    public void Dispose()
    {
        if (!_initialized) return;
        _initialized = false;
        Application.quitting -= OnApplicationQuitting;
        if (_driver != null) _driver.UpdateTick -= OnUpdateTick;
        _driver = null;
        _currentProcedure?.OnLeave(this);
        _currentProcedure = null;
        _nextProcedure = null;
        _procedures.Clear();
    }

    protected override void OnInit()
    {
        // 显式注册固定流程（不使用反射），新增状态在此续加
        Register(new ProcedureLaunch());
        Register(new ProcedureCheckVersion());
        Register(new ProcedureUpdateManifest());
        Register(new ProcedurePreload());
        Register(new ProcedureLogin());
        Register(new ProcedureGame());
        _initialized = true;

        // 初始化时立即进入 ProcedureLaunch，并同样派发首个状态事件
        _currentProcedure = _procedures[typeof(ProcedureLaunch)];
        _currentProcedure.OnEnter(this);
        EventCenter.Dispatch(ProcedureEvents.Changed, CurrentProcedureName);

        // 每帧推进由 MonoDriver 驱动；退出应用时离开当前状态
        _driver = MonoDriver.Instance;
        _driver.UpdateTick += OnUpdateTick;
        Application.quitting += OnApplicationQuitting;
    }

    private void Register(ProcedureBase procedure)
    {
        Type type = procedure.GetType();
        if (_procedures.ContainsKey(type))
            throw new InvalidOperationException($"[ProcedureMgr] 流程状态重复注册: {type.Name}");
        _procedures.Add(type, procedure);
    }

    // 切换顺序固定：旧状态 OnLeave → 更新当前状态引用 → 新状态 OnEnter → 派发 ProcedureEvents.Changed
    private void SwitchTo(ProcedureBase next)
    {
        _currentProcedure?.OnLeave(this);
        _currentProcedure = next;
        next.OnEnter(this);
        EventCenter.Dispatch(ProcedureEvents.Changed, CurrentProcedureName);
    }

    private void OnUpdateTick()
    {
        Tick(Time.unscaledDeltaTime);
    }

    private void OnApplicationQuitting()
    {
        Dispose();
    }
}
