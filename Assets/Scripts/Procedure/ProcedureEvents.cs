/// <summary>
/// 流程状态机事件 Key（业务模块自建常量类，点分风格对齐框架级 EventKeys）。
/// </summary>
public static class ProcedureEvents
{
    /// <summary>流程状态切换完成（含初始化进入首个状态）。参数：当前状态类名（string）</summary>
    public const string Changed = "Procedure.Changed";
}
