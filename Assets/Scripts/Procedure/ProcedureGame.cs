using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 游戏主流程：最终常驻状态，进入后不再自动切换（后续子流程由业务自行扩展）。
/// 进入即后台闲时预热战斗组 AB（BundleMap.BattleGroup）：用户停留在主页期间把
/// 跳一跳所需包提前拉齐，点开始时全部缓存命中（微信 CDN/首包按需下载的关键路径）。
/// 编辑器模拟模式自动跳过；预热失败不阻塞（业务加载时自然重试）。
/// </summary>
public sealed class ProcedureGame : ProcedureBase
{
    public override void OnEnter(ProcedureMgr procedureManager)
    {
        Debug.Log("[Procedure] 进入 ProcedureGame");
        WarmBattleAsync().Forget();
    }

    public override void OnLeave(ProcedureMgr procedureManager)
    {
        Debug.Log("[Procedure] 离开 ProcedureGame");
    }

    private static async UniTaskVoid WarmBattleAsync()
    {
        await ResMgr.Instance.PreloadBundlesAsync(BundleMap.BattleGroup);
        Debug.Log("[ProcedureGame] 战斗组 AB 预热完成");
    }
}
