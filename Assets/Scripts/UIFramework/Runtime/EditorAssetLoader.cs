#if UNITY_EDITOR
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 编辑器模拟加载器（Auto 模式）：内容根 Assets/GameRes 已迁出 Resources，
/// 编辑器内免构建 AB 直接开发——按与 BundleLoader 完全相同的业务 key 加载，
/// 差别仅在底层换 AssetDatabase 直读（微信 AB 构建与编辑器开发同构，业务代码零分叉）。
/// AssetDatabase 是编辑器主线程同步 API，包一层 UniTask 保持异步签名统一。
/// </summary>
public class EditorAssetLoader : IResLoader
{
    private Dictionary<string, string> _keyToPath;

    public async UniTask<T> LoadAsync<T>(string assetKey, CancellationToken token) where T : Object
    {
        if (token.IsCancellationRequested)
            return null;

        if (!TryResolvePath(assetKey, rebuildCache: false, out string path)
            && !TryResolvePath(assetKey, rebuildCache: true, out path))
        {
            Debug.LogError($"[EditorAssetLoader] 内容根 {BundleMap.ResRoot} 下未找到资产: {assetKey}");
            return null;
        }

        await UniTask.Yield();
        if (token.IsCancellationRequested)
            return null;
        return AssetDatabase.LoadAssetAtPath<T>(path);
    }

    /// <summary>key → 工程路径；miss 时重建一次缓存（兼容会话中途新导入资产）</summary>
    private bool TryResolvePath(string assetKey, bool rebuildCache, out string path)
    {
        if (rebuildCache || _keyToPath == null)
            _keyToPath = BuildCache();
        return _keyToPath.TryGetValue(assetKey, out path);
    }

    private static Dictionary<string, string> BuildCache()
    {
        var dict = new Dictionary<string, string>();
        string root = BundleMap.ResRoot;
        if (!AssetDatabase.IsValidFolder(root))
        {
            Debug.LogError($"[EditorAssetLoader] 内容根不存在: {root}（请执行菜单 UIFramework/4. Migrate Resources → GameRes）");
            return dict;
        }

        string prefix = root + "/";
        foreach (var guid in AssetDatabase.FindAssets("", new[] { root }))
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(assetPath) || assetPath.EndsWith(".meta"))
                continue;
            if (AssetDatabase.IsValidFolder(assetPath))
                continue;
            if (!assetPath.StartsWith(prefix))
                continue;

            // 相对 key = 去 "根/" 前缀 + 去扩展名（与 BundleMap.KeyToAssetAddress 同构）
            string relative = assetPath.Substring(prefix.Length);
            string key = System.IO.Path.ChangeExtension(relative, null);
            dict[key] = assetPath;
        }
        return dict;
    }

    public void OnAssetReleased(string assetKey, Object asset)
    {
        // 编辑器模拟：资产由 AssetDatabase 托管，无需按引用卸载
    }

    public void UnloadUnused()
    {
        Resources.UnloadUnusedAssets(); // 兜底回收模拟加载产生的托管引用
    }
}
#endif
