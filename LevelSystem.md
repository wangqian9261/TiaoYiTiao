# LevelSystem —— 《跳一跳》程序化关卡编辑器与有限关卡模式

依据《跳一跳程序化关卡编辑器制作策划案》落地。有限关卡模式替代原无限模式（客户端运行时不重新生成正式关卡，只读取烘焙后的路线）。

## 一、模块边界

```
Assets/Scripts/LevelSystem/            运行时（数据 / 生成 / 效果 / 结算 / 包装载）
  LevelTypes.cs          枚举（DifficultyTier/TileKind/RewardKind/TrapKind/ValidationSeverity）、
                         领域类型（LevelDefinition/DifficultyProfile/GeneratedTile/GeneratedLevel/
                         ValidationIssue/ValidationReport/LevelResult/LevelRecord）、
                         全局奖励/陷阱配置、四大核心接口（ILevelGenerator/ILevelValidator/
                         ILevelPackExporter/ITileEffectResolver）
  Pcg32.cs               PCG32 确定性随机（FNV-1a 稳定哈希初始化：levelId+seed+generatorVersion）
  LevelMath.cs           蓄力毫秒 ↔ 跳跃距离换算（与 JumpConfig 手感曲线严格一致；150–1250ms 口径）
  DifficultyProfiles.cs  五档难度表（基准边长 1.60m / 基准边缘间距 0.65m）+ 模拟目标通关率
  LevelJsonDto.cs        JsonUtility DTO（毫米整数；嵌套类必须 [Serializable]，见下"坑"）
  LevelGenerator.cs      生成器：路线（尺寸/间距/X-Z 转向）→ 数量公式 → 权重抽取 →
                         高约束优先放置（冲刺/幻影/反向/封印）→ 普通奖励陷阱 → 100 次尝试不静默删减
  LevelValidator.cs      发布阻断条件全集（独立重推几何，非生成结果自说自话）
  LevelPackIo.cs         领域 ↔ DTO、layoutHash（SHA-256/毫米数组）、包 SHA-256
  LevelRuntime.cs        运行时门面：包装载（索引 latest + schema 门）/ 进度存档 / 计划构建 /
                         试玩注入（SessionState）/ 结算（单关最高分 + 累计通关数）
  LevelTileEffectResolver.cs  落格效果解析（策划案数值统一出口；Coins 为护盾转换载荷）
  LevelEconomyConfig.cs  全局经济配置 ScriptableObject（奖励值/转盘奖池只在此改）
Assets/Scripts/LevelSystem/Editor/     编辑器（EditorWindow / 预览 / 模拟 / 导出）
  LevelEditorWindow.cs   Game/Level Editor 主窗口
  LevelDraftStore.cs     草稿 CRUD（Assets/GameRes/Config/Levels/Drafts/level_{id:D3}.json）
  LevelBakePipeline.cs   发布管线（存草稿→生成→阻断校验→1000模拟→烘焙→重导入校验→SHA-256→索引）
  LevelSimulator.cs      玩家误差模拟（档位无关同一"人"：σ=62ms / fumble 0.4% / 迟疑 8%）
  LevelScenePreview.cs   Scene 视图 3D 路线叠加（duringSceneGui，不落场景对象）
  LevelPlaytestBootstrap.cs  退出 Play 清空试玩草稿
  LevelRegressionMenu.cs LevelSystem/9 回归（菜单可跑；五档×{20,40,60}×种子 / 确定性 / 哈希 / schema 门）
Assets/GameRes/Config/
  LevelEconomyConfig.asset           全局经济配置（菜单 LevelSystem/1 确保创建）
  Levels/Drafts/level_*.json         关卡草稿（策划可编辑定义）
  Levels/Pack/pack_{ver}.json        发布包（全包原子替换；旧包保留供回滚）
  Levels/Pack/pack_{ver}.sha256.txt  包文件 SHA-256
  Levels/Pack/pack_index.json        包索引（latest 指向当前生效版本；回滚=指回旧版本）
```

## 二、编辑器使用（菜单 Game/Level Editor）

- **左侧列表**：新建 / 复制（换 Seed）/ 停用 / 章节内 ↑↓ 排序 / 章节与难度筛选。
- **中间预览**：2D 俯视路线（编号 / 方向 / 每跳蓄力毫秒 / 类型标签 / 格面触发范围 / 完美判定圈），只读；
  勾选 "Scene 3D 叠加" 在 Scene 视图同步线框预览。
