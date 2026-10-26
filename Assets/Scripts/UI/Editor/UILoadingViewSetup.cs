using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 加载页资产一键生成（全幂等，可重复执行）· v2 无角色版美术包（uiloading-characterless-v2）：
/// - GameRes/Texture/Loading 6 张切片配置为 Sprite（Single、FullRect、Alpha 透明、无 mipmap、
///   Clamp、Bilinear、CompressedHQ q100；背景 max2048 / 控件 max1024，防 ASTC 默认档糊块）
/// - GameRes/Fonts/Loading/HuayangLoading-SC-Medium.otf（OFL 子集）为唯一文本字体，缺失即报错
/// - GameRes/Prefabs/UILoadingView.prefab：prefab 已存在时经 LoadPrefabContents 就地更新
///   （同路径保存保持资产 GUID，不删除重建）；子层级全量重建并重新接线。
/// 坐标唯一依据美术包 layout.json（750×1334 左上原点）换算工程 1080×1920 设计空间：
/// sx=1080/750=1.44，sy=1920/1334≈1.43928；固定尺寸父容器内子节点 y=（父中心y−子中心y）×sy。
/// 层级：Background（拉伸锚+Cover sizeDelta，运行时 UILoadingView.AdjustBackgroundCover 维护）
/// / Logo 上中锚 (0.5,1) / LoadingGroup 下中锚 (0.5,0)（底板/状态文本/轨道/填充/花心/百分比同组，
/// 仅组锚点随比例适配）。
/// 花朵指示器 pivot=(18/48, 1−18/36)=(0.375,0.5) 即花心锚点，沿填充内容线 x=135+390p（750 系）
/// 相对轨道中心滑移；0% 由 UILoadingView 隐藏。填充条 Image.Filled/Horizontal/Left 不得整体缩放。
/// 美术 PNG/OTF 需事先从美术包拷入（本工具不读工程外部路径）；生成路径均在 GameRes 下，
/// 编辑器模拟加载与微信 AB 构建同构（prefabs 包依赖 texture_loading 包，BundleLoader 自动预载）。
/// </summary>
public static class UILoadingViewSetup
{
    private const string TexDir = "Assets/GameRes/Texture/Loading";
    private const string FontDir = "Assets/GameRes/Fonts/Loading";
    private const string PrefabDir = "Assets/GameRes/Prefabs";
    private const string PrefabPath = PrefabDir + "/UILoadingView.prefab";
    private const string LegacyBackground = TexDir + "/loading_background.jpeg"; // v2 烘焙背景，旧版弃用

    // 切片 → 导入尺寸上限（layout.json：背景 2048，控件 1024）
    private static readonly string[] SliceFiles =
    {
        "loading_background_750x1334.png",
        "loading_logo_frame.png",
        "loading_panel_base.png",
        "loading_progress_track.png",
        "loading_progress_fill.png",
        "loading_progress_marker.png",
    };
    private const int BackgroundMaxSize = 2048;
    private const int ControlMaxSize = 1024;

    // layout.json runtime_text：状态/百分比 color #651F0D
    private static readonly Color TextColor = new Color(101f / 255f, 31f / 255f, 13f / 255f);

    private const string StatusNormalText = "正在进入花漾世界…";

    [MenuItem("LoadingUI/1. Create Assets")]
    public static void CreateAll()
    {
        EnsureDir(TexDir);
        EnsureDir(PrefabDir);

        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new System.InvalidOperationException("Stop Play before rebuilding loading");
        FloralSliceAssets.Load("loading");

        // ---------- 字体（OFL 子集，动态文案唯一字体，缺失即失败防系统回退） ----------
        Font font = AssetDatabase.LoadAssetAtPath<Font>($"{FontDir}/HuayangLoading-SC-Medium.otf");
        if (font == null)
        {
            Debug.LogError($"[UILoadingViewSetup] 缺少字体 {FontDir}/HuayangLoading-SC-Medium.otf（请先从美术包 fonts/ 拷入）");
            return;
        }
        AssetDatabase.SaveAssetIfDirty(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));

        // ---------- 加载页预制体（Top 层；已存在则就地更新保 GUID） ----------
        BuildOrUpdatePrefab(font);

        AssetDatabase.SaveAssetIfDirty(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
        AssetDatabase.Refresh();
        Debug.Log("[UILoadingViewSetup] 加载页资产就绪（v2 六切片 / HuayangLoading 字体 / UILoadingView.prefab 已更新）");
    }

    // ---------- 预制体构建（幂等：不存在则新建，存在则 LoadPrefabContents 就地更新） ----------

