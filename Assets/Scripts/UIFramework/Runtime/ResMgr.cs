using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using Cysharp.Threading.Tasks;

/// <summary>资源加载模式：Auto=按平台自动选择（编辑器模拟 / 真机AB），可显式指定用于测试</summary>
public enum ResLoadMode
{
    Auto,
    /// <summary>已废弃：内容根迁出 Resources 后无 Resources API 可用，归一到 Auto 行为（仅保留枚举兼容）</summary>
    ResourcesOnly,
    BundleOnly
}

public class ResMgr : Singleton<ResMgr>, IInit
{
    private Dictionary<string, AssetItem> _assetDict = new Dictionary<string, AssetItem>();

    // 在途加载表：同 key 并发请求复用同一次加载。
    // 修复原设计并发双载后 _assetDict.Add 重复键抛 ArgumentException 的崩溃。
    private Dictionary<string, UniTaskCompletionSource<AssetItem>> _inflightLoads
        = new Dictionary<string, UniTaskCompletionSource<AssetItem>>();

#if UNITY_EDITOR
    private readonly EditorAssetLoader _editorAssetLoader = new EditorAssetLoader();
    private bool _warnedResourcesOnly;
#endif
    private readonly BundleLoader _bundleLoader = new BundleLoader();

    /// <summary>加载模式，运行时可切换；已缓存资产仍按其原始加载器正确释放</summary>
    public ResLoadMode LoadMode = ResLoadMode.Auto;

    /// <summary>当前 AB 加载器（GM/调试与包预热判断用；非 AB 模式下功能受限）</summary>
    public BundleLoader BundleLoader => _bundleLoader;

    public void OnInit()
    {
    }

    public async UniTask<T> LoadAssetAsync<T>(string assetKey, CancellationToken token = default) where T : Object
    {
        // 命中缓存：引用计数+1直接返回
        if (_assetDict.TryGetValue(assetKey, out AssetItem item))
        {
            item.RefCount++;
            return item.Asset as T;
        }

        // 已有同 key 加载在途：等待同一次加载完成后计引用，不重复发起加载
        if (_inflightLoads.TryGetValue(assetKey, out var inflight))
        {
            AssetItem sharedItem = await inflight.Task;
            if (token.IsCancellationRequested || sharedItem == null)
                return null;
            sharedItem.RefCount++;
            return sharedItem.Asset as T;
        }

        var tcs = new UniTaskCompletionSource<AssetItem>();
        _inflightLoads.Add(assetKey, tcs);
        try
        {
            IResLoader loader = ResolveLoader();
            T asset = await loader.LoadAsync<T>(assetKey, token);
            if (token.IsCancellationRequested || asset == null)
            {
                tcs.TrySetResult(null);
                return null;
            }

            AssetItem newItem = new AssetItem(asset, loader);
            _assetDict.Add(assetKey, newItem);
            tcs.TrySetResult(newItem);
            return asset;
        }
        catch (System.Exception e)
        {
            tcs.TrySetResult(null);
            Debug.LogException(e);
            return null;
        }
        finally
        {
            _inflightLoads.Remove(assetKey);
        }
    }

    public void ReleaseAsset(string assetKey)
    {
        if (!_assetDict.TryGetValue(assetKey, out AssetItem item))
            return;

        item.RefCount--;
        if (item.RefCount <= 0)
        {
            _assetDict.Remove(assetKey);
            // 释放回调交给当初加载它的那个加载器（模式切换后也不会串）
            item.Source.OnAssetReleased(assetKey, item.Asset);
        }
    }

    public void UnloadAllUnused()
    {
#if UNITY_EDITOR
        _editorAssetLoader.UnloadUnused();
#endif
        _bundleLoader.UnloadUnused();
    }

    /// <summary>
    /// 按 AB 包名批量预热（只加载包文件与依赖，不加载具体资产；已载包命中缓存）。
    /// 当前策略非 AB 模式（编辑器模拟）时直接跳过完成。
    /// 进度 = 已完成包数/总包数，单调不减。包名见 BundleMap.BootGroup/BattleGroup。
    /// </summary>
    public async UniTask PreloadBundlesAsync(IReadOnlyList<string> bundleNames,
        System.IProgress<float> progress = null, CancellationToken token = default)
    {
        if (ResolveLoader() != _bundleLoader)
        {
            Debug.Log("[ResMgr] 当前策略非 AB 模式，跳过包预热（编辑器模拟模式无 AB 概念）");
            return;
        }
        if (bundleNames == null || bundleNames.Count == 0)
            return;

        int total = bundleNames.Count;
        int done = 0;
        foreach (string name in bundleNames)
        {
            await _bundleLoader.PreloadBundleAsync(name, token);
            done++;
            progress?.Report((float)done / total);
        }
    }

    private IResLoader ResolveLoader()
    {
        switch (LoadMode)
        {
            case ResLoadMode.BundleOnly:
                return _bundleLoader;
            default: // Auto / ResourcesOnly（ResourcesOnly 已废弃，归一到 Auto 行为）
#if UNITY_EDITOR
                if (LoadMode == ResLoadMode.ResourcesOnly && !_warnedResourcesOnly)
                {
                    _warnedResourcesOnly = true;
                    Debug.LogWarning("[ResMgr] ResourcesOnly 已随内容根迁移（Assets/GameRes）废弃，按编辑器模拟加载处理");
                }
                return _editorAssetLoader; // Auto：编辑器模拟直读，便于未构建AB时开发
#else
                return _bundleLoader;    // Auto：真机（微信小游戏）走 AB，首包/CDN 由 SDK 配置决定
#endif
        }
    }
}

public class AssetItem
{
    public Object Asset;
    public int RefCount;
    public IResLoader Source;
    public AssetItem(Object asset, IResLoader source)
    {
        Asset = asset;
        RefCount = 1;
        Source = source;
    }
}
