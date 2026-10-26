using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 加载页事件 Key（业务模块自建常量类，点分风格对齐框架级 EventKeys）。
/// </summary>
public static class LoadingEvents
{
    /// <summary>加载失败后点击重试（UILoadingView 失败态点击背景派发，无参；由加载发起方订阅）</summary>
    public const string RetryRequested = "Loading.RetryRequested";
}

/// <summary>
/// 通用加载页（Top 层，全屏不透明背景阻断一切点击，模态）· v2 无角色版美术：
/// 背景 Cover 等比铺满 / Logo 上中（系统胶囊避让）/ 底部下中锚定加载组
/// （珍珠底板 + 状态文本 + 空轨道 + Filled 横向左起填充 + 花朵指示器 + 独立百分比文本）。
/// SetProgress(0..1) 驱动填充裁切、花心滑移与百分比；0% 隐藏花朵；失败态改写状态文案并
/// 开放背景点击重试（派发 LoadingEvents.RetryRequested）。加载完成由调用方 ClosePanel。
/// 文案字体 HuayangLoading-SC-Medium（OFL 子集，随 GameRes/Fonts/Loading 打包，勿用系统回退）。
/// </summary>
public class UILoadingView : UIPanel
{
    private const string StatusNormalText = "正在进入花漾世界…";
    private const string StatusFailedText = "加载失败，点击重试";

    public Text statusText;            // 状态文本（正常/失败两态，编辑器工具接线）
    public Text percentText;           // 独立百分比文本（N%，编辑器工具接线）
    public Image fillImage;            // 填充条（Filled · Horizontal · Left，编辑器工具接线）
    public RectTransform markerRect;  // 花朵指示器（pivot=花心，轨道子节点，编辑器工具接线）
    public RectTransform backgroundRect; // 背景（Cover 等比铺满，编辑器工具接线）
    public RectTransform logoRect;    // Logo（上中锚，胶囊避让目标，编辑器工具接线）
    public Button retryButton;        // 重试热区（背景按钮，仅失败态 interactable，编辑器工具接线）

    // 花朵花心（pivot）相对轨道中心 x 的 0%→100% 滑移范围（layout.json 750 系换算 1080×1920）：
    // 花心 x=135+390p，轨道中心 x=330 → (135−330)×1.44=−280.8 → (525−330)×1.44=+280.8
    public Vector2 markerXRange = new Vector2(-280.8f, 280.8f);

    public float Progress { get; private set; }
    public bool IsFailed { get; private set; }

    private int _lastPercent = -1;    // 百分比文本脏检查：驱动层每帧 SetProgress 只在实际变化时重建
    private Vector2 _logoAnchored;   // Logo 胶囊避让基准位（实例首次 OnEnable 缓存）
    private bool _logoCached;

    public override void OnCreate()
    {
        if (retryButton != null)
            retryButton.onClick.AddListener(OnRetryClicked);
    }

    public override void OnOpen(object param = null)
    {
        // 每次打开重置进度与失败态（缓存复用安全）；param 可传 float 作为初始进度
        SetProgress(param is float f ? f : 0f);
    }

    /// <summary>设置加载进度（0..1）：填充裁切 + 花心滑移 + 百分比文本；0% 隐藏花朵；同时解除失败态</summary>
    public void SetProgress(float progress)
    {
        Progress = Mathf.Clamp01(progress);
        IsFailed = false;

        if (fillImage != null) fillImage.fillAmount = Progress;
        if (markerRect != null)
        {
            // 0% 隐藏花朵：空轨道左缘不突出花朵，其余进度沿填充内容线跟随
            markerRect.gameObject.SetActive(Progress > 0f);
            markerRect.anchoredPosition = new Vector2(
                Mathf.Lerp(markerXRange.x, markerXRange.y, Progress),
                markerRect.anchoredPosition.y);
        }

        // 失败→正常即使进度同值也必须恢复文案：直接比较当前文本，不依赖百分比脏检查
        if (statusText != null && statusText.text != StatusNormalText)
            statusText.text = StatusNormalText;

        int percent = Mathf.RoundToInt(Progress * 100f); // layout.json：RoundToInt(Clamp01(p)*100) + "%"
        if (percent != _lastPercent)
        {
            _lastPercent = percent;
            if (percentText != null) percentText.text = $"{percent}%";
        }

        if (retryButton != null && retryButton.interactable)
            retryButton.interactable = false;
    }

    /// <summary>进入加载失败态：提示点击重试并开放背景点击（点击派发 LoadingEvents.RetryRequested）</summary>
    public void SetFailed()
    {
        IsFailed = true;
        _lastPercent = -1; // 复位脏检查：恢复后首个 SetProgress 必定重建百分比文本
        if (statusText != null) statusText.text = StatusFailedText;
        if (retryButton != null) retryButton.interactable = true;
    }

    private void OnRetryClicked()
    {
        if (!IsFailed) return; // 仅失败态响应
        EventCenter.Dispatch(LoadingEvents.RetryRequested);
    }

    // ---------- 自适应（比例 / 安全区） ----------

    private void OnEnable()
    {
        if (!_logoCached && logoRect != null)
        {
            _logoAnchored = logoRect.anchoredPosition;
            _logoCached = true;
        }
        AdjustBackgroundCover();
        ApplyCapsuleAvoidance();
    }

    private void OnRectTransformDimensionsChange()
    {
        AdjustBackgroundCover();
        ApplyCapsuleAvoidance();
    }

    /// <summary>
    /// 背景 750×1334 等比 Cover 铺满：两轴取缩放最大值，居中裁切溢出，绝不单轴拉伸。
    /// 背景为拉伸锚，sizeDelta=封面尺寸−画布尺寸（恒 ≥0）。
    /// </summary>
    private void AdjustBackgroundCover()
    {
        if (backgroundRect == null) return;
        var self = (RectTransform)transform;
        Vector2 size = self.rect.size; // 根节点拉伸=画布尺寸（设计单位）
        if (size.x <= 0f || size.y <= 0f) return;
        float scale = Mathf.Max(size.x / 750f, size.y / 1334f);
        backgroundRect.sizeDelta = new Vector2(750f, 1334f) * scale - size;
    }

    /// <summary>
    /// 微信胶囊避让（同 UIHomeView 口径）：与胶囊水平重叠且顶边过高的 Logo 整体下移到
    /// 胶囊底缘之下；无胶囊（编辑器/未模拟）时还原基准位。
    /// </summary>
    private void ApplyCapsuleAvoidance()
    {
        if (logoRect == null || !_logoCached) return;
        Rect? capsule = WeChatService.SystemInfo?.GetMenuButtonRect();
        Canvas rootCanvas = GetComponentInParent<Canvas>()?.rootCanvas;
        float scaleFactor = rootCanvas != null ? rootCanvas.scaleFactor : 1f;
        if (scaleFactor <= 0f) scaleFactor = 1f;

        if (capsule == null)
        {
            logoRect.anchoredPosition = _logoAnchored;
            return;
        }

        var corners = new Vector3[4];
        logoRect.GetWorldCorners(corners); // Overlay 画布：世界坐标即屏幕像素（y 自底向上）
        bool overlapX = corners[2].x >= capsule.Value.x && corners[0].x <= capsule.Value.xMax;
        float elemTopFromTop = Screen.height - corners[1].y;
        float shiftPx = capsule.Value.yMax + 24f * scaleFactor - elemTopFromTop;
        logoRect.anchoredPosition = overlapX && shiftPx > 0f
            ? _logoAnchored - new Vector2(0f, shiftPx / scaleFactor)
            : _logoAnchored;
    }
}