    private static void BuildOrUpdatePrefab(Font font)
    {
        GameObject root;
        bool isNew;
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (existing == null)
        {
            root = new GameObject("UILoadingView", typeof(RectTransform), typeof(UILoadingView));
            Stretch((RectTransform)root.transform);
            isNew = true;
        }
        else
        {
            root = PrefabUtility.LoadPrefabContents(PrefabPath); // 同路径保存 → 资产 GUID 不变
            isNew = false;
        }

        try
        {
            // 子层级全量重建（子节点皆本工具托管，无外部引用；根组件与序列化 PanelKey/Layer 原样保留）
            for (int i = root.transform.childCount - 1; i >= 0; i--)
                Object.DestroyImmediate(root.transform.GetChild(i).gameObject);

            // 背景：整屏插画，raycastTarget 保持 true 阻断一切点击（加载期间模态）；
            // 挂 Button 作失败态重试热区：正常态 interactable=false、transition=None（避免整屏被禁用色染色）。
            // 拉伸锚 + sizeDelta=(0,0.96) 即 1080×1920 下 Cover 尺寸，其余比例由运行时 Cover 逻辑重算。
            var background = AddImage((RectTransform)root.transform, "Background",
                LoadSprite($"{TexDir}/loading_background_750x1334.png"));
            Stretch(background);
            background.sizeDelta = new Vector2(0f, 0.96f);
            background.GetComponent<Image>().raycastTarget = true;
            var retryButton = background.gameObject.AddComponent<Button>();
            retryButton.transition = Button.Transition.None;
            retryButton.interactable = false;

            // Logo：固定美术字牌匾 606×306 @ (76,105)（中心 379,258）→ 上中锚 (0.5,1)
            // 偏移 ((379−375)×1.44, −258×1.43928)=(5.76,−371.34)，872.64×440.42
            var logo = AddImage((RectTransform)root.transform, "Logo",
                LoadSprite($"{TexDir}/loading_logo_frame.png"));
            SetAnchor(logo, 0.5f, 1f);
            logo.sizeDelta = new Vector2(872.64f, 440.42f);
            logo.anchoredPosition = new Vector2(5.76f, -371.34f);

            // 加载组：640×222 @ (52,1033)（中心 372,1144；组底 1255=1334−79 设计留白）
            // → 下中锚 (0.5,0) 偏移 ((372−375)×1.44, (1334−1144)×1.43928)=(−4.32,273.46)，921.6×319.52
            // 底板/轨道/填充/花心/文本同组，仅组锚点随比例适配，子节点不得分别漂移。
            var group = new GameObject("LoadingGroup", typeof(RectTransform)).transform;
            var groupRect = (RectTransform)group;
            groupRect.SetParent(root.transform, false);
            SetAnchor(groupRect, 0.5f, 0f);
            groupRect.sizeDelta = new Vector2(921.6f, 319.52f);
            groupRect.anchoredPosition = new Vector2(-4.32f, 273.46f);

            // 珍珠底板：无字无进度条的底卡 640×222，与组同尺寸居中
            var panel = AddImage(groupRect, "LoadingPanel",
                LoadSprite($"{TexDir}/loading_panel_base.png"));
            panel.sizeDelta = new Vector2(921.6f, 319.52f);
            FloralSliceAssets.Decorate(panel.GetComponent<Image>(),"loading","loading_panel_base",1.44f);
            panel.anchoredPosition = Vector2.zero;

            // 状态文本：rect (180,1112,390,36)（中心 375,1130）→ 组内 (4.32,+20.15)，561.6×51.81，27×sy≈39
            var statusText = AddText(groupRect, "StatusText", StatusNormalText,
                new Vector2(4.32f, 20.15f), new Vector2(561.6f, 51.81f), 39, TextColor, font);

            // 轨道：404×46 @ (128,1155)（中心 330,1178）→ 组内 (−60.48,−48.93)，581.76×66.21
            var track = AddImage(groupRect, "ProgressTrack",
                LoadSprite($"{TexDir}/loading_progress_track.png"));
            track.sizeDelta = new Vector2(581.76f, 66.21f);
            track.anchoredPosition = new Vector2(-60.48f, -48.93f);

            // 填充条：390×32 @ (135,1162)（中心 330,1178=轨道中心）→ 轨道内 (0,0)，561.6×46.05；
            // Filled 横向左起裁切（layout.json fill："no scaleX"）
            FloralSliceAssets.Decorate(track.GetComponent<Image>(),"loading","loading_progress_track",1.44f);
            var fill = AddImage(track, "ProgressFill",
                LoadSprite($"{TexDir}/loading_progress_fill.png"));
            fill.sizeDelta = new Vector2(561.6f, 46.05f);
            fill.anchoredPosition = Vector2.zero;
            var fillImage = fill.GetComponent<Image>();
            fillImage.type = Image.Type.Filled;
            fillImage.fillMethod = Image.FillMethod.Horizontal;
            fillImage.fillOrigin = (int)Image.OriginHorizontal.Left;
            fillImage.fillAmount = 0f;

            // 花朵指示器：48×36，pivot=花心 (18/48, 1−18/36)=(0.375,0.5)；花心沿内容线 x=135+390p（750 系）
            // 相对轨道中心滑移 (135−330)×1.44=−280.8 → (525−330)×1.44=+280.8；y=0（与轨道中心同线）。
            // 初始 0% 隐藏，运行时按进度显隐。
            var marker = AddImage(track, "ProgressMarker",
                LoadSprite($"{TexDir}/loading_progress_marker.png"));
            marker.pivot = new Vector2(0.375f, 0.5f);
            marker.sizeDelta = new Vector2(69.12f, 51.81f);
            marker.anchoredPosition = new Vector2(-280.8f, 0f);
            marker.gameObject.SetActive(false);

            // 百分比文本：rect (557,1161,66,36)（中心 590,1179）→ 组内 (313.92,−50.37)，95.04×51.81，26×sy≈37
            var percentText = AddText(groupRect, "PercentText", "0%",
                new Vector2(313.92f, -50.37f), new Vector2(95.04f, 51.81f), 37, TextColor, font);

            // ---------- 脚本接线 ----------
            var view = root.GetComponent<UILoadingView>();
            view.statusText = statusText;
            view.percentText = percentText;
            view.fillImage = fillImage;
            view.markerRect = marker;
            view.backgroundRect = background;
            view.logoRect = logo;
            view.retryButton = retryButton;
            view.markerXRange = new Vector2(-280.8f, 280.8f); // 0%→100%：花心=填充内容左缘→右缘

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath); // 同路径覆盖 → GUID 保持
            Debug.Log($"[UILoadingViewSetup] 已{(isNew ? "生成" : "就地更新")} {PrefabPath}");
        }
        finally
        {
            if (isNew) Object.DestroyImmediate(root);
            else PrefabUtility.UnloadPrefabContents(root);
        }
    }

    // ---------- 构建辅助 ----------

    /// <summary>配置切片为 Sprite 导入（FullRect/Clamp/Bilinear/CompressedHQ q100）；失败（文件缺失/导入失败）返回 null</summary>
    private static Sprite ConfigureSprite(string path, int maxSize)
    {
        if (!File.Exists(path)) return null;
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        if (importer == null) return null;
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.filterMode = FilterMode.Bilinear;
        importer.maxTextureSize = maxSize;
        importer.textureCompression = TextureImporterCompression.CompressedHQ; // 高质量压缩防 ASTC 默认档糊块
        importer.compressionQuality = 100;
        importer.SaveAndReimport();

        // 本引擎 TextureImporter 缺 spriteMeshType API（默认 Tight）：改写 meta 序列化值（0=FullRect）后强制重导，
        // 导入器按 meta 反序列化生效；珍珠/花朵等带透明边的控件禁 Tight 防图集裁切走样。
        string metaPath = path + ".meta";
        if (File.Exists(metaPath))
        {
            string meta = File.ReadAllText(metaPath);
            if (meta.Contains("spriteMeshType: 1"))
            {
                File.WriteAllText(metaPath, meta.Replace("spriteMeshType: 1", "spriteMeshType: 0"));
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            }
        }
        return LoadSprite(path);
    }

    private static Sprite LoadSprite(string path)
    {
        var key=Path.GetFileNameWithoutExtension(path);if(FloralSliceAssets.Has("loading",key))return FloralSliceAssets.Sprite("loading",key);
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    private static RectTransform AddImage(RectTransform parent, string name, Sprite sprite)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        var image = go.GetComponent<Image>();
        image.sprite = sprite;
        image.raycastTarget = false;
        return rect;
    }

    private static Text AddText(RectTransform parent, string name, string content,
        Vector2 pos, Vector2 size, int fontSize, Color color, Font font)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text));
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.sizeDelta = size;
        rect.anchoredPosition = pos;
        var text = go.GetComponent<Text>();
        text.text = content;
        text.font = font;
        text.fontSize = fontSize;
        text.color = color;
        text.alignment = TextAnchor.MiddleCenter; // layout.json runtime_text：居中
        text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        return text;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    /// <summary>点锚定（pivot 保持 0.5,0.5）：x 左=0/中=0.5/右=1，y 下=0/上=1；anchoredPosition 相对锚点</summary>
    private static void SetAnchor(RectTransform rect, float anchorX, float anchorY)
    {
        rect.anchorMin = new Vector2(anchorX, anchorY);
        rect.anchorMax = new Vector2(anchorX, anchorY);
    }

    private static void EnsureDir(string dir)
    {
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
    }
}
