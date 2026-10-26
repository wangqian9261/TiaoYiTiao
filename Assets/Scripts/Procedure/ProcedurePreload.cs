using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 预加载流程：预热首屏组 AB 包（BundleMap.BootGroup——加载页/主页/配置/首屏BGM），
/// 完成后切 Login。编辑器模拟模式为即时 no-op；真机 AB 模式把包文件提前载入内存，
/// 后续 OpenPanel/PlayBgm 首帧即缓存命中。加载页进度段（0.6→0.8）由 ProcedureLoadingDriver
/// 的固定状态映射自然覆盖。超时守卫 30s：包内模式本地文件不会触发，为未来 CDN 弱网留口
/// （超时放行并记错误，缺包资产由各业务自身的兜底路径处理）。
/// </summary>
public sealed class ProcedurePreload : ProcedureBase
{
    private const float TimeoutSeconds = 30f;

    private bool _done;
    private float _elapsed;

    public override void OnEnter(ProcedureMgr procedureManager)
    {
        Debug.Log("[Procedure] 进入 ProcedurePreload");
        _done = false;
        _elapsed = 0f;
        WarmBootAsync().Forget();
    }

    public override void OnUpdate(ProcedureMgr procedureManager, float deltaTime)
    {
        _elapsed += deltaTime;
        if (!_done && _elapsed >= TimeoutSeconds)
        {
            Debug.LogError($"[ProcedurePreload] 首屏包预热超时 {TimeoutSeconds:F0}s，放行（缺包资产走业务兜底）");
            _done = true; // 超时放行，避免流程卡死
        }
        if (_done)
            procedureManager.ChangeProcedure<ProcedureLogin>();
    }

    public override void OnLeave(ProcedureMgr procedureManager)
    {
        Debug.Log("[Procedure] 离开 ProcedurePreload");
    }

    private async UniTaskVoid WarmBootAsync()
    {
        await ResMgr.Instance.PreloadBundlesAsync(BundleMap.BootGroup);
        _done = true;
        Debug.Log("[ProcedurePreload] 首屏组 AB 预热完成");
    }
}
