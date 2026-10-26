# Framework 微型框架文档（Tuanjie + UniTask + 微信小游戏）

> 与 `UIFramework.md`（UI 框架）配套的通用微型框架：基础底层、核心管理器、微信平台适配层、调试控制台与编辑器打包工具。
> 资源加载复用 UIFramework 的 `ResMgr` 双策略（编辑器 GameRes 模拟 / 真机 AssetBundle，内容根=Assets/GameRes）；启动入口复用 `GameLauncher`。
> **源码为权威**：`Assets/Scripts/Framework/`；本文用于架构理解与接入规范。

## 目录

```
Assets/Scripts/Framework/
├─ Base/                 # PureSingleton（纯C#单例）、EventCenter（事件总线）、MonoDriver（全局Mono驱动）
├─ Managers/             # AudioMgr、PoolMgr、DataMgr、InputMgr、FloatTextMgr
├─ WeChatPlatform/       # WeChatService 门面 + 六模块接口/Mock/真机实现
├─ DebugConsole/         # DebugConsoleMgr（悬浮日志 + GM 指令）
├─ Util/                 # RandomUtil / TimeUtil / StringUtil / Extensions
├─ Demo/                 # 演示 DTO（DemoConfig / DemoPlayerSave）
└─ Editor/               # Framework/1 演示资产、Framework/2 打包预检、Framework/3 宏管理窗
```

## 一、基础底层

### PureSingleton\<T\>（纯 C# 单例）
- `Instance` 懒加载线程安全；`Init()` 幂等（GameLauncher 统一调用，`OnInit()` 只执行一次）。
- MonoBehaviour 单例仍用 UIFramework 的 `Singleton<T>`，两者命名并存不冲突。

### EventCenter（事件总线）
- `AddListener / RemoveListener / Dispatch`（无参 Action 与带参 Action\<T\>）。
- 规则：`Dispatch(key)` 只通知无参订阅；`Dispatch(key, arg)` 通知同类型 Action\<T\> 与无参 Action；订阅者异常互相隔离；同一委托重复添加自动去重。
- `EventKeys`：框架级事件约定（`Input.Tap / Input.DragStart/Dragging/DragEnd / Input.FirstTouch / WeChat.OnShow / WeChat.OnHide / UI.FloatText`）。

### MonoDriver（全局 Mono 驱动）
- 唯一驱动物体（自动 DontDestroyOnLoad）：`UpdateTick / FixedUpdateTick / LateUpdateTick` 事件（订阅者异常隔离）。
- `RunCoroutine / NextFrame / Delay(seconds, cb, unscaled) / Interval`，句柄经 `CancelTimer` 取消。

## 二、核心管理器

### AudioMgr（音频）
- BGM 单通道（UniTask 淡入淡出，操作自动串行防并发打架）+ SFX `PlayOneShot` 轮转池（6源）。
- `PlayBgm(key, fade) / StopBgm(fade) / PlaySfx(key, vol) / SetBgmVolume / SetSfxVolume / SetMuted`。
- **WebGL/微信限制**：`AudioListener.pause` 门控，等 `EventKeys.FirstTouch` 解锁并补播待播 BGM；编辑器直接解锁。
- 资产经 ResMgr 加载，key 如 `Audio/click`。

### PoolMgr（对象池）
- `RegisterPoolAsync(key, prefabResKey, warmup)` 幂等；模板常驻（经 ResMgr）。
- `Spawn(key, pos?, rot?)` / `Despawn(key, go)`（隐藏+归位+重置局部变换）；**LIFO 复用**（后回收先复用）；外部销毁的脏引用自动跳过。
- `ClearPool / ClearAll / GetStats`（DebugConsole `mem` 指令展示）；可选实现 `IPoolable`（OnSpawn/OnDespawn）。

### DataMgr（数据）
- 配置表：`LoadConfigAsync<T>("Config/demo")`（GameRes/Config JSON，经 ResMgr 加载并常驻缓存）。
- 存档：`Save<T>(key, data, obfuscate) / Load<T>(key, obfuscated) / Delete`，落地经 `WeChatService.Storage`。
- `obfuscate=true` 时 XOR+Base64 混淆（防随手窥看，非加密；正式加密请换 AES/服务端）。

