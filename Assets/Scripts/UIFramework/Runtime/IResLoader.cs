using System.Threading;
using UnityEngine;
using Cysharp.Threading.Tasks;

/// <summary>
/// 资源加载策略接口：ResMgr 按运行环境选择具体实现
/// （编辑器模拟加载 / 微信小游戏 AB 首包或CDN）。
/// </summary>
public interface IResLoader
{
    /// <summary>按业务key加载资产（如 "Prefabs/Panel_Home"、"Texture/Reward_Gold"）</summary>
    UniTask<T> LoadAsync<T>(string assetKey, CancellationToken token) where T : Object;

    /// <summary>某资产引用计数归零、移出缓存字典后回调；加载器可在此执行自身清理</summary>
    void OnAssetReleased(string assetKey, Object asset);

    /// <summary>策略性全量清理（场景切换等时机调用）</summary>
    void UnloadUnused();
}
