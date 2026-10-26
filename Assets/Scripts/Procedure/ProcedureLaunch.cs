using UnityEngine;

/// <summary>
/// 启动流程（空流程演示）：进入即记录日志，每帧推进一步。
/// 真实业务接入点：框架预热、SDK 初始化等，完成后在 OnUpdate 中切换。
/// </summary>
public sealed class ProcedureLaunch : ProcedureBase
{
    public override void OnEnter(ProcedureMgr procedureManager)
    {
        Debug.Log("[Procedure] 进入 ProcedureLaunch");
    }

    public override void OnUpdate(ProcedureMgr procedureManager, float deltaTime)
    {
        procedureManager.ChangeProcedure<ProcedureCheckVersion>();
    }

    public override void OnLeave(ProcedureMgr procedureManager)
    {
        Debug.Log("[Procedure] 离开 ProcedureLaunch");
    }
}
