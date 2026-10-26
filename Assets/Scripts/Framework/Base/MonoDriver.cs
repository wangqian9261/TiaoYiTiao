using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// 全局 Mono 驱动：统一 Update/FixedUpdate/LateUpdate 订阅、协程与延时/间隔定时。
/// 场景中仅此一个驱动物体（自动 DontDestroyOnLoad），便于 WebGL/微信生命周期管控，
/// 订阅者异常互相隔离。
/// </summary>
public class MonoDriver : Singleton<MonoDriver>
{
    /// <summary>每帧 Update 触发（订阅处勿做重活）</summary>
    public event Action UpdateTick;
    public event Action FixedUpdateTick;
    public event Action LateUpdateTick;

    private void Update() => Raise(UpdateTick);
    private void FixedUpdate() => Raise(FixedUpdateTick);
    private void LateUpdate() => Raise(LateUpdateTick);

    private static void Raise(Action handlers)
    {
        if (handlers == null) return;
        foreach (Action h in handlers.GetInvocationList())
        {
            try
            {
                h();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }
    }

    /// <summary>宿主协程：所有定时/异步流程集中在此物体上</summary>
    public Coroutine RunCoroutine(IEnumerator routine) => StartCoroutine(routine);

    /// <summary>取消 Delay/Interval/RunCoroutine 返回的句柄</summary>
    public void CancelTimer(Coroutine handle)
    {
        if (handle != null) StopCoroutine(handle);
    }

    /// <summary>下一帧执行</summary>
    public Coroutine NextFrame(Action cb) => StartCoroutine(NextFrameRoutine(cb));

    /// <summary>延时执行（默认受 timeScale 影响；unscaled=true 用真实时间）</summary>
    public Coroutine Delay(float seconds, Action cb, bool unscaled = false)
        => StartCoroutine(DelayRoutine(seconds, cb, unscaled));

    /// <summary>间隔循环执行；返回句柄用 CancelTimer 停止</summary>
    public Coroutine Interval(float seconds, Action cb, bool unscaled = false)
        => StartCoroutine(IntervalRoutine(seconds, cb, unscaled));

    private IEnumerator NextFrameRoutine(Action cb)
    {
        yield return null;
        cb?.Invoke();
    }

    private IEnumerator DelayRoutine(float seconds, Action cb, bool unscaled)
    {
        if (unscaled) yield return new WaitForSecondsRealtime(seconds);
        else yield return new WaitForSeconds(seconds);
        cb?.Invoke();
    }

    private IEnumerator IntervalRoutine(float seconds, Action cb, bool unscaled)
    {
        if (unscaled)
        {
            var waitReal = new WaitForSecondsRealtime(seconds);
            while (true) { yield return waitReal; cb?.Invoke(); }
        }
        else
        {
            var wait = new WaitForSeconds(seconds);
            while (true) { yield return wait; cb?.Invoke(); }
        }
    }
}