- **右侧参数**：levelId / chapterId / 章节内顺序 / gridCount(20–60) / difficultyTier / seed / themeId / enabled。
- **底部操作**：生成预览、更换 Seed、校验关卡、玩家误差模拟（1000 次）、试玩当前关、保存草稿、烘焙并导出 JSON（发布管线）、回滚到…。
- **发布流程**：保存草稿 → 生成 → 阻断校验（Error 即阻断）→ 1000 次模拟（低于档位目标仅警告）→ 编辑器试玩 → 烘焙 JSON → 重导入校验（逐关 layoutHash + 文件 SHA-256）→ 更新索引。编辑器只产出发布产物，不连正式数据库。
- **试玩**：经 SessionState 传递草稿（不生成/不保存任何临时正式资产），进 Play 后主页免体力直入；本 Play 会话内重开均注入该草稿，退出 Play 自动清空；试玩局不写进度与经济。
- **回滚**：窗口"回滚到…"改索引 latest；新对局（新 Play 会话/新进程）即用回滚版本，进行中的对局持有原计划对象不受影响。

## 三、运行时接入点

- `JumpGame` 计划模式：`_plan != null` 时世界按烘焙瓦片全量铺设（尺寸/位置/奖励/陷阱/轴/震荡偏移逐格读取）；
  `_plan == null`（无包）回退旧无限模式。`Restart()` 每局重新解析计划（通关推进/包回滚即生效）。
- 关卡名/进度：`JumpGame.CurrentLevelName`（HUD/主页铭牌/结果页统一走它）。
- 金币：本关累计（`AddRunCoins`）+ 转盘"本关金币翻倍"回溯（`SetRunCoinDouble` 含触发前已得），结算统一入账
  （金币砖 + 通关金币 = clearCoinBase × 档位倍率）。
- 陷阱冻结：`TrapTimeFrozen`（转盘弹窗 / OnApplicationPause）冻结物品停留与全部陷阱倒计时。
- 玩家数据：PlayerMgr 新增 `diamonds`、体验皮肤（GrantTrialSkin/HasActiveTrialSkin）。
- GM 指令：`level [info]|play <levelId>|setseq <n>|next|reset|pack`；经济配置缺失回退代码默认值。

## 四、关键口径（策划案解读备忘）

- 轴心距 = 0.65 × U(间距倍率区间) × 档位系数 + 半边长和，再钳制进 150–1250ms 可达区间（≈2.2–6.6m）。
- 几何口径：理想落点链 `stand[j+1] = stand[j] + axis×D[j]`；瓦片实际位置 = 理想落点 + 横向震荡偏移。
  引擎落点判定只看行进轴投影 → 震荡不影响本跳轴距，"震荡仍满足可达性"由构造保证（校验器仍独立复核）。
- 震荡：烘焙终点位 = 理想落点 + 横移；运行时初铺在理想落点，踩中震荡砖滑到烘焙终点。
- 反向出口校验：离开反向格的有效按压 = `ChargeMsReversedAtDist`（1-p 口径），越界为警告不阻断。
- 模拟目标（≥75/60/45/30/20%）为发布底线：校准后五档中位 93/91/81/56/29% 全达标（大师档最贴近下限属预期）。

## 五、坑与边界

1. **JsonUtility 嵌套类必须 [Serializable]**：顶层平铺类可省略；含嵌套自定义类/数组时缺失会**静默截断字段**
   （曾致 levels 数组整体消失，靠发布重导入校验兜底拦截）。已全 DTO 标注；回归含"JSON 往返数组完整"检查。
2. 微信打包前必须重跑 **UIFramework/3**（新增 GameRes/Config 下经济配置与 Levels 包资产）。
3. 未实现（依赖外部系统）：广告复活（护盾是唯一复活路径）、排行榜服务端接入、皮肤商店
   （体验皮肤先记账，"已永久拥有转 100 金币"路径休眠）、正式货币服务端结算（本地即 PlayerMgr 入账）。
4. LevelEconomyConfig 缺失时回退代码默认值（窗口 OnEnable/菜单 LevelSystem/1 会自动创建）。
5. 包索引在运行时一次性装载缓存：热更包需重启进程/重进 Play 生效（进行中对局不受影响属设计语义）。
