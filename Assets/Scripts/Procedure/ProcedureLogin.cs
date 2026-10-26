using UnityEngine;

/// <summary>
/// 登录流程（空流程演示）：进入即记录日志，每帧推进一步。
/// 真实业务接入点：账号登录/鉴权，成功后再进入游戏；本轮不做真实登录。
/// </summary>
public sealed class ProcedureLogin : ProcedureBase
{
    public override void OnEnter(ProcedureMgr procedureManager)
    {
        Debug.Log("[Procedure] 进入 ProcedureLogin");
    }

    public override void OnUpdate(ProcedureMgr procedureManager, float deltaTime)
    {
        procedureManager.ChangeProcedure<ProcedureGame>();
    }

    public override void OnLeave(ProcedureMgr procedureManager)
    {
        Debug.Log("[Procedure] 离开 ProcedureLogin");
    }
}
