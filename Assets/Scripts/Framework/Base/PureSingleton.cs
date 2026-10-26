using System;

/// <summary>
/// 纯 C# 单例基类（非Mono）。适合 AudioMgr/PoolMgr/DataMgr 等无组件管理器。
/// MonoBehaviour 单例继续使用 UIFramework 的 Singleton&lt;T&gt;，两者命名并存不冲突。
/// </summary>
public abstract class PureSingleton<T> where T : PureSingleton<T>, new()
{
    private static T _instance;
    private static readonly object _lock = new object();
    private bool _inited;

    public static T Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    _instance ??= new T();
                }
            }
            return _instance;
        }
    }

    /// <summary>供 GameLauncher 统一初始化调用（幂等，只生效一次）</summary>
    public void Init()
    {
        if (_inited) return;
        _inited = true;
        OnInit();
    }

    /// <summary>子类初始化逻辑</summary>
    protected virtual void OnInit() { }
}
