/// <summary>
/// 流程状态基类（纯 C#，仿 UGF Procedure 的轻量实现）。
/// 生命周期：OnEnter（进入，恰好一次）→ OnUpdate（驻留期间每帧）→ OnLeave（离开，恰好一次）。
/// 切换请在 OnUpdate 中调用 procedureManager.ChangeProcedure&lt;T&gt;()：
/// 只登记目标状态，实际切换延迟到本帧 OnUpdate 结束后执行，每帧最多切换一次，
/// 不会在初始化调用栈中连续递归进入多个状态。
/// </summary>
public abstract class ProcedureBase
{
    /// <summary>进入状态时调用（含 ProcedureMgr 初始化时进入首个状态）</summary>
    public virtual void OnEnter(ProcedureMgr procedureManager) { }

    /// <summary>驻留期间每帧调用（deltaTime 为 Time.unscaledDeltaTime）</summary>
    public virtual void OnUpdate(ProcedureMgr procedureManager, float deltaTime) { }

    /// <summary>离开状态时调用（切换或 ProcedureMgr.Dispose 时恰好一次）</summary>
    public virtual void OnLeave(ProcedureMgr procedureManager) { }
}