### InputMgr（输入）
- 屏幕事件：Tap / DragStart / Dragging(DragArgs{Position,Delta}) / DragEnd，触摸与鼠标统一（微信触摸经 SDK 转 Unity Input，无需直连 wx 事件）。
- 首次触摸派发一次 `FirstTouch`（AudioMgr 解锁依赖）。
- 辅助射线：`RaycastUI(pos)`（最上层UI物体）/ `RaycastWorld(pos)`（主相机命中物）；GM/测试用 `SimulateTap(pos)`。

### FloatTextMgr（全局飘字 UI）
- **事件驱动**：各系统 `EventCenter.Dispatch(EventKeys.FloatText, "文本")` 即显示，零依赖接入。
- **常驻不销毁**：运行时自建 `FloatTextCanvas`（ScreenSpaceOverlay，sortingOrder=60：CanvasTop(50) 之上、DebugConsole(100) 之下；CanvasScaler 1080×1920/match 0.5）+ DontDestroyOnLoad；不进 UIMgr 面板栈——PopAllToPage/ClearAllPanel/游戏世界销毁均影响不到。
- **一次只显示一条**：FIFO 排队逐条完整显示；队列上限 8，溢出丢最旧。
- **排重=冷却窗口**：同一文本 3s 内只受理一次（按入队受理时刻记时；过期项在新受理时惰性清理——表内容=最近受理过的互异文本集合）。
- **对象池**：条目空闲栈复用（串行显示 → 常态仅 1 个条目反复复用）。
- 样式与节奏同战斗 Toast：44 号品牌金 + 黑影，屏幕正中 (0,0)，前 60% 停留 + 后 40% 上飘 60px 淡出（共 1.4s，unscaled 时间）。
- **与战斗 Toast 关系**：`Jump.Toast → UIBattleView` 原通道保持不变（战斗内提示专用）；全局飘字只订阅 `UI.FloatText`，两键互不串扰。
- GM：`toast <文本>`（走纯事件链路）/ `toast info`（队列/池/冷却统计）。

## 三、微信平台适配层（WeChatService 门面）

**业务层只经 `WeChatService.*` 访问平台能力，禁止直接调 `WX.*`。** `WeChatService.Init()`（GameLauncher 已调用）自动按环境装载：

| 环境 | 实现 |
|---|---|
| 编辑器 / Standalone 等 | Mock 全家桶（PlayerPrefs 存储、模拟回调） |
| 微信小游戏构建（WebGL 目标） | WX SDK 真实实现 |

- `Lifecycle`：OnShow/OnHide → `EventCenter` 事件（后台暂停恢复逻辑订阅 `EventKeys.WXShow/WXHide` 即可）。
- `Storage`：两端统一 **PlayerPrefs**——微信构建由 SDK 的 weapp-adapter 自动桥接 `wx.setStorageSync`（单key与总量约10MB）。
- `Share.Share(content) / Share.EnablePassiveShare(content)`（主动/被动分享）。
- `Login.LoginAsync() → {Ok, Code}`（Code 交服务端换 openid）；`GetUserInfoAsync()` 注意：真机须在用户点击链路中调用，且微信新规下可能返回匿名头像昵称。
- `Ads.ShowRewardVideoAsync(adUnitId) → {Ok=完整看完, Shown}`（UniTask 化，失败自动预载重试一次）；`ShowInterstitial / ShowBanner / HideBanner`。
- `Report.ReportEvent / ReportMonitor`；`GetStorageReport()`（微信存储占用）；`Vibrate(bool)`。

**编译裁剪（重要）**：wx-runtime 系 DLL 仅覆盖 Editor / WebGL / WeixinMiniGame 平台。真机实现整体包在
`#if UNITY_EDITOR || UNITY_WEBGL || WEIXINMINIGAME` 内（见 `WeChatModules.cs` / `WeChatService.cs`）——其他目标（Standalone 回归等）只编译 Mock，保证全目标可编译。环境检测同样基于 Unity 自带宏，不自造 `WECHAT_MINIGAME`。

