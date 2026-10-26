using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using Cysharp.Threading.Tasks;

public class AssetLoader : MonoBehaviour
{
    // key -> 本对象持有的引用次数；与 ResMgr 的引用计数一一对应。
    // 修复原设计 HashSet 去重导致的计数失衡：同一 key 加载 N 次只释放 1 次 → RefCount 永不归零 → 资源泄漏。
    private Dictionary<string, int> _loadedAssetCounts = new Dictionary<string, int>();
    protected CancellationTokenSource _cts;

    protected virtual void Awake()
    {
        _cts = new CancellationTokenSource();
    }

    public virtual async UniTask<T> LoadAsset<T>(string assetKey) where T : Object
    {
        if (_cts.IsCancellationRequested)
            return null;

        T asset = await ResMgr.Instance.LoadAssetAsync<T>(assetKey, _cts.Token);

        if (_cts.IsCancellationRequested || asset == null)
        {
            if (asset != null)
            {
                ResMgr.Instance.ReleaseAsset(assetKey);
            }
            return null;
        }

        _loadedAssetCounts.TryGetValue(assetKey, out int count);
        _loadedAssetCounts[assetKey] = count + 1;
        return asset;
    }

    public virtual async UniTask LoadAssetsBatch(params string[] assetKeys)
    {
        foreach (var key in assetKeys)
        {
            await LoadAsset<Object>(key);
        }
    }

    /// <summary>
    /// 记录由外部（UIMgr 加载的面板prefab）替本对象持有的资源，纳入"谁加载谁记录"的统一释放管理。
    /// 修复原设计 UIMgr 直接访问 protected 字段导致的编译错误。
    /// </summary>
    public void TrackAsset(string assetKey)
    {
        _loadedAssetCounts.TryGetValue(assetKey, out int count);
        _loadedAssetCounts[assetKey] = count + 1;
    }

    public virtual void UnloadAllAssets()
    {
        foreach (var kv in _loadedAssetCounts)
        {
            for (int i = 0; i < kv.Value; i++)
            {
                ResMgr.Instance.ReleaseAsset(kv.Key);
            }
        }
        _loadedAssetCounts.Clear();
    }

    public virtual void UnloadSingleAsset(string assetKey)
    {
        if (_loadedAssetCounts.TryGetValue(assetKey, out int count))
        {
            ResMgr.Instance.ReleaseAsset(assetKey);
            if (count <= 1)
                _loadedAssetCounts.Remove(assetKey);
            else
                _loadedAssetCounts[assetKey] = count - 1;
        }
    }

    /// <summary>获取当前loader加载的资源列表，用于内存日志</summary>
    public IEnumerable<string> GetLoadedAssetKeys()
    {
        return _loadedAssetCounts.Keys;
    }

    protected virtual void OnDestroy()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;

        // 非播放期（编辑器工具销毁内存对象、退出Play的析构期）跳过释放：
        // 此时 ResMgr 可能已销毁或不存在，访问 Instance 反而误建空管理器
        if (!Application.isPlaying)
            return;
        UnloadAllAssets();
    }
}
