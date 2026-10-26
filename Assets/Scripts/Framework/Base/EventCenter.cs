using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 全局事件总线：订阅/派发/取消，解耦 UI、业务逻辑与平台回调（替代 SendMessage 与直接引用）。
/// 规则：
/// - Dispatch(key) 只通知无参 Action 订阅者；
/// - Dispatch(key, arg) 通知同类型 Action&lt;T&gt; 订阅者，并一并通知无参 Action 订阅者；
/// - 每个订阅者异常互相隔离，单个出错不影响其他订阅者；
/// - 重复添加同一委托自动去重；移除后不再收到派发。
/// </summary>
public static class EventCenter
{
    private static readonly Dictionary<string, List<Delegate>> _handlers = new Dictionary<string, List<Delegate>>();
    private static readonly object _lock = new object();

    public static void AddListener(string key, Action handler) => Add(key, handler);
    public static void AddListener<T>(string key, Action<T> handler) => Add(key, handler);
    public static void RemoveListener(string key, Action handler) => Remove(key, handler);
    public static void RemoveListener<T>(string key, Action<T> handler) => Remove(key, handler);

    public static void Dispatch(string key)
    {
        Delegate[] snapshot = GetSnapshot(key);
        if (snapshot == null) return;
        for (int i = snapshot.Length - 1; i >= 0; i--)
        {
            if (snapshot[i] is Action a)
                SafeInvoke(a);
        }
    }

    public static void Dispatch<T>(string key, T arg)
    {
        Delegate[] snapshot = GetSnapshot(key);
        if (snapshot == null) return;
        for (int i = snapshot.Length - 1; i >= 0; i--)
        {
            if (snapshot[i] is Action<T> typed)
                SafeInvoke(() => typed(arg));
            else if (snapshot[i] is Action plain)
                SafeInvoke(plain);
        }
    }

    /// <summary>当前某事件的订阅数（调试用）</summary>
    public static int GetListenerCount(string key)
    {
        lock (_lock)
        {
            return _handlers.TryGetValue(key, out var l) ? l.Count : 0;
        }
    }

    /// <summary>清空全部事件（慎用，仅场景彻底重置时）</summary>
    public static void ClearAll()
    {
        lock (_lock)
        {
            _handlers.Clear();
        }
    }

    private static void Add(string key, Delegate handler)
    {
        if (handler == null) return;
        lock (_lock)
        {
            if (!_handlers.TryGetValue(key, out var list))
            {
                list = new List<Delegate>();
                _handlers[key] = list;
            }
            if (!list.Contains(handler))
                list.Add(handler);
        }
    }

    private static void Remove(string key, Delegate handler)
    {
        if (handler == null) return;
        lock (_lock)
        {
            if (_handlers.TryGetValue(key, out var list))
            {
                list.Remove(handler);
                if (list.Count == 0)
                    _handlers.Remove(key);
            }
        }
    }

    private static Delegate[] GetSnapshot(string key)
    {
        lock (_lock)
        {
            return _handlers.TryGetValue(key, out var l) && l.Count > 0 ? l.ToArray() : null;
        }
    }

    private static void SafeInvoke(Action action)
    {
        try
        {
            action();
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
    }
}

/// <summary>框架级事件Key约定（业务模块请建自己的常量类扩展）</summary>
public static class EventKeys
{
    // 输入
    public const string FirstTouch = "Input.FirstTouch";
    public const string Tap = "Input.Tap";
    public const string DragStart = "Input.DragStart";
    public const string Dragging = "Input.Dragging";
    public const string DragEnd = "Input.DragEnd";

    // 微信生命周期与平台
    public const string WXShow = "WeChat.OnShow";
    public const string WXHide = "WeChat.OnHide";

    // UI
    /// <summary>string：全局飘字文本（FloatTextMgr 常驻顶层飘字UI显示）</summary>
    public const string FloatText = "UI.FloatText";
}
