using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 加载页资产一键生成（全幂等，可重复执行）· v3 切片版：
/// - GameRes/Texture/Loading 5 张 v3 切片配置为 Sprite（Single、Alpha 透明、无 mipmap）
/// - GameRes/Prefabs/UILoadingView.prefab（Top 层加载页：背景/Logo框/状态文本/轨道/填充/蛋标）
/// v2→v3 迁移：自动删除旧版烘焙背景 loading_background.jpeg（轨道已改回独立切片）。
/// 坐标由美术 loading_slices_manifest.json（750×1334 左上原点，v3）换算到工程 1080×1920
/// 设计空间：sx=1080/750=1.44，sy=1920/1334≈1.43928。
/// 锚点方案（自适应）：背景全屏拉伸；Logo 框锚上中 (0.5,1)（居中于系统胶囊区下方，
/// 中心留官方 logo 位——manifest 约定 no generated wordmark）；状态文本/轨道属底部内容带，
/// 锚定下中 (0.5,0)——不同比例下贴住底部云带（16:9 与设计稿像素重合）。
/// 状态文本=引擎 Text 三态（加载中… N% / 加载失败 点击重试），色 #4E2643、字号 26×1.44≈38；
/// 蛋标 pivot=(20,24) 于 40×48=正中心，滑移线=填充内容线（750系 x=121+508p，y=1073）。
/// 美术 PNG 需事先从切片目录拷入（本工具不读工程外部路径）；生成路径均在 GameRes 下，
/// 编辑器模拟加载与微信 AB 构建同构（prefabs 包依赖 texture_loading 包，BundleLoader 自动预载）。
/// </summary>
public static class UILoadingViewSetup
{
    private const string TexDir = "Assets/GameRes/Texture/Loading";
    private const string PrefabDir = "Assets/GameRes/Prefabs";
    private const string PrefabPath = PrefabDir + "/UILoadingView.prefab";
    private const string LegacyBackground = TexDir + "/loading_background.jpeg"; // v2 烘焙背景，v3 弃用

    private static readonly string[] SliceFiles =
    {
        "loading_background_750x1334.png",
        "loading_logo_frame.png",
        "loading_progress_track.png",
        "loading_progress_fill.png",
        "loading_progress_marker.png",
    };

    // 状态文本色（manifest runtime_text_layout color_rgba 78,38,67）
    private static readonly Color StatusColor = new Color(78f / 255f, 38f / 255f, 67f / 255f);

