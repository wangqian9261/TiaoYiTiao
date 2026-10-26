using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

public static class BattleEvents { public const string PauseRequested = "Battle.PauseRequested"; }

/// <summary>Transparent HUD; game owns movement, scoring and landing. Decorative Graphics never intercept touch.</summary>
public class UIBattleView : UIPanel
{
    public static UIBattleView Active { get; private set; }
    public RectTransform topHUD, bottomChargeGroup, trapHint, cancelZone, chargeNeedle, progressMarker;
    public Text levelNameText, scoreText, coinsText, progressText, instructionText, cancelText, trapText, bonusHintText;
    public Button pauseButton, resumeButton, restartButton, settingsButton, homeButton, settingsCloseButton;
    public Image progressFill, targetRing, timerRing, cancelBase;
    public CanvasGroup levelLabel, comboFeedback, perfectFeedback;
    public SpriteNumber comboDigits, perfectDigits;
    public GameObject chargeVisual, pauseOverlay, pauseMenu, settingsMenu;
    public Slider musicSlider, soundSlider;
    public Transform floatLayer;
    private readonly List<FloatHint> _floats = new List<FloatHint>();
    private bool _subscribed;
    private float _introElapsed, _comboElapsed = 99, _perfectElapsed = 99, _scorePulse = 99;
    private int _lastProgress = -1, _lastScore = -1, _lastCoins = -1, _lastLevel = -1;
    private Canvas _canvas;
    private Camera UICamera => _canvas != null && _canvas.renderMode != RenderMode.ScreenSpaceOverlay ? _canvas.worldCamera : null;

    public override void OnCreate()
    {
        pauseButton.onClick.AddListener(() => {
            var game = JumpGame.Instance;
            if (game == null || game.State == JumpState.Idle || game.State == JumpState.Over) return;
            game.SetPaused(true); settingsMenu.SetActive(false); pauseMenu.SetActive(true);
            EventCenter.Dispatch(BattleEvents.PauseRequested);
        });
        resumeButton.onClick.AddListener(() => JumpGame.Instance?.SetPaused(false));
        restartButton.onClick.AddListener(() => { ResetHud(); JumpGame.Instance?.Restart(); });
        homeButton.onClick.AddListener(() => JumpGame.ExitToHome().Forget());
        settingsButton.onClick.AddListener(() => {
            musicSlider.SetValueWithoutNotify(AudioMgr.Instance.BgmVolume);
            soundSlider.SetValueWithoutNotify(AudioMgr.Instance.SfxVolume);
            pauseMenu.SetActive(false); settingsMenu.SetActive(true);
        });
        settingsCloseButton.onClick.AddListener(() => { settingsMenu.SetActive(false); pauseMenu.SetActive(true); });
        musicSlider.onValueChanged.AddListener(value => AudioMgr.Instance.SetBgmVolume(value));
        soundSlider.onValueChanged.AddListener(value => AudioMgr.Instance.SetSfxVolume(value));
    }

    public override void OnOpen(object param = null)
    {
        Active = this; _canvas = GetComponentInParent<Canvas>()?.rootCanvas;
        if (!_subscribed)
        {
            EventCenter.AddListener<JumpScoreArgs>(JumpEvents.ScoreChanged, OnScoreChanged);
            EventCenter.AddListener<string>(JumpEvents.Toast, ShowToast);
            EventCenter.AddListener(JumpEvents.Started, ResetHud);
            EventCenter.AddListener<DragArgs>(EventKeys.Dragging, OnDragging);
            EventCenter.AddListener<DragArgs>(EventKeys.DragStart, OnChargeStarted);
            _subscribed = true;
        }
        ResetHud();
    }

    public override void OnClose()
    {
        if (Active == this) Active = null;
        if (_subscribed)
        {
            EventCenter.RemoveListener<JumpScoreArgs>(JumpEvents.ScoreChanged, OnScoreChanged);
            EventCenter.RemoveListener<string>(JumpEvents.Toast, ShowToast);
            EventCenter.RemoveListener(JumpEvents.Started, ResetHud);
            EventCenter.RemoveListener<DragArgs>(EventKeys.Dragging, OnDragging);
            EventCenter.RemoveListener<DragArgs>(EventKeys.DragStart, OnChargeStarted);
            _subscribed = false;
        }
        if (JumpGame.Instance != null && JumpGame.Instance.UserPaused) JumpGame.Instance.SetPaused(false);
        foreach (var hint in _floats) if (hint.Rect != null) hint.Rect.gameObject.SetActive(false);
    }
    protected override void OnDestroy() { OnClose(); base.OnDestroy(); }

