# 执行任务：以已确认源图重组 UILoadingView（2026-10-22）

用户已要求根据此美术包重新拼接 UILoading，直接实施。目标工程仅 `/Users/qian/UnityProject/Game3D001/Game3D001`。不要操作 Kuangjia 或其他项目，不改 UIHome、角色、跳台、玩法，不重新生成美术。不做提交/发布，不更改账户权限或 AI 授权模式。

美术包绝对路径：`/Users/qian/Documents/ChatGPT/跳一跳美术/outputs/ui-slices/uiloading-characterless-v2`。

当前任务只涉及 Loading。本会话早期 3D 角色建模已结束，不继续/重新生成角色。UIHome 已在 2026-10-21 替换为无角色花园版，必须完整保留，不回退。新的完整源图为 `sources/reference-original.png`。用户明确要求“根据这个图拆分，然后让团结 AI 重新拼接 UILoadingView”，美术拆分已就绪，请执行工程拼装并验收，不只给计划。

先读 `README.md`、`layout.json`，查看 `previews/assembled-065-750x1334.png` 和 `previews/reference-vs-reassembled.png`。以独立 PNG 和 JSON 拼接，不要把整张效果图铺上去冒充动态 UI。底部加载状态和百分比必须为引擎 Text/TMP，不能烘焙。

## 最小修改范围

1. 操作前确认工程路径、编辑器播放状态与是否有用户未保存修改；不得丢弃用户改动。需要时停止主页预览；若处于正在进行的对局或存在 dirty 场景，先报告，不得静默保存/覆盖。团结桥接 manage_editor.refresh/request_compile 会自动保存 dirty 场景，这次不要再次触发该副作用。把本次将改的原文件和 meta 备份到工程外的美术包 `integration-backup/`（不覆盖任何历史备份）。不要改主场景内容。
2. 纹理只在 `Assets/GameRes/Texture/Loading/` 内更新。映射：`loading_background.png`→`loading_background_750x1334.png`；`loading_logo.png`→`loading_logo_frame.png`；track/fill/marker 保持原工程同名；新建 `loading_panel_base.png`。已有资源 meta/GUID 保留。不删除其他资源。
3. 更新现有 `Assets/GameRes/Prefabs/UILoadingView.prefab`，保持 prefab GUID、资源 Key `Prefabs/UILoadingView` 和 `UILoadingView` 脚本身份。背景、Logo、LoadingPanel、空轨道、fill、marker、StatusText、PercentText 分层。新底板不含条和文字。
4. 现有 `Assets/Scripts/UI/Editor/UILoadingViewSetup.cs` 目前在 prefab 已存在时直接跳过；需要最小更新为可重复更新当前 prefab 的工具，不能只调用旧 CreateAll 并声称完成。使用 PrefabUtility 更新，不删除再建以避免 GUID 变化。保留其他工程现有规范。
5. `Assets/Scripts/UI/UILoadingView.cs` 保持 `SetProgress(float)`、`SetFailed()`、`Progress`、`IsFailed`、`LoadingEvents.RetryRequested` 契约。最小增加独立百分比 Text 引用。正常状态显示“正在进入花漾世界…”，百分比显示 RoundToInt(Clamp01(p)*100)+"%"。花朵花心依 manifest 跟随 p，0% 隐藏；失败态显示“加载失败，点击重试”，原重试事件继续有效。失败后相同进度、再次 OnOpen、缓存复用要恢复正常文本，避免脏检查遗漏。
6. 不更改 `Assets/Scripts/Procedure/ProcedureLoadingDriver.cs` 的流程映射、最短展示时长、平滑进度、关闭逻辑。保留当前模态/点击阻断。不要新增虚假的倒计时。
7. 保持项目现有全局 CanvasScaler 1080×1920，按 manifest 统一换算。背景等比 Cover，Logo上中，加载整组下中；接入现有安全区逻辑，系统胶囊不得遮挡标题。不得拉伸珍珠/花朵。
8. 字体：使用本包 `fonts/HuayangLoading-SC-Medium.otf` 和 `fonts/OFL.txt`，导入 `Assets/GameRes/Fonts/Loading/`；这是 Source Han Sans SC Medium 的 OFL 1.1 子集（已重命名，覆盖本页正常/失败文案和 ASCII），约 24KB。不要改 UIHome 的 HuayangUI-SC-Medium.otf。不要依赖系统 fallback。导入后逐字检查 HasCharacter / fontData。纹理建议 CompressedHQ/quality100，避免默认 ASTC6x6 导致花朵糊块；背景 max2048、控件 max1024。

## 验收

- 定向验证 0%、65%、100%，fill、花心、百分比一致；SetFailed→同值SetProgress→OnOpen，事件只派发一次；prefab全部字段非空，无丢失脚本。
- 保存游戏内 750×1334 对照截图；再验证 9:19.5 和 3:4 比例及安全区，不遮挡、不变形。若测试环境不能支持全部比例，准确报告已测与未测。
- 正常启动流程 Loading→Home，Loading真实关闭，不修改/遮挡Home，不触发角色重建。
- 检查真实编译结果、相关Console错误；区分既有编辑器插件异常和新增错误。不清空原日志掩盖问题。
- 若运行时需要 AB，沿用项目现有构建规则。重建会覆盖已有产物时先备份对应产物；只在完成编译/接线后执行，记录实际构建结果。不要将仅 AssetDatabase 下成功说成小游戏发布成功。
- 结果保存到美术包 `integration-result/`：改动文件清单、实际测试结果、截图、未完成限制。只有真实验证后才称完成；遇到授权/访问阻塞明确报告，不通过其他渠道绕过。

测试注意：通过桥接执行的 Editor 回调中 Screen.width/height 可能对应 SceneView；实际 EventSystem 射线测试应在运行时 MonoBehaviour 协程中进行，使用 Canvas.pixelRect 作为断言尺寸，避免误报。测试临时状态要恢复，不清玩家存档。至少保留 0/65/100% 和失败态的实际引擎截图。

注意：美术为平面图补绘分层，细节存在重建差异，不能宣称像素级无损；Logo及固定标语保留为美术字。该任务不包含 Home。