    [MenuItem("LoadingUI/1. Create Assets")]
    public static void CreateAll()
    {
        EnsureDir(TexDir);
        EnsureDir(PrefabDir);

        // v2→v3 迁移：删除旧烘焙背景（其轨道已改为独立切片叠加）
        if (AssetDatabase.LoadAssetAtPath<Object>(LegacyBackground) != null)
        {
            AssetDatabase.DeleteAsset(LegacyBackground);
            Debug.Log("[UILoadingViewSetup] 已删除 v2 旧背景 loading_background.jpeg（v3 改用独立轨道切片）");
        }

        AssetDatabase.Refresh();

        // ---------- 切片导入配置 ----------
        foreach (string file in SliceFiles)
        {
            if (ConfigureSprite($"{TexDir}/{file}") == null)
            {
                Debug.LogError($"[UILoadingViewSetup] 缺少切片 {TexDir}/{file}（请先从美术切片目录拷入 5 张 loading PNG 后重跑）");
                return;
            }
        }
        AssetDatabase.SaveAssets();

        // ---------- 加载页预制体（Top 层） ----------
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null)
        {
            var root = new GameObject("UILoadingView", typeof(RectTransform), typeof(UILoadingView));
            var rootRect = (RectTransform)root.transform;
            Stretch(rootRect);

            // 背景：整屏插画，raycastTarget 保持 true 阻断一切点击（加载期间模态）；
            // 挂 Button 作失败态重试热区：正常态 interactable=false、transition=None（避免整屏被禁用色染色）
            var background = AddImage(rootRect, "Background",
                LoadSprite($"{TexDir}/loading_background_750x1334.png"));
            Stretch(background);
            background.GetComponent<Image>().raycastTarget = true;
            var retryButton = background.gameObject.AddComponent<Button>();
            retryButton.transition = Button.Transition.None;
            retryButton.interactable = false;

            // Logo 框：拱+环装饰（中心留官方 logo 位）；(193,182) 364×152 → 上中锚 (0.5,1) 偏移 (0,-371.34)
            var logo = AddImage(rootRect, "LogoFrame",
                LoadSprite($"{TexDir}/loading_logo_frame.png"));
            SetAnchor(logo, 0.5f, 1f);
            logo.sizeDelta = new Vector2(524.16f, 218.77f);
            logo.anchoredPosition = new Vector2(0f, -371.34f);

            // 状态文本：box [150,966,600,1024]（中心 375,995）→ 下中锚 (0.5,0) 偏移 (0,487.92)，648×83.48
            var statusText = AddText(rootRect, "StatusText", "加载中… 0%",
                new Vector2(0f, 487.92f), new Vector2(648f, 83.48f), 38, StatusColor);
            SetAnchor((RectTransform)statusText.transform, 0.5f, 0f);

            // 轨道：(46,1022) 659×112 fixed（中心 375.5,1078）→ 下中锚 (0.5,0) 偏移 (0.72,368.86)，948.96×161.20
            var track = AddImage(rootRect, "ProgressTrack",
                LoadSprite($"{TexDir}/loading_progress_track.png"));
            SetAnchor(track, 0.5f, 0f);
            track.sizeDelta = new Vector2(948.96f, 161.20f);
            track.anchoredPosition = new Vector2(0.72f, 368.86f);

            // 填充条：PNG (116,1054) 518×38（内容区 508×28 含 5px 边距）→ 相对轨道 (-0.72,+7.20)，745.92×54.69
            // （填充中心 1073 高于轨道中心 1078 → y 取正；曾误写负致整体偏低 14.4px）
            var fill = AddImage(track, "ProgressFill",
                LoadSprite($"{TexDir}/loading_progress_fill.png"));
            fill.sizeDelta = new Vector2(745.92f, 54.69f);
            fill.anchoredPosition = new Vector2(-0.72f, 7.20f);
            var fillImage = fill.GetComponent<Image>();
            fillImage.type = Image.Type.Filled;                 // 横向左起裁切（manifest motion: clip_left_to_right）
            fillImage.fillMethod = Image.FillMethod.Horizontal;
            fillImage.fillOrigin = (int)Image.OriginHorizontal.Left;
            fillImage.fillAmount = 0f;

            // 蛋标：40×48、pivot=(20,24)=正中心；pivot 沿填充内容线 (121+508p,1073)（750系）
            // → 相对轨道 y=+7.20（与填充内容中心同线，高于轨道中心 5px），x 滑移范围 (-366.48,+365.04)
            var marker = AddImage(track, "ProgressMarker",
                LoadSprite($"{TexDir}/loading_progress_marker.png"));
            marker.sizeDelta = new Vector2(57.6f, 69.08f);
            marker.anchoredPosition = new Vector2(-366.48f, 7.20f);

            // ---------- 脚本接线 ----------
            var panel = root.GetComponent<UILoadingView>();
            panel.statusText = statusText;
            panel.fillImage = fillImage;
            panel.markerRect = marker;
            panel.retryButton = retryButton;
            panel.markerXRange = new Vector2(-366.48f, 365.04f); // 0%→100%：填充内容左缘→右缘

            SavePrefab(root, PrefabPath);
        }
        else
        {
            Debug.Log("[UILoadingViewSetup] 预制体已存在，跳过（幂等）");
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[UILoadingViewSetup] 加载页资产就绪（v3 切片配置 / UILoadingView.prefab）");
    }

    // ---------- 构建辅助 ----------

    /// <summary>配置切片为 Sprite 导入；失败（文件缺失/导入失败）返回 null</summary>
    private static Sprite ConfigureSprite(string path)
    {
        if (!File.Exists(path)) return null;
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        if (importer == null) return null;
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.maxTextureSize = 2048;
        importer.SaveAndReimport();
        return LoadSprite(path);
    }

    private static Sprite LoadSprite(string path)
    {
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
        Vector2 pos, Vector2 size, int fontSize, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text));
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.sizeDelta = size;
        rect.anchoredPosition = pos;
        var text = go.GetComponent<Text>();
        text.text = content;
        text.font = GetFont();
        text.fontSize = fontSize;
        text.color = color;
        text.alignment = TextAnchor.MiddleCenter; // manifest alignment: center
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

    private static void SavePrefab(GameObject root, string path)
    {
        PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
        Debug.Log($"[UILoadingViewSetup] 已生成 {path}");
    }

    private static void EnsureDir(string dir)
    {
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
    }

    private static Font GetFont()
    {
        try { return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); }
        catch { return Resources.GetBuiltinResource<Font>("Arial.ttf"); }
    }
}