## 四、DebugConsole（调试控制台）

- 代码构建 UI（无资源依赖）：左上角"≡"开关 → 半透明日志面板（环形缓冲200行，Error/Warning 着色）+ 命令输入框 + RUN。
- **Release 自动关闭**（仅 编辑器 / Development 构建创建；`EnabledInRelease` 常量可强制开启）。
- 业务扩展：`DebugConsoleMgr.RegisterCommand("cmd", "说明", handler)`；`Execute("cmd args")` 与控制台输入同源。
- 内置指令：`help / clear / mem / fps / time <scale> / audio bgm|sfx <key> | mute 0|1 / pool spawn|despawn|clear|stats <n> / wx hide|show|share|login|reward ok|fail / save / load / cfg <key> / ui home|reward / toast <文本>|info（全局飘字）`。

## 五、编辑器工具

| 菜单 | 作用 |
|---|---|
| `UIFramework/1、2、3` | 场景层级 / 演示Prefab / AB 构建（详见 UIFramework.md） |
| `Framework/1. Create Demo Assets` | 程序化生成 click/BGM wav、PoolCube.prefab、Config/demo.json（幂等） |
| `Framework/2. Pre-Build Check` | 打包前检查（见下） |
| `Framework/3. Define Manager` | 按 BuildTargetGroup 查看/增删宏定义（微信相关宏由平台自动注入，仅作查看与业务自定义） |
| `Framework/4. Optimize AB External Textures` | 根外模型贴图治理（512/关 mipmap，幂等；AI 新导入模型后重跑） |

**打包前检查**（转换/打包前必跑）：
1. AB 目标记校验：`ABBuildTarget.marker`（工程根，菜单3构建时写入目标平台+时间）——AB 目标必须与当前激活平台一致；GameRes 资产晚于 AB 构建时提示重跑。
2. 大资源扫描：贴图>1MB / 音频>500KB 列表 Top10。
3. AB 体积：单包 >4MB 警告；总量目标 <15MB、硬上限 20MB（包内模式 wasm+data 总限 ~29MB）+ Boot/Battle 分组小计。
4. MiniGameConfig 一致性：`assetLoadType=0`（CDN 模式）必须有 CDN；`DeleteStreamingAssets` 建议为 1。

⚠ **平台切换注意**：用 ABBuilder 构建 StandaloneOSX 等测试 AB 会切换激活构建平台；打包微信小游戏前务必切回 MiniGame（`EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTarget.MiniGame)`）并重跑 `UIFramework/3`，`Framework/2` 会拦截不一致。

## 六、启动顺序（GameLauncher）

```
ResMgr.OnInit → UIMgr.OnInit → WeChatService.Init（平台门面，DataMgr存档依赖）
→ PoolMgr.Init → DataMgr.Init → AudioMgr.Init → FloatTextMgr.Init（全局飘字）→ InputMgr.Init（依赖MonoDriver自动创建）
→ DebugConsoleMgr.Init（Release空操作）→ 打开 UIHomeView（正式主页）→ PlayBgm（首触解锁后补播）
```

## 七、微信小游戏部署（与 UIFramework.md 部署节配合）

1. AB：切 MiniGame 平台 → `UIFramework/3` 构建生产 AB → `Framework/2` 预检。
2. CDN 优先：MiniGameConfig `assetLoadType=0` + 填 CDN → 转换产物上传 CDN（不占首包）；包内模式=1 则 AB 进首包。
3. 真机注意：首触前无音频（解锁自动补播）；`GetUserInfo` 须点击链路；激励视频 `adUnitId` 用微信后台申请的真实位；`ReportEvent` 在数据分析后台查看。

## 八、已知边界

- BGM 演示音频为程序化生成（非无缝循环），生产请换正式音频。
- XOR 存档仅为防窥混淆。
- PoolMgr 的 ActiveCount 对"外部直接 Destroy"仅作近似（Spawn 时跳过脏引用）。
- `SetMuted` 不断正在播的 SFX（自然播完）。
- 微信真机的登录/分享/广告/上报链路已在编辑器 Mock 全流程验证，**真机最终验证待一次微信构建**。
