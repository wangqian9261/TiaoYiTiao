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
/// 通用加载页（Top 层，全屏不透明背景阻断一切点击，模态）：
/// Logo 框（拱+环装饰，中心留官方 logo 位）/ 状态文本三态（加载中… N% · 加载失败 点击重试）/
/// 进度条（Image Filled 横向左起裁切）+ 蛋标（pivot=中心，沿填充内容线随进度滑移）。
/// SetProgress(0..1) 驱动填充/蛋标/百分比文本并解除失败态；SetFailed() 进入失败态开放点击重试
/// （派发 LoadingEvents.RetryRequested）。加载完成由调用方 ClosePanel。
/// </summary>
public class UILoadingView : UIPanel
{
    public Text statusText;         // 状态文本（编辑器工具接线）
    public Image fillImage;         // 填充条（Filled · Horizontal · Left，编辑器工具接线）
    public RectTransform markerRect; // 蛋标（编辑器工具接线）
    public Button retryButton;      // 重试热区（背景按钮，仅失败态 interactable，编辑器工具接线）

    // 蛋标 pivot x 的 0%→100% 滑移范围（相对轨道中心=填充内容左缘→右缘，750 设计稿换算 1080×1920）
    public Vector2 markerXRange = new Vector2(-366.48f, 365.04f);

    public float Progress { get; private set; }
    public bool IsFailed { get; private set; }

    private int _lastPercent = -1; // 百分比文本脏检查：驱动层每帧 SetProgress 只在实际变化时重建文本

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

    /// <summary>设置加载进度（0..1）：填充条裁切 + 蛋标滑移 + 百分比文本；同时解除失败态</summary>
    public void SetProgress(float progress)
    {
        Progress = Mathf.Clamp01(progress);
        IsFailed = false;
        if (fillImage != null) fillImage.fillAmount = Progress;
        if (markerRect != null)
            markerRect.anchoredPosition = new Vector2(
                Mathf.Lerp(markerXRange.x, markerXRange.y, Progress),
                markerRect.anchoredPosition.y);

        int percent = Mathf.RoundToInt(Progress * 100f); // manifest normal_format：round(clamp(p,0,1)*100)%
        if (percent != _lastPercent)
        {
            _lastPercent = percent;
            if (statusText != null) statusText.text = $"加载中… {percent}%";
        }
        if (retryButton != null && retryButton.interactable)
            retryButton.interactable = false;
    }

    /// <summary>进入加载失败态：提示点击重试并开放背景点击（点击派发 LoadingEvents.RetryRequested）</summary>
    public void SetFailed()
    {
        IsFailed = true;
        _lastPercent = -1;
        if (statusText != null) statusText.text = "加载失败 点击重试";
        if (retryButton != null) retryButton.interactable = true;
    }

    private void OnRetryClicked()
    {
        if (!IsFailed) return; // 仅失败态响应
        EventCenter.Dispatch(LoadingEvents.RetryRequested);
    }
}
