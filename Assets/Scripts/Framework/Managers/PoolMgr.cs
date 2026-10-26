using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>可池化物体钩子（Spawn/Despawn 时机回调，可选实现）</summary>
public interface IPoolable
{
    void OnSpawn();
    void OnDespawn();
}

/// <summary>
/// 对象池：按 key 分组复用 GameObject，减少频繁 Instantiate/Destroy 带来的 GC
/// 与卡顿（WebGL GC 卡顿致命）。模板经 ResMgr 异步加载并常驻；
/// Despawn 仅隐藏归位并重置局部变换，不销毁。
/// 注意：外部直接 Destroy 的池对象会在下次 Spawn 时被识别为脏引用并跳过。
/// </summary>
public class PoolMgr : PureSingleton<PoolMgr>
{
    private class Pool
    {
        public GameObject Template;
        public Transform Root;
        public readonly Stack<GameObject> Inactive = new Stack<GameObject>();
        public int ActiveCount;
    }

    private Transform _root;
    private readonly Dictionary<string, Pool> _pools = new Dictionary<string, Pool>();

    protected override void OnInit()
    {
        var go = new GameObject("PoolRoot");
        Object.DontDestroyOnLoad(go);
        _root = go.transform;
    }

    /// <summary>注册池并预热（幂等；prefabResKey 如 "Prefabs/PoolCube"）</summary>
    public async UniTask RegisterPoolAsync(string poolKey, string prefabResKey, int warmup = 0)
    {
        if (_pools.ContainsKey(poolKey)) return;
        if (_root == null) Instance.Init(); // 兜底：未走 GameLauncher 也能用

        GameObject prefab = await ResMgr.Instance.LoadAssetAsync<GameObject>(prefabResKey);
        if (prefab == null)
        {
            Debug.LogError($"[PoolMgr] 模板加载失败: {prefabResKey}");
            return;
        }

        var holder = new GameObject("Pool_" + poolKey);
        holder.transform.SetParent(_root, false);
        var pool = new Pool { Template = prefab, Root = holder.transform };
        _pools[poolKey] = pool;

        for (int i = 0; i < warmup; i++)
        {
            var go = Object.Instantiate(prefab, pool.Root, false);
            go.SetActive(false);
            pool.Inactive.Push(go);
        }
        Debug.Log($"[PoolMgr] 池注册: {poolKey}（预热 {warmup}）");
    }

    /// <summary>从池中取出实例（无空闲则 Instantiate 新实例）</summary>
    public GameObject Spawn(string poolKey, Vector3? position = null, Quaternion? rotation = null)
    {
        if (!_pools.TryGetValue(poolKey, out var pool) || pool.Template == null)
        {
            Debug.LogError($"[PoolMgr] 未注册的池: {poolKey}");
            return null;
        }

        GameObject go = null;
        while (pool.Inactive.Count > 0)
        {
            var candidate = pool.Inactive.Pop();
            if (candidate != null) { go = candidate; break; }
            // 外部销毁的脏引用，跳过
        }
        if (go == null)
            go = Object.Instantiate(pool.Template, pool.Root, false);

        go.transform.SetPositionAndRotation(position ?? Vector3.zero, rotation ?? Quaternion.identity);
        go.SetActive(true);
        pool.ActiveCount++;
        go.GetComponent<IPoolable>()?.OnSpawn();
        return go;
    }

    /// <summary>回收入池（隐藏+归位+重置局部变换）</summary>
    public void Despawn(string poolKey, GameObject go)
    {
        if (go == null) return;
        if (!_pools.TryGetValue(poolKey, out var pool))
        {
            Object.Destroy(go); // 未注册池的对象无处回收
            return;
        }
        go.GetComponent<IPoolable>()?.OnDespawn();
        go.SetActive(false);
        go.transform.SetParent(pool.Root, false);
        go.transform.ResetLocal();
        pool.Inactive.Push(go);
        pool.ActiveCount--;
    }

    /// <summary>清空某池的空闲实例（使用中的实例回收时仍会被接受）</summary>
    public void ClearPool(string poolKey)
    {
        if (!_pools.TryGetValue(poolKey, out var pool)) return;
        while (pool.Inactive.Count > 0)
        {
            var go = pool.Inactive.Pop();
            if (go != null) Object.Destroy(go);
        }
        pool.ActiveCount = 0; // 使用中的实例由调用方自行处理
        Debug.Log($"[PoolMgr] 池已清空: {poolKey}");
    }

    public void ClearAll()
    {
        foreach (var kv in _pools) ClearPool(kv.Key);
    }

    public int GetActiveCount(string poolKey)
        => _pools.TryGetValue(poolKey, out var p) ? p.ActiveCount : -1;

    public int GetInactiveCount(string poolKey)
        => _pools.TryGetValue(poolKey, out var p) ? p.Inactive.Count : -1;

    /// <summary>池统计（DebugConsole mem 指令用）</summary>
    public string GetStats()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("== PoolMgr 池统计 ==");
        if (_pools.Count == 0) sb.AppendLine("  （无已注册池）");
        foreach (var kv in _pools)
            sb.AppendLine($"  {kv.Key}: 活跃 {kv.Value.ActiveCount} / 空闲 {kv.Value.Inactive.Count}");
        return sb.ToString();
    }
}
