using UnityEngine;

/// <summary>
/// 校验版本流程（空流程演示）：进入即记录日志，每帧推进一步。
/// 真实业务接入点：向服务器请求版本号并比对，完成后再切换；本轮不做真实联网。
/// </summary>
public sealed class ProcedureCheckVersion : ProcedureBase
{
    public override void OnEnter(ProcedureMgr procedureManager)
    {
        Debug.Log("[Procedure] 进入 ProcedureCheckVersion");
    }

    public override void OnUpdate(ProcedureMgr procedureManager, float deltaTime)
    {
        procedureManager.ChangeProcedure<ProcedureUpdateManifest>();
    }

    public override void OnLeave(ProcedureMgr procedureManager)
    {
        Debug.Log("[Procedure] 离开 ProcedureCheckVersion");
    }
}
