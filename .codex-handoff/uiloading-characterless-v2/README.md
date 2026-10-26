# 花漾跳趣 · UILoadingView 拆图交付 v2 · 2026-10-22

设计画布 750 × 1334；原图 941 × 1672。只重组 Loading，不修改 Home、角色、玩法和其他工程。

## 素材与来源

- `assets/loading_background.png`：无标题、无加载卡片的完整背景。被遮挡区域用内置图像编辑补绘；中央无遮挡场景保留原图像素，含前景跳台蝴蝶结。
- `assets/loading_logo.png`：透明标题组件。保留固定名称“花漾跳趣”和固定标语“花开一路，跳出惊喜”。
- `assets/loading_panel_base.png`：无动态文字、无进度条的珍珠/蝴蝶结底板。
- `assets/loading_progress_track.png`：空进度轨道。
- `assets/loading_progress_fill.png`：100% 满幅填充条，运行时从左裁切。
- `assets/loading_progress_marker.png`：独立花朵指示器。

这是由平面图补绘、透明分离、裁切导出的可复用资产，不是 PSD 原始图层的无损导出。标题、底板等细节与原稿有小幅重建差异；没有重新设计场景或加入角色。完整生成提示词和来源保存在 `generation-config.json`；使用的是内置 image_gen，不是 API CLI。`sources` 只用于溯源，不应导入 Unity。

## 拼装

以 `layout.json` 为唯一坐标依据；左上角为原点，像素单位，所有 rect 为 x/y/宽/高。素材按实际 PNG 尺寸摆放，不能再次按原始生成尺寸缩放。

| 层 | 组件 | x / y | 宽 / 高 |
| --- | --- | --- | --- |
| 0 | Background | 0 / 0 | 750 / 1334 |
| 1 | Logo | 76 / 105 | 606 / 306 |
| 2 | LoadingPanel | 52 / 1033 | 640 / 222 |
| 3 | ProgressTrack | 128 / 1155 | 404 / 46 |
| 4 | ProgressFill | 135 / 1162 | 390 / 32 |
| 5 | Marker | x=135+390p−18 / y=1160 | 48 / 36 |
| 6 | StatusText | 180 / 1112 | 390 / 36 |
| 7 | PercentText | 557 / 1161 | 66 / 36 |

`p=Clamp01(progress)`。花心位置 `(135+390p,1178)`；花朵的局部花心像素是 `(18,18)`，不是整张 PNG 的中心。在 Unity 可将 Marker pivot 设置为 `(18/48, 1−18/36)`。0% 时隐藏 Marker，避免空轨道左缘突出花朵；其他进度跟随填充。轨道比原稿略缩短，为“100%”与花朵留出间隔，避免满进度遮挡数字。

注意：原稿“65%”与画出的填充长度并不精确对应；交付的预览及实际控件采用真正的 65% 长度。使用 `Image.Type.Filled / Horizontal / Left`，不能缩放整个填充条，也不要把 65% 写死。

所有装饰板固定比例，不做九宫格拉伸。背景等比 Cover 铺满。Logo 上中锚；加载底板、轨道、填充、花朵和两段文字放在同一个下中锚定组，设计底部留白 79 px。只适配组锚点，不能分别漂移。

工程现有参考画布 1080×1920。优先保持全局 CanvasScaler 不变，在 Loading 内统一换算：x/w 乘 1080/750，y/h 乘 1920/1334；或者建立等比 750 设计宽度子空间。不要为此改全项目 Canvas。接入已有安全区；长屏不得拉伸背景/底板，顶部避让系统胶囊。

## 字体

标题和标语是 AI 绘制的固定美术字，不对应可准确指定的现成字体。v2 动态中文/数字使用 **HuayangLoading SC Medium**：Source Han Sans SC Medium 的 OFL 1.1 子集，已重命名；字体及许可证在 `fonts/`，预览与引擎可使用同一字体。该子集包含本页正常/失败状态全部字符及 ASCII；增加文案时先扩充子集。不要覆盖 UIHome 的 HuayangUI 字体，不复制 macOS 字体进游戏，不依赖系统 fallback。原字体授权见 https://github.com/adobe-fonts/source-han-sans/blob/release/LICENSE.txt。

## 导入和图集

只导入 `assets/` 六张 PNG（或按需求选择可选图集，不能两套同时重复导入）。Sprite / Single / FullRect，Alpha Is Transparency=true，Mipmaps=false，Wrap=Clamp，Filter=Bilinear。背景 maxSize=2048；控件 maxSize=1024。

`atlas/loading-controls-1024.png` 为可选控件图集，不含背景，1024×1024；坐标在 `atlas/frames.json`。间距8px、不旋转、不裁边、外扩0px，须关闭 mipmaps。压缩平台最终打包建议由 Unity SpriteAtlas 以同样上限重新打包并设置2px外扩；未进行真机压缩验证。

## 检查文件

- `previews/assembled-000-750x1334.png`、`assembled-065-750x1334.png`、`assembled-100-750x1334.png`：实际素材拼装和真实进度。
- `previews/assembled-no-dynamic-text.png`：不包含运行时文字的拼装预览，固定 Logo 美术字保留。
- `previews/reference-vs-reassembled.png`：左原稿 / 右重组稿。
- `checks/alpha-light-dark.png`：浅底/深底透明轮廓检查；有色底检查图不可作为控件导入。
- `checks/asset-validation.json`：尺寸、Alpha像素和图集检查结果。
- `TUANJIE_HANDOFF.md`：交给团结 AI 的实施范围和验收要求。

本轮用户提供的源图与 v1 源图逐像素一致，复用已检查的图像切片，不重复生成美术。v2 补上可分发中文字体并重新制作预览。工程内拼装、编译和运行验收交给团结 AI 执行并回传证据；美术包检查通过不代表工程已完成。
