using System;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Floral victory/failure overlay. View only: it never grants result coins.</summary>
public class PanelJumpResult : UIPanel
{
    [Serializable] public class Presentation
    {
        public GameObject root;
        public Text title, score, coins, status, retryLabel;
        public Button retry, home, revive;
    }
    public Presentation victory, defeat;
    public RectTransform panelRoot;
    public CanvasGroup reveal;
    public Image dim;
    public Canvas overlayCanvas;
    // Keep old setup-tool fields source compatible; active aliases are reassigned on every open.
    public Text titleText, scoreText, coinsText;
    [HideInInspector] public Text bestText, statsText, encourageText;
    public Button retryBtn, homeBtn, reviveBtn;
    Presentation _shown;
    bool _bound, _actionBusy;
    int _animationVersion;
    float _revealScale = 1f;

    public override void OnCreate()
    {
        if (_bound) return;
        _bound = true;
        foreach (var p in new[] { victory, defeat })
        {
            if (p == null) continue;
            if (p.retry != null) p.retry.onClick.AddListener(OnRetryClick);
            if (p.home != null) p.home.onClick.AddListener(OnHomeClick);
            if (p.revive != null) p.revive.onClick.AddListener(OnReviveClick);
        }
    }

    public override void OnOpen(object param = null)
    {
        var args = param as JumpResultArgs;
        bool cleared = args?.LevelCleared ?? false;
        _animationVersion++;
        _actionBusy = false;
        victory.root.SetActive(cleared);
        defeat.root.SetActive(!cleared);
        _shown = cleared ? victory : defeat;
        titleText = _shown.title; scoreText = _shown.score; coinsText = _shown.coins;
        retryBtn = _shown.retry; homeBtn = _shown.home; reviveBtn = defeat.revive;
        // The testable alias has the same activeSelf contract as its whole presentation.
        reviveBtn.gameObject.SetActive(!cleared);
        titleText.text = cleared ? "关卡完成" : "挑战失败";
        scoreText.text = Mathf.Max(0, args?.Score ?? 0).ToString();
        coinsText.text = "+" + Mathf.Max(0, args?.CoinsGained ?? 0);
        bool finalLevel = cleared && (args?.LevelId ?? 0) > 0 && string.IsNullOrEmpty(args.NextLevelName);
        _shown.retryLabel.text = cleared ? (finalLevel ? "再次挑战" : "下一关") : "重新开始";
        _shown.status.text = finalLevel ? "全部通关" : "";
        SetActions(true);
        // Page routing is unchanged, but the result must render and intercept above the Top-layer HUD.
        if (Application.isPlaying && overlayCanvas != null)
        {
            var top = UIMgr.Instance.GetLayerRoot(UILayer.Top);
            var topCanvas = top != null ? top.GetComponent<Canvas>() : null;
            // Unity clears this flag on a root Canvas when saving the prefab;
            // it must be re-enabled after the instance is nested under CanvasPage.
            overlayCanvas.overrideSorting = true;
            overlayCanvas.sortingOrder = (topCanvas != null ? topCanvas.sortingOrder : 400) + 10;
        }
        _revealScale = .96f;
        if (reveal != null) reveal.alpha = 0f;
        ApplyLayout();
    }

    public override UniTask OnOpenAnim() => Animate(true);
    public override UniTask OnCloseAnim() => Animate(false);
    async UniTask Animate(bool opening)
    {
        int version = ++_animationVersion;
        float fromAlpha = reveal != null ? reveal.alpha : 1f;
        float fromScale = _revealScale;
        float duration = opening ? .2f : .12f;
        for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
        {
            if (this == null || version != _animationVersion || !isActiveAndEnabled) return;
            float p = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / duration));
            if (reveal != null) reveal.alpha = Mathf.Lerp(fromAlpha, opening ? 1f : 0f, p);
            _revealScale = Mathf.Lerp(fromScale, opening ? 1f : .96f, p);
            ApplyLayout();
            await UniTask.Yield();
        }
        if (this == null || version != _animationVersion) return;
        if (reveal != null) reveal.alpha = opening ? 1f : 0f;
        _revealScale = opening ? 1f : .96f;
        ApplyLayout();
    }
    public override void OnClose() { _animationVersion++; }
    void OnDisable() { _animationVersion++; }
    void LateUpdate() { ApplyLayout(); }
    void ApplyLayout()
    {
        if (panelRoot == null || Screen.width <= 0 || Screen.height <= 0) return;
        var rect = ((RectTransform)transform).rect;
        if (rect.width <= 0 || rect.height <= 0) return;
        Rect safe = Screen.safeArea;
        float top = Screen.height - safe.yMax, bottom = safe.yMin;
        var capsule = WeChatService.SystemInfo?.GetMenuButtonRect();
        if (capsule.HasValue) top = Mathf.Max(top, capsule.Value.yMax + 8f);
        float left = safe.xMin / Screen.width * rect.width;
        float right = (Screen.width - safe.xMax) / Screen.width * rect.width;
        float topUi = top / Screen.height * rect.height, bottomUi = bottom / Screen.height * rect.height;
        float fit = Mathf.Min((rect.width-left-right)/750f, (rect.height-topUi-bottomUi)/1334f);
        panelRoot.localScale = Vector3.one * Mathf.Max(.01f, fit) * _revealScale;
        panelRoot.anchoredPosition = new Vector2((left-right)*.5f, (bottomUi-topUi)*.5f);
    }
    void SetActions(bool enabled)
    {
        if (_shown == null) return;
        _shown.retry.interactable = enabled;
        _shown.home.interactable = enabled;
        if (_shown.revive != null) _shown.revive.interactable = enabled;
    }
    void OnRetryClick()
    {
        if (_actionBusy || !isActiveAndEnabled) return;
        var game = JumpGame.Instance;
        if (game == null) { _shown.status.text = "请稍后再试"; return; }
        if (!PlayerMgr.Instance.SpendEnergy(1))
        {
            _shown.status.text = "体力不足，请稍后再试";
            WeChatService.Vibrate(false);
            return;
        }
        _actionBusy = true; SetActions(false);
        RestartAfterClose(game).Forget();
    }
    async UniTaskVoid RestartAfterClose(JumpGame game)
    {
        await UIMgr.Instance.ClosePanel(JumpConfig.ResultPanelKey, false);
        if (game != null && JumpGame.Instance == game) game.Restart();
    }
    void OnHomeClick()
    {
        if (_actionBusy || !isActiveAndEnabled) return;
        _actionBusy = true; SetActions(false);
        JumpGame.ExitToHome().Forget();
    }
    void OnReviveClick()
    {
        if (_actionBusy || !isActiveAndEnabled || _shown != defeat) return;
        // No approved revival gameplay or configured ad placement exists yet.
        // Never treat MockAds or a failed/cancelled advertisement as a resurrection.
        _shown.status.text = "复活暂不可用";
    }
}