    private void ResetHud()
    {
        _introElapsed = 0; _comboElapsed = _perfectElapsed = _scorePulse = 99;
        _lastProgress = _lastScore = _lastCoins = _lastLevel = -1;
        comboFeedback.alpha = perfectFeedback.alpha = 0;
        pauseOverlay.SetActive(false); settingsMenu.SetActive(false); pauseMenu.SetActive(true);
        chargeVisual.SetActive(false); cancelZone.gameObject.SetActive(false); trapHint.gameObject.SetActive(false);
        bonusHintText.gameObject.SetActive(false);
        foreach (var hint in _floats) if (hint.Rect != null) hint.Rect.gameObject.SetActive(false);
    }

    private void Update()
    {
        ApplySafeArea();
        var game = JumpGame.Instance;
        if (game == null) return;
        pauseOverlay.SetActive(game.UserPaused);
        float dt = game.TimeFrozen ? 0f : Time.deltaTime;
        _introElapsed += dt; _comboElapsed += dt; _perfectElapsed += dt; _scorePulse += dt;
        if (_lastLevel != game.CurrentLevel)
        {
            _lastLevel = game.CurrentLevel; _introElapsed = 0;
            string difficulty = game.Plan != null ? DifficultyProfiles.Get(game.Plan.Tier).DisplayName : "普通";
            levelNameText.text = "第" + game.CurrentLevel + "关 · " + difficulty;
        }
        levelLabel.alpha = Mathf.Lerp(1f, .35f, Mathf.Clamp01((_introElapsed - 5f) / .6f));
        if (_lastScore != game.Score) { _lastScore = game.Score; scoreText.text = game.Score + "分"; }
        if (_lastCoins != game.RunCoins) { _lastCoins = game.RunCoins; coinsText.text = "本关金币 " + game.RunCoins; }
        if (_lastProgress != game.LevelProgress)
        {
            _lastProgress = game.LevelProgress;
            float p = Mathf.Clamp01((float)game.LevelProgress / Mathf.Max(1, game.LevelTargetBlocks));
            progressFill.fillAmount = p; progressText.text = game.LevelProgress + "/" + game.LevelTargetBlocks;
            progressMarker.anchoredPosition = new Vector2(99f + 476f * p, -193f);
            progressMarker.gameObject.SetActive(p > 0);
        }
        bool charging = game.State == JumpState.Charging;
        chargeVisual.SetActive(charging); cancelZone.gameObject.SetActive(charging);
        instructionText.gameObject.SetActive(!charging);
        instructionText.text = game.State == JumpState.Ready ? "长按蓄力\n松手起跳" : "";
        if (charging)
        {
            chargeNeedle.localEulerAngles = new Vector3(0, 0, -game.ChargePhase * 360f);
            bool hasRange = game.TryGetChargeTarget(out Vector2 range);
            targetRing.gameObject.SetActive(hasRange);
            if (hasRange)
            {
                targetRing.fillAmount = range.y - range.x;
                targetRing.rectTransform.localEulerAngles = new Vector3(0, 0, -range.x * 360f);
                bool urgent = game.ChargeElapsed >= JumpConfig.ChargeCancelSeconds - 5f;
                targetRing.color = urgent && Mathf.Repeat(game.ChargeElapsed * 3f, 1f) < .5f ? new Color(1f, .35f, .35f) : Color.white;
            }
        }
        UpdateTrap(game, game.CollapseCountdown);
        bonusHintText.gameObject.SetActive(false);
        AnimateFeedback(comboFeedback, _comboElapsed, 1.25f); AnimateFeedback(perfectFeedback, _perfectElapsed, .9f);
        scoreText.transform.localScale = Vector3.one * (1f + .15f * Mathf.Sin(Mathf.Clamp01(_scorePulse / .3f) * Mathf.PI));
        foreach (var hint in _floats)
        {
            if (!hint.Rect.gameObject.activeSelf) continue;
            hint.Elapsed += dt; float p = hint.Elapsed / 1.1f;
            hint.Rect.anchoredPosition = hint.Start + Vector2.up * (50f * p);
            hint.Group.alpha = Mathf.Clamp01((1f - p) * 3f);
            if (p >= 1f) hint.Rect.gameObject.SetActive(false);
        }
    }

