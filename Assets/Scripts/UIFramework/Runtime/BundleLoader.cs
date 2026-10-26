using System.Collections.Generic;
using System.IO;
using System.Threading;
using UnityEngine;
using Cysharp.Threading.Tasks;

/// <summary>
/// AssetBundle 模式加载器（微信小游戏 CDN/首包优先方案）：
/// - AB 由菜单 UIFramework/3. Build AssetBundles 构建到 Assets/StreamingAssets/ab 下；
/// - 微信转换构建时，WX SDK 按 bundlePathIdentifier(StreamingAssets;) 自动把 AB 转为
///   data 文件：MiniGameConfig 配 assetLoadType=0 且填写 CDN 时，运行时从 {CDN}/Assets/...
///   远程加载（SDK 自带磁盘缓存，bundleHashLength 控制hash缓存键），AB 不占小游戏首包；
///   assetLoadType=1 时打进 data-package 分包（wasm+data 总限 ~29MB，超限 SDK 自动降级 CDN）。
/// - 编辑器内直接从本地 StreamingAssets 读取，与真机代码路径完全一致。
/// 分包映射：key→包名/地址 统一走 BundleMap（ABBuilder 构建侧同源，改映射只动 BundleMap 一处）。
/// </summary>
public class BundleLoader : IResLoader
{
    /// <summary>
    /// 编辑器回归测试钩子：覆盖AB查找目录（如指向构建到项目外临时目录的
    /// Standalone目标AB）。设置后需重新加载资产才生效；仅测试用途。
    /// </summary>
    public static string BaseDirOverride;

    private AssetBundleManifest _manifest;
    private readonly Dictionary<string, AssetBundle> _bundleCache = new Dictionary<string, AssetBundle>();

    // 包级在途表：后台预热与业务加载并发命中同一包时，只发起一次 LoadFromFileAsync
    //（否则重复载入同一 AB 会占双份内存）。
    private readonly Dictionary<string, UniTaskCompletionSource<AssetBundle>> _inflightBundles
        = new Dictionary<string, UniTaskCompletionSource<AssetBundle>>();

    /// <summary>冷加载计数（真正发起 LoadFromFileAsync 的次数），测试去重断言用</summary>
    public int ColdLoadCount { get; private set; }

    /// <summary>已加载包清单（GM/调试用）</summary>
    public IReadOnlyDictionary<string, AssetBundle> LoadedBundles => _bundleCache;

    private string BaseDir => BaseDirOverride != null
        ? BaseDirOverride
        : Path.Combine(Application.streamingAssetsPath, "ab");

    // 清单AB名 = 输出目录名（BuildPipeline 以目录名生成 AssetBundleManifest 所在的AB）
    private string ManifestBundleName =>
        Path.GetFileName(BaseDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

    public async UniTask<T> LoadAsync<T>(string assetKey, CancellationToken token) where T : Object
    {
        if (token.IsCancellationRequested)
            return null;
        try
        {
            AssetBundle bundle = await LoadBundleWithDepsAsync(BundleMap.KeyToBundleName(assetKey), token);
            if (bundle == null)
                return null;

            var req = bundle.LoadAssetAsync<T>(BundleMap.KeyToAssetAddress(assetKey));
            var asset = await req.WithCancellation(token);
            if (asset == null)
                Debug.LogError($"[BundleLoader] AB内未找到资产: {BundleMap.KeyToBundleName(assetKey)}/{BundleMap.KeyToAssetAddress(assetKey)}");
            return asset as T;
        }
        catch (System.OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>预热单个 AB：加载包文件与全部依赖（不加载具体资产）；已载包命中缓存直接返回。PreloadBundlesAsync/Procedure 流程与 GM 使用。</summary>
    public async UniTask<AssetBundle> PreloadBundleAsync(string bundleName, CancellationToken token = default)
    {
        return await LoadBundleWithDepsAsync(bundleName, token);
    }

    public void OnAssetReleased(string assetKey, Object asset)
    {
        // AB 模式下资产随 Bundle 生命周期管理，单个资产释放交由 UnloadUnused 兜底
    }

    public void UnloadUnused()
    {
        foreach (var kv in _bundleCache)
        {
            if (kv.Value != null)
                kv.Value.Unload(false);
        }
        _bundleCache.Clear();
        _inflightBundles.Clear();
        _manifest = null;
        Resources.UnloadUnusedAssets();
        System.GC.Collect();
    }

    private async UniTask<AssetBundle> LoadBundleWithDepsAsync(string bundleName, CancellationToken token)
    {
        if (_manifest == null)
        {
            AssetBundle manifestBundle = await GetOrLoadBundleFileAsync(ManifestBundleName, token);
            if (manifestBundle == null)
            {
                Debug.LogError($"[BundleLoader] 清单AB加载失败: {Path.Combine(BaseDir, ManifestBundleName)}，请先执行菜单 UIFramework/3. Build AssetBundles");
                return null;
            }

            var manifestReq = manifestBundle.LoadAssetAsync<AssetBundleManifest>("AssetBundleManifest");
            await manifestReq.WithCancellation(token);
            _manifest = manifestReq.asset as AssetBundleManifest;
            if (_manifest == null)
            {
                Debug.LogError("[BundleLoader] 清单AB中缺少 AssetBundleManifest 资产");
                return null;
            }
        }

        foreach (var dep in _manifest.GetAllDependencies(bundleName))
        {
            if (await GetOrLoadBundleFileAsync(dep, token) == null)
                return null;
        }
        return await GetOrLoadBundleFileAsync(bundleName, token);
    }

    private async UniTask<AssetBundle> GetOrLoadBundleFileAsync(string bundleName, CancellationToken token)
    {
        if (_bundleCache.TryGetValue(bundleName, out var cached) && cached != null)
            return cached;

        // 同包在途：并发请求共享同一次加载（PreloadBundleAsync 与业务 LoadAsync 竞态安全）
        if (_inflightBundles.TryGetValue(bundleName, out var inflight))
            return await inflight.Task;

        var tcs = new UniTaskCompletionSource<AssetBundle>();
        _inflightBundles.Add(bundleName, tcs);
        try
        {
            ColdLoadCount++;
            string path = Path.Combine(BaseDir, bundleName);
            var req = AssetBundle.LoadFromFileAsync(path);
            await req.WithCancellation(token);
            if (req.assetBundle == null)
            {
                Debug.LogError($"[BundleLoader] AB加载失败: {path}（AB需与当前激活构建平台一致；请先执行菜单 UIFramework/3. Build AssetBundles）");
                tcs.TrySetResult(null);
                return null;
            }
            _bundleCache[bundleName] = req.assetBundle;
            tcs.TrySetResult(req.assetBundle);
            return req.assetBundle;
        }
        catch (System.OperationCanceledException)
        {
            tcs.TrySetResult(null);
            return null;
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            tcs.TrySetResult(null);
            return null;
        }
        finally
        {
            _inflightBundles.Remove(bundleName);
        }
    }
}
