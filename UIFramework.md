# UIFramework 完整UI框架文档（Tuanjie + UniTask + 微信小游戏 · 修复版）

> 架构特点：统一 UIRoot、六分层 Canvas（含专用遮罩层）、UIPanel 继承 AssetLoader、资源谁加载谁记录自动释放、CancellationToken 处理异步竞态；
> 资源系统**双策略**（编辑器 GameRes 模拟加载 / 真机 AssetBundle），内容根 = `Assets/GameRes`（已从 Resources 迁出，消除 webgl.data 与 AB 的双份体积），微信小游戏走 AB（包内或 CDN，由 WX SDK 配置决定）；
> 包含功能：分层 UI、UI 栈管理、栈回退、全局遮罩、遮罩点击关闭（销毁语义）、面板异步动画（纯 UniTask 无 DOTween）、打开防重复锁、关闭互斥锁、UI 内存调试日志、一键 AB 构建。
> 本文与 `Assets/Scripts/UIFramework/` 源码一一对应；**源码为权威**，本文用于架构理解与接入规范。

## 目录
- [场景层级配置](#场景层级配置)
- [框架文件清单](#框架文件清单)
- [Singleton / IInit / UILayer](#singleton--iinit--uilayer)
- [AssetLoader（资源记账基类）](#assetloader资源记账基类)
- [UIPanel（面板生命周期）](#uipanel面板生命周期)
- [ResMgr 资源系统（GameRes 模拟 / AB 拆包）](#resmgr-资源系统gameres-模拟--ab-拆包)
- [UIMgr（UI栈与遮罩核心）](#uimgrui栈与遮罩核心)
- [GameLauncher 启动入口](#gamelauncher-启动入口)
- [业务面板示例](#业务面板示例)
- [编辑器一键工具（菜单1/2/3）](#编辑器一键工具菜单123)
- [微信小游戏部署指引](#微信小游戏部署指引)
- [API 使用示例](#api-使用示例)
- [功能清单（如实版）](#功能清单如实版)
- [注意事项与已知边界](#注意事项与已知边界)

## 场景层级配置

```
UIRoot（纯容器，无Canvas）
├─ CanvasPage      // UILayer.Page   底层常驻页面    SortOrder=10
├─ CanvasMask      // 全局遮罩专用层                 SortOrder=15
│   └─ UIMask      // Image(黑,α0.6,RaycastTarget) + Button，默认隐藏
├─ CanvasPopup     // UILayer.Popup  普通弹窗        SortOrder=20
├─ CanvasTip       // UILayer.Tip    飘字提示        SortOrder=30
├─ CanvasGuide     // UILayer.Guide  新手引导        SortOrder=40
└─ CanvasTop       // UILayer.Top    强弹窗/断线提示  SortOrder=50
```

> **遮罩层设计要点**（相对旧版文档的关键修正）：UIMask 必须处于某个 Canvas 下（Graphic 无 Canvas 不渲染、不参与 Raycast）。放在 `CanvasMask(15)` 的语义：
> - Page(10) 被遮罩挡住，屏蔽底层点击；
> - Popup(20) 面板本体可点击；空白区域点击透到遮罩 → 触发"点遮罩关闭栈顶弹窗"；
> - Tip/Guide/Top(30/40/50) 在遮罩之上，不受遮罩影响（Top 强弹窗不会被误关）。
> 所有子 Canvas：`Screen Space - Overlay` + CanvasScaler(ScaleWithScreenSize 1080×1920, match 0.5) + GraphicRaycaster；UI 预制体**不带 Canvas**。
> EventSystem 与 GameLauncher 均由菜单工具自动兜底创建。

## 框架文件清单

```
Assets/Scripts/UIFramework/
├─ Runtime/
│  ├─ Singleton.cs          // 泛型单例基类（自动DontDestroyOnLoad+去重）
│  ├─ IInit.cs              // 管理器初始化接口
│  ├─ UILayer.cs            // Page/Popup/Tip/Guide/Top 枚举
│  ├─ AssetLoader.cs        // 面板资源记账基类（谁加载谁记录）
│  ├─ UIPanel.cs            // 面板基类与生命周期
│  ├─ IResLoader.cs         // 资源加载策略接口
│  ├─ BundleMap.cs          // key→AB包名/地址 单一映射来源（模块级细拆规则 + Boot/Battle 分组）
│  ├─ EditorAssetLoader.cs  // 编辑器模拟加载（GameRes 直读 AssetDatabase，#if UNITY_EDITOR）
│  ├─ BundleLoader.cs       // AssetBundle 模式（微信包内/CDN；包级在途去重 + 预热接口）
│  └─ ResMgr.cs             // 资源管理器（缓存+引用计数+双策略调度+包预热API）
├─ Demo/
│  ├─ GameLauncher.cs       // 启动入口（串行初始化 + 打开首页）
│  ├─ PanelHome.cs          // Page 层示例
│  └─ PanelReward.cs        // Popup 层示例（异步图标+缩放动画）
└─ Editor/
   ├─ UIFrameworkSetup.cs   // 菜单1/2：场景层级、演示Prefab
   └─ UIFrameworkABBuilder.cs // 菜单3：AB 构建
```

## Singleton / IInit / UILayer

与常规实现一致：`Singleton<T>` 懒加载 + FindObjectOfType 兜底 + Awake 去重；`IInit.OnInit()` 供 GameLauncher 统一调度；`UILayer` 五层枚举。

## AssetLoader（资源记账基类）

UIPanel 的父类，核心修复与约定：

```csharp
// 计数字典：同一 key 加载 N 次就释放 N 次（修复 HashSet 去重导致的计数失衡泄漏）
private Dictionary<string, int> _loadedAssetCounts;

public async UniTask<T> LoadAsset<T>(string assetKey) where T : Object;   // CTS 防销毁竞态
public void TrackAsset(string assetKey);      // 外部（UIMgr）加载的面板prefab记入本面板账本
public void UnloadAllAssets();                // 按计数逐次 Release
public void UnloadSingleAsset(string assetKey);
public IEnumerable<string> GetLoadedAssetKeys(); // 内存日志用
```

- CTS 在 `Awake` 创建、`OnDestroy` 取消并释放；`OnDestroy` 带 `Application.isPlaying` 守卫，避免编辑器析构期误触 ResMgr。

## UIPanel（面板生命周期）

```csharp
public virtual void OnCreate();                       // 实例化后一次
public virtual void OnOpen(object param = null);     // 每次打开
public virtual UniTask OnOpenAnim();                 // 默认 UniTask.CompletedTask
public virtual void OnClose();                       // 由 UIMgr 恰好调用一次
public virtual UniTask OnCloseAnim();                // 默认 UniTask.CompletedTask
public virtual void DestroyPanel();                  // 只 Destroy，不再调 OnClose（防双调）
```

- 动画默认实现返回 `UniTask.CompletedTask`（消除无意义 `async` 的 CS1998 警告）。

## ResMgr 资源系统（GameRes 模拟 / AB 拆包）

### 模式

```csharp
public enum ResLoadMode { Auto, ResourcesOnly, BundleOnly }
ResMgr.Instance.LoadMode = ResLoadMode.Auto; // 运行时可切换
```

| 模式 | 行为 |
|---|---|
| `Auto` | 编辑器 → GameRes 模拟加载（EditorAssetLoader 直读 AssetDatabase）；真机（微信小游戏构建）→ AssetBundle |
| `ResourcesOnly` | **已废弃**：内容根已迁出 Resources，无 Resources API 可用——归一到 Auto 行为并警告（仅保留枚举兼容） |
| `BundleOnly` | 强制 AB（编辑器回归测试用；AB 必须与当前激活构建平台一致，现为 MiniGame 目标） |

### IResLoader 策略接口

```csharp
public interface IResLoader
{
    UniTask<T> LoadAsync<T>(string assetKey, CancellationToken token) where T : Object;
    void OnAssetReleased(string assetKey, Object asset); // 引用归零后的清理回调
    void UnloadUnused();                                  // 策略性全量清理
}
```

- **EditorAssetLoader**（编辑器模拟）：`AssetDatabase.LoadAssetAtPath` 按 `Assets/GameRes/<key>` 直读（key→路径缓存，miss 时重建一次兼容新导入资产）；免构建 AB 直接开发，与微信 AB 路径同构。
- **BundleLoader**（真机/回归）：AB 常驻缓存 + `AssetBundleManifest` 依赖预载（清单亦走 `LoadAssetAsync` 全异步）+ `LoadFromFileAsync`；**包级在途去重**（并发预热与业务加载同包只冷载一次，防双份内存）；`PreloadBundleAsync` 预热；`OnAssetReleased` 不做单个卸载，`UnloadUnused` 统一 `Unload(false)` + `UnloadUnusedAssets`。

### ResMgr 核心行为

- **缓存 + 引用计数**：`Dictionary<string, AssetItem>`（Asset、RefCount、**Source 加载器**）；Release 归零时回调给当初加载它的加载器——模式切换后释放不串。
- **在途去重**：同 key 并发请求复用同一次加载（`UniTaskCompletionSource`），修复并发双载 `_assetDict.Add` 重复键崩溃。
- **包预热**：`PreloadBundlesAsync(bundleNames, progress, token)`——只加载包文件与依赖、不加载具体资产；非 AB 策略（编辑器模拟）直接跳过；进度=已完成包数/总数（单调）。
- 取消安全：token 取消返回 null，不产生孤儿计数。

### BundleMap：key→包名/地址 单一映射来源（拆包管理核心）

映射集中在 `BundleMap.cs` 一处（ABBuilder 构建侧与 BundleLoader 运行侧共用，改映射只动这里）；
包粒度=模块对齐（微信小游戏按需下载的关键）：

| 包 | 内容 | 首载时机 |
|---|---|---|
| `prefabs` | 全部 UI 面板 prefab（texture_* 依赖自动级联） | 首屏 |
| `texture_loading` / `texture_home` / `texture_levelui` / `texture_wheel` | 加载页/主页/HUD/转盘 各屏切片 | 随所在面板 |
| `texture` | Texture 根下零散切片（Reward_Gold） | 演示奖励页 |
| `audio_bgm` / `audio_sfx` | bgm_loop、bgm_jump / 6 个 SFX | 首屏 / 战斗 |
| `config` | 关卡发布包（Levels）+ LevelEconomyConfig + demo | 首屏 |
| `jump_block` / `jump_character` / `jump_items` / `jump_traps` | 方块/角色/6 物品/7 陷阱（TJGenerators 外部模型依赖收编入包） | 战斗 |
| `shared_external` | 被多包复用的根外依赖（出现时 ABBuilder 会警告） | 按需 |

```
业务key "Prefabs/UIHomeView" → 包 "prefabs" → 地址 "Assets/GameRes/Prefabs/UIHomeView"（不含扩展名）
分组常量：BundleMap.BootGroup（首屏）/ BundleMap.BattleGroup（战斗）
AB 输出目录：Assets/StreamingAssets/ab（清单AB名=目录名"ab"）
构建报告：工程根 BuildReports/ab-report-<target>.json（控制台同步打印每包体积）
体积阈值（Framework/2 预检）：单包 >4MB 警告；总量目标 <15MB、硬上限 20MB（包内模式 wasm+data 总限 ~29MB）
```

## UIMgr（UI栈与遮罩核心）

相对旧版文档的全部修复（均已回归验证）：

1. **入栈先于打开动画**：`OpenPanel` 在 `await OnOpenAnim()` **之前** Push + 刷新遮罩——动画期间关栈顶/点遮罩作用于正确面板（旧版动画后才压栈，期间会关错层）。
2. **ClosePanel 从栈剔除**：允许关闭非栈顶面板且不留脏引用；不在栈中的面板拒绝重复生命周期。
3. **单实例守卫**：命中缓存且已在栈中 → 直接返回现有实例，不再重复 Push。
4. **防重复锁 try/finally**：加载异常不会永久锁死面板。
5. **关闭互斥 `_closingTop`**：连点遮罩/关闭按钮时，关闭动画期间的第二次关闭被忽略，不会连环 Pop。
6. **PopAllToPage 重入保护**。
7. **遮罩仅由 Popup/Guide 触发**：Tip 飘字不压暗全屏；Top 层点遮罩不关闭。
8. `CloseTopPanel/ClosePanel/PopAllToPage` 均为 `UniTask`（不再 async void）；空判与明确报错。

```csharp
public async UniTask<UIPanel> OpenPanel(string panelKey, UILayer layer, object param = null);
public async UniTask CloseTopPanel(bool destroy = false);
public async UniTask ClosePanel(string panelKey, bool destroy = false);
public async UniTask PopAllToPage();
public void ClearAllPanel();
public Transform GetLayerRoot(UILayer layer);
public void PrintUIMemoryLog();
```

> **遮罩点击语义（项目既定决策）**：点遮罩 = `CloseTopPanel(true)` **销毁**栈顶普通弹窗；缓存复用仅对 `ClosePanel(key, destroy:false)` 调用方生效。
> **微信返回键**：微信小游戏公开 API 无可拦截的返回键（已核实 SDK 全源码），旧版文档的"返回按键拦截"功能**不可实现**，相关代码已删除。

## GameLauncher 启动入口

```csharp
private async void Start()
{
    try
    {
        await InitAllManagers();   // ResMgr → UIMgr 串行初始化，后续Mgr续加
        await UIMgr.Instance.OpenPanel("Prefabs/UIHomeView", UILayer.Page);
    }
    catch (Exception e) { Debug.LogException(e); } // async void 不吞异常栈
}
```

挂在启动场景 `GameLauncher` 物体上，仅启动场景存在（管理器自带 DontDestroyOnLoad）。

## 业务面板示例

- **PanelHome**：`OnCreate` 注册按钮 → `UIMgr.Instance.OpenPanel("Prefabs/Panel_Reward", UILayer.Popup).Forget();`
- **PanelReward**：`OnCreate` 先注册关闭回调再 `await LoadAsset<Sprite>("Texture/Reward_Gold")`（销毁竞态安全）；`OnOpenAnim/OnCloseAnim` 用 `Time.unscaledDeltaTime` 插值缩放（0.3s/0.25s），循环内带 `panelRoot == null` 守卫。

## 编辑器一键工具（菜单1/2/3）

| 菜单 | 作用 | 幂等 |
|---|---|---|
| `UIFramework/1. Build Scene Hierarchy` | 删旧测试Canvas；建 UIRoot+六Canvas+UIMask；兜底 EventSystem/GameLauncher；保存场景 | ✓ |
| `UIFramework/2. Create Demo Prefabs` | 生成 `GameRes/Prefabs/Panel_Home、Panel_Reward` 与占位金币 Sprite | ✓ |
| `UIFramework/3. Build AssetBundles` | 按当前激活平台构建 AB 到 `Assets/StreamingAssets/ab`（BundleMap 细拆 + 外部依赖收编 + 体积报告，先清空旧产物） | ✓（覆盖旧AB） |
| `UIFramework/4. Migrate Resources → GameRes` | 一次性内容根迁移（已完成则幂等跳过） | ✓ |

## 微信小游戏部署指引

1. **AB 目标平台**：**打微信小游戏前必须在 MiniGame（WeixinMiniGame）平台下执行菜单3**（AB 与构建平台不一致时加载失败）；当前工程常驻 MiniGame 平台，AB 即产即用。
2. **资源进包 vs CDN**（微信转换面板 `MiniGameConfig`）：
   - `assetLoadType=1`（包内，当前配置）：AB 转 data 文件进 `data-package` 分包，与 `wasmcode` 分包开机并行全量下载；**wasm + data 总限 ~29MB**（超限 SDK 自动降级 CDN 并报错）；
   - `assetLoadType=0` + 填写 `CDN`：AB 按包懒下载（Boot组 ≈1.6MB / Battle组 ≈4MB），SDK 自带磁盘缓存，内容可热更免重审；需将转换产物上传至该 CDN。
   - SDK 依据 `bundlePathIdentifier=StreamingAssets;` 识别 AB 所在目录，保持默认即可。
3. 代码零分叉：`ResLoadMode.Auto` 在真机自动走 AB，编辑器走 GameRes 模拟；**结构已 CDN 就绪**——拿到 CDN 后只改 MiniGameConfig + 上传产物即可切换。
4. 打包顺序：切 MiniGame 平台 → AB 重建（**已自动化：ABAutoBuildHook 在 Build 按钮与任何 BuildPlayer（含微信面板「转换小游戏」）前置钩子中自动检测新鲜度并重建**，无需手动记得跑菜单3）→ Framework/2 预检 → WX 面板转换 → 核对 Build/minigame 的 data-package+wasmcode 总量 ≤29MB → 微信开发者工具/真机验证。
5. 编辑器 BundleOnly 回归注意：MiniGame 目标 AB 的音频在 macOS 编辑器不可解码（`Unsupported file or audio format`）——资产加载与游戏逻辑正常，仅本机播放报错、真机正常；如需编辑器听音回归可临时构建 Standalone 目标 AB 并用 `BundleLoader.BaseDirOverride` 指过去。

## API 使用示例

```csharp
// 打开主页（底层Page）
await UIMgr.Instance.OpenPanel("Prefabs/UIHomeView", UILayer.Page);

// 打开奖励弹窗（Popup）
await UIMgr.Instance.OpenPanel("Prefabs/Panel_Reward", UILayer.Popup);

// 关闭栈顶面板（销毁并释放）
await UIMgr.Instance.CloseTopPanel(true);

// 关闭指定面板但保留缓存复用
await UIMgr.Instance.ClosePanel("Prefabs/Panel_Reward", false);

// 一键关闭所有弹窗回退到底层Page
await UIMgr.Instance.PopAllToPage();

// 打印UI资源内存日志（调试）
UIMgr.Instance.PrintUIMemoryLog();

// 强制 AB 模式回归（编辑器）
ResMgr.Instance.LoadMode = ResLoadMode.BundleOnly;

// 预热首屏/战斗组 AB 包（编辑器模拟模式自动跳过；进度=已完成包数/总数）
var progress = new Progress<float>(p => Debug.Log($"预热 {p:P0}"));
await ResMgr.Instance.PreloadBundlesAsync(BundleMap.BootGroup, progress);
await ResMgr.Instance.PreloadBundlesAsync(BundleMap.BattleGroup);

// GM 调试：res info（模式+已载包清单）/ res warm boot|battle|<包名>
```

## 功能清单（如实版）

1. ✅ 多分层 UI 渲染管理（五层 + 专用遮罩层）
2. ✅ UI 栈层级管理与页面回退（含非栈顶关闭、单实例、重入保护）
3. ✅ 异步动画生命周期（无 DOTween，unscaled，动画期间栈语义正确）
4. ✅ 资源双策略加载（GameRes 模拟 / AssetBundle）+ 引用计数释放 + 释放回调按加载器分发 + 模块级细拆（BundleMap）+ 包预热与包级在途去重
5. ✅ 防重复点击、防重复打开锁 + 关闭互斥锁
6. ✅ 全局遮罩交互拦截（Popup/Guide 触发，Top 不受遮罩误关）
7. ✅ 完整内存日志调试体系
8. ✅ 全局统一初始化架构
9. ✅ 微信小游戏 AB 首包/CDN 部署链路（编辑器+AB回归已验证；**返回按键拦截不可实现已移除**）
10. ✅ 一键场景/演示资产/AB 构建工具

## 注意事项与已知边界

1. 场景节点名称大小写与代码严格匹配；重建层级请用菜单1。
2. `Instantiate(prefab, parent, false)` 保持局部坐标。
3. 动画用 `Time.unscaledDeltaTime`，暂停时仍播放。
4. 栈模型：同 PanelKey 单实例；遮罩点击销毁语义为既定决策，如需"点遮罩隐藏复用"改 `OnMaskClick` 中 `CloseTopPanel` 参数即可。
5. 打开动画与关闭动画**恰好重叠**的瞬间（<0.05s 窗口）存在缩放双写的小概率视觉抖动，循环守卫保证不抛错；如需彻底消除可后续为面板增加动画 CTS。
6. AB 分包映射（key→包名、地址）统一在 `BundleMap` 单一来源；**内容根 = `Assets/GameRes`**——游戏资产必须放这里（TJGenerators 导入 output_path 同理），`Assets/Resources` 已弃用。根外依赖（TJGenerators 模型等）由 ABBuilder 按首引用者收编、跨包复用自动移入 `shared_external`；模型贴图请跑 `Framework/4. Optimize AB External Textures`（512/关 mipmap）。
7. 微信真机 AB/CDN 加载的最终验证待一次真实微信构建（编辑器侧已按 SDK 机制对接并回归通过模拟与 AB 双路径）。
8. **AB 自动重建钩子**（`ABAutoBuildHook.cs`，[InitializeOnLoad] 自动注册）：①Build 按钮被接管——先确保 AB 新鲜再执行标准构建并弹结果（Tuanjie 的 `RegisterBuildPlayerHandler` 是替换式、无 DefaultBuild 回退，故由钩子自己调 `BuildPipeline.BuildPlayer`）；②`ABPreprocessBuildHook : IPreprocessBuildWithReport` 覆盖一切直调 BuildPlayer 的路径（含微信转换面板）。新鲜判定=marker 平台一致+输出齐全+GameRes 与 TJGenerators/History 无晚于 marker 的改动（检测窗口 `(marker, now]`，未来时间戳文件视为已被本次构建覆盖，防时钟偏差死循环）；不新鲜自动重跑菜单3 并复核。