    private void ApplySafeArea()
    {
        var root = (RectTransform)transform;
        if (root.rect.width <= 0 || root.rect.height <= 0) return;
        float scale = root.rect.width / 750f;
        float unitsPerPixel = root.rect.height / Mathf.Max(1f, Screen.height);
        Rect safe = Screen.safeArea;
        float top = Mathf.Max(0, Screen.height - safe.yMax) * unitsPerPixel, bottom = Mathf.Max(0, safe.yMin) * unitsPerPixel;
        Rect? capsule = WeChatService.SystemInfo?.GetMenuButtonRect();
        if (capsule.HasValue) top = Mathf.Max(top, capsule.Value.yMax * unitsPerPixel + 10f * scale - 57f * scale);
        topHUD.localScale = bottomChargeGroup.localScale = Vector3.one * scale;
        topHUD.anchoredPosition = new Vector2(0, -top); bottomChargeGroup.anchoredPosition = new Vector2(0, bottom);
    }

    public bool IsInCancelZone(Vector2 screenPosition) => cancelZone != null && cancelZone.gameObject.activeInHierarchy &&
        RectTransformUtility.RectangleContainsScreenPoint(cancelZone, screenPosition, UICamera);
    private void OnChargeStarted(DragArgs args)
    {
        cancelBase.color = Color.white;
        cancelText.text = "取消";
    }
    private void OnDragging(DragArgs args)
    {
        bool inside = IsInCancelZone(args.Position);
        cancelBase.color = inside ? new Color(1f, .6f, .7f) : Color.white;
        cancelText.text = inside ? "松手取消" : "取消";
    }
    private void UpdateTrap(JumpGame game, float remaining)
    {
        var cam = Camera.main;
        if (remaining < 0 || cam == null) { trapHint.gameObject.SetActive(false); return; }
        Vector3 screen = cam.WorldToScreenPoint(game.EffectAnchor + Vector3.up * .65f);
        if (screen.z <= 0 || screen.x < 0 || screen.x > Screen.width || screen.y < 0 || screen.y > Screen.height)
        { trapHint.gameObject.SetActive(false); return; }
        trapHint.gameObject.SetActive(true);
        RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)transform, screen, UICamera, out Vector2 pos);
        trapHint.anchoredPosition = pos; trapHint.localScale = Vector3.one * (((RectTransform)transform).rect.width / 750f);
        trapText.text = Mathf.Max(0, remaining).ToString("0.0") + "s";
        timerRing.fillAmount = Mathf.Clamp01(remaining / Mathf.Max(.01f, game.EffectDuration));
    }
    private void OnScoreChanged(JumpScoreArgs args)
    {
        if (args.Gain <= 0) { ResetHud(); return; }
        _scorePulse = 0;
        if (args.Perfect)
        {
            perfectDigits.SetNumber(args.Gain); _perfectElapsed = 0;
            if (args.Combo >= 2) { comboDigits.SetNumber(args.Combo); _comboElapsed = 0; } else _comboElapsed = 99;
        }
        else { _comboElapsed = 99; ShowToast("+" + args.Gain); }
    }
    private static void AnimateFeedback(CanvasGroup group, float elapsed, float duration)
    {
        group.alpha = elapsed < duration ? Mathf.Clamp01((duration - elapsed) / .3f) : 0f;
        group.transform.localScale = Vector3.one * (elapsed < .18f ? Mathf.Lerp(.8f, 1.06f, elapsed / .18f) : 1f);
    }
    public void ShowToast(string message)
    {
        if (string.IsNullOrEmpty(message)) return;
        FloatHint entry = null;
        foreach (var hint in _floats) if (!hint.Rect.gameObject.activeSelf) { entry = hint; break; }
        if (entry == null)
        {
            var go = new GameObject("BattleToast", typeof(RectTransform), typeof(Text), typeof(CanvasGroup), typeof(Outline));
            go.transform.SetParent(floatLayer, false);
            var text = go.GetComponent<Text>(); text.font = levelNameText.font; text.fontSize = 32;
            text.alignment = TextAnchor.MiddleCenter; text.raycastTarget = false; text.color = new Color(.39f,.16f,.07f);
            var outline = go.GetComponent<Outline>(); outline.effectColor = new Color(1f,.97f,.9f); outline.effectDistance = Vector2.one * 2f;
            entry = new FloatHint {Rect=(RectTransform)go.transform,Text=text,Group=go.GetComponent<CanvasGroup>()};
            entry.Rect.sizeDelta = new Vector2(850,90); _floats.Add(entry);
        }
        entry.Elapsed = 0; entry.Text.text = message; entry.Group.alpha = 1;
        entry.Start = new Vector2(0, ((RectTransform)transform).rect.height * .15f);
        entry.Rect.anchoredPosition = entry.Start; entry.Rect.gameObject.SetActive(true);
    }
    private sealed class FloatHint { public RectTransform Rect; public Text Text; public CanvasGroup Group; public Vector2 Start; public float Elapsed; }
}
