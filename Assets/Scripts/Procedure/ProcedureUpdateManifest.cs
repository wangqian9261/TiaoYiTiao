using UnityEngine;

/// <summary>
/// 更新资源清单流程（空流程演示）：进入即记录日志，每帧推进一步。
/// 真实业务接入点：下载/比对 Manifest、热更资源，完成后再切换；本轮不做真实下载。
/// </summary>
public sealed class ProcedureUpdateManifest : ProcedureBase
{
    public override void OnEnter(ProcedureMgr procedureManager)
    {
        Debug.Log("[Procedure] 进入 ProcedureUpdateManifest");
    }

    public override void OnUpdate(ProcedureMgr procedureManager, float deltaTime)
    {
        procedureManager.ChangeProcedure<ProcedurePreload>();
    }

    public override void OnLeave(ProcedureMgr procedureManager)
    {
        Debug.Log("[Procedure] 离开 ProcedureUpdateManifest");
    }
}
