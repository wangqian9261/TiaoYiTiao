# Procedure 轻量流程状态机文档（仿 UGF Procedure · Tuanjie）

> 定位：轻量仿 UnityGameFramework Procedure 的流程状态机，**不引入 UGF 依赖**、无反射、无 asmdef；
> 固定流程：`Launch → CheckVersion → UpdateManifest → Preload → Login → Game`，前五个为空流程演示（每帧推进一步），`ProcedureGame` 为最终常驻状态；
> 本文与 `Assets/Scripts/Procedure/` 源码一一对应；**源码为权威**，本文用于架构理解与接入规范。

## 目录
- [文件清单](#文件清单)
- [流程与驱动链路](#流程与驱动链路)
- [生命周期](#生命周期)
- [切换规则与异常约定](#切换规则与异常约定)
- [ProcedureEvents.Changed 事件](#procedureeventschanged-事件)
- [公共 API](#公共-api)
- [流程加载页 UILoadingView](#流程加载页-uiloadingview)
- [扩展方式](#扩展方式)
- [验收要点](#验收要点)

## 文件清单

```
Assets/Scripts/Procedure/
├─ ProcedureBase.cs             // 状态基类（OnEnter/OnUpdate/OnLeave，纯 C#）
├─ ProcedureMgr.cs              // 状态机管理器（PureSingleton：注册/切换/每帧推进/Dispose）
├─ ProcedureEvents.cs           // 事件 Key 常量（Procedure.Changed）
├─ ProcedureLaunch.cs           // 启动（空流程演示）
├─ ProcedureCheckVersion.cs     // 校验版本（空流程演示）
├─ ProcedureUpdateManifest.cs   // 更新资源清单（空流程演示）
├─ ProcedurePreload.cs          // 预加载（空流程演示）
├─ ProcedureLogin.cs            // 登录（空流程演示）
├─ ProcedureGame.cs             // 游戏主流程（最终常驻状态）
└─ ProcedureLoadingDriver.cs    // 流程加载页驱动（Procedure.Changed → UILoadingView，见下文）
```

## 流程与驱动链路

```
GameLauncher.Start
   └─ InitAllManagers 末尾：ProcedureMgr.Instance.Init()   // PureSingleton 幂等初始化
        ├─ 显式注册六个状态（无反射）→ 立即进入 ProcedureLaunch → 派发 Procedure.Changed
        ├─ 订阅 MonoDriver.UpdateTick → 每帧 Tick(Time.unscaledDeltaTime)
        └─ 订阅 Application.quitting → Dispose()
MonoDriver.Update（每帧）
   └─ ProcedureMgr.Tick：当前状态 OnUpdate → 消费待切换请求（每帧最多一次）
```

- 驱动走 `MonoDriver.UpdateTick` 而非 `GameLauncher.Update`：GameLauncher 仅存在于启动场景，管理器自带的 DontDestroyOnLoad MonoDriver 才是全生命周期的每帧驱动源。
- `Application.quitting` 在编辑器停止 Play 时同样触发：当前状态恰好执行一次 `OnLeave`，随后清空全部状态与待切换请求。

## 生命周期

| 回调 | 时机 | 说明 |
|---|---|---|
| `OnEnter` | 进入状态（含初始化进入首个状态） | 恰好一次；此刻 `CurrentProcedure` 已指向新状态 |
| `OnUpdate` | 驻留期间每帧（MonoDriver 驱动） | `deltaTime = Time.unscaledDeltaTime`；切换请求在此发起 |
| `OnLeave` | 切换离开 / Dispose | 恰好一次 |

## 切换规则与异常约定

- `ChangeProcedure<T>()` **只登记**目标状态，实际切换延迟到本帧 `OnUpdate` 结束后执行：每帧最多切换一次，不会在初始化调用栈中连续递归进入多个状态。
- 切换顺序固定：旧状态 `OnLeave` → 更新当前状态引用 → 新状态 `OnEnter` → 派发 `ProcedureEvents.Changed`。
- 以下误用抛 `InvalidOperationException`（消息带 `[ProcedureMgr]` 前缀）：
  1. 重复注册同一状态类；
  2. 切换到未注册的状态；
  3. 初始化前调用 `ChangeProcedure` / `Tick`；
  4. 上一次切换请求尚未消费时再次请求（含同一帧内重复请求）。
- 切换请求请在 `OnUpdate` 中发起；`OnEnter/OnLeave` 中发起虽不会递归，但属于留到下一帧生效的登记，不建议。

## ProcedureEvents.Changed 事件

每次进入状态（含初始化进入 ProcedureLaunch）派发一次，参数为当前状态类名（string）：

```csharp
// 订阅：风格对齐框架级 EventKeys 点分式，业务模块自建常量类为项目既定约定
EventCenter.AddListener<string>(ProcedureEvents.Changed, name => Debug.Log($"当前流程: {name}"));

// 取消订阅
Action<string> handler = name => Debug.Log($"当前流程: {name}");
EventCenter.AddListener(ProcedureEvents.Changed, handler);
EventCenter.RemoveListener(ProcedureEvents.Changed, handler);
```

## 公共 API

```csharp
public abstract class ProcedureBase
{
    public virtual void OnEnter(ProcedureMgr procedureManager) { }
    public virtual void OnUpdate(ProcedureMgr procedureManager, float deltaTime) { }
    public virtual void OnLeave(ProcedureMgr procedureManager) { }
}

public class ProcedureMgr : PureSingleton<ProcedureMgr>
{
    public ProcedureBase CurrentProcedure { get; }      // 当前状态（未初始化为 null）
    public string CurrentProcedureName { get; }        // 当前状态类名（未初始化为空串）
    public bool IsCurrent<T>() where T : ProcedureBase;
    public void ChangeProcedure<T>() where T : ProcedureBase;  // 仅登记，本帧 OnUpdate 后切换
    public void Tick(float deltaTime);                 // 由 MonoDriver.UpdateTick 驱动
    public void Dispose();                             // 离开当前状态并清空（quitting 自动调用）
}
// 初始化入口（GameLauncher 已接好）：ProcedureMgr.Instance.Init();
```

## 流程加载页 UILoadingView

`ProcedureLoadingDriver`（纯静态类）通过 `[RuntimeInitializeOnLoadMethod(AfterSceneLoad)]` 自装，订阅 `ProcedureEvents.Changed`，把流程链映射到通用加载页 `UILoadingView`（Top 层，全屏模态）：

- 进入 `ProcedureLaunch` 打开加载页；每个状态推进目标进度：Launch=0 / CheckVersion=0.2 / UpdateManifest=0.4 / Preload=0.6 / Login=0.8 / Game=1.0；
- 显示值按固定速率平滑追近（全程 0.45s）并保证最短展示 0.8s——空流程一闪而过由展示层兜底；将来状态接入真实耗时（版本请求/清单下载/预载/鉴权）后同一驱动继续适用；
- 进入 `ProcedureGame` 后填充追平即 `ClosePanel` 销毁加载页，露出 UIHomeView；
- 状态类与 GameLauncher **不感知 UI**（对齐扩展方式约定：状态机接口无需改动）；未登记进目标表的状态名不参与加载页，扩展新流程互不影响。

## 扩展方式

1. 新建状态：`Assets/Scripts/Procedure/` 下增加 `ProcedureXxx : ProcedureBase`；
2. 注册：在 `ProcedureMgr.OnInit` 的注册段添加 `Register(new ProcedureXxx());`
3. 流转：在上游状态的 `OnUpdate` 中 `procedureManager.ChangeProcedure<ProcedureXxx>();`

真实业务接入示例（状态机接口无需改动）：CheckVersion 等待版本请求回调成功后再切换；UpdateManifest 完成清单下载后再切换；Preload 预载必要资源（勿重复加载 ResMgr/DataMgr 已初始化的内容）；Login 鉴权成功后进入 Game。

## 验收要点

1. 运行后日志依次出现六个 Procedure（进入/离开成对，顺序与固定流程一致）。
2. 每帧最多切换一次；初始化调用栈中不会连续递归进入多个状态。
3. `Procedure.Changed` 依次收到六个状态类名。
4. 进入 `ProcedureGame` 后保持常驻，不再自动切换。
5. 停止运行（`Application.quitting`，编辑器停止 Play 同样触发）时当前状态执行一次 `OnLeave`。
6. 现有 UI/资源/数据管理器行为保持不变（主页 `UIHomeView` 由 GameLauncher 打开）。
7. 启动时加载页随流程出现：Launch 后打开、进度随状态推进（平滑+最短展示 0.8s）、进入 Game 后自动关闭销毁，`UIHomeView` 正常显现。
