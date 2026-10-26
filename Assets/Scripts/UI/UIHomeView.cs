using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Cysharp.Threading.Tasks;

/// <summary>
/// 主页（Page 层语义，全屏背景阻断世界输入）· v4 品牌重设计「花样跳去」：
/// 品牌标题 / 双货币胶囊（金币←PlayerMgr.Coins、钻石←PlayerMgr.Shards，实时刷新）
/// / 2D 角色与蘑菇/花朵平台 / 开始游戏（体力闸门）/ 底部三圆钮（装扮/关卡/排行）
/// / 设置与礼物圆钮。顶部元素按微信胶囊按钮做安全区避让（GM `wx capsule on` 演练）。
/// 装扮/关卡/排行/礼物为预留接线位（序列化引用已留，业务接入时自行挂监听）。
/// </summary>
public class UIHomeView : UIPanel
{
    public Text coinsText;    // 金币值（编辑器工具接线）
    public Text diamondsText; // 钻石值（皮肤碎片，编辑器工具接线）
    public Button settingsBtn; // 设置（左上圆钮）
    public Button giftBtn;     // 礼物（右上圆钮，预留）
    public Button startBtn;    // 开始游戏（主按钮）
    public Button dressBtn;    // 装扮（底部圆钮，预留）
    public Button levelsBtn;   // 关卡（底部圆钮，预留）
    public Button rankBtn;     // 排行（底部圆钮，预留）

    // 胶囊避让：顶部元素原始 offset 缓存（重复 Apply 幂等可还原）
    private static readonly Dictionary<RectTransform, Vector2> CapsuleOffsets = new Dictionary<RectTransform, Vector2>();
    private static readonly string[] CapsuleTargets =
        { "SettingsBtn", "GiftBtn", "CoinCapsule", "DiamondCapsule" };
    private HomeCharacterStage _characterStage;
    private RawImage _characterPreview;
    private Image _staticCharacter;

    public override void OnCreate()
    {
        if (startBtn != null)
            startBtn.onClick.AddListener(OnStartClicked);
        if (settingsBtn != null)
            settingsBtn.onClick.AddListener(OnSettingsClicked);
    }

    private void OnSettingsClicked()
    {
        UIMgr.Instance.OpenPanel(UISettingsView.Key, UILayer.Page).Forget();
    }

    private void OnEnable()
    {
        EventCenter.AddListener(PlayerEvents.Changed, RefreshStatus);
        RefreshStatus();
        ApplyCapsuleAvoidance();
        EnsureCharacterPreview();
        if (_characterStage != null) _characterStage.SetVisible(true);
#if UNITY_EDITOR
        // 编辑器试玩：主页打开即免体力直入草稿关卡（SessionState 注入，不落正式资产）
        LevelRuntime.TryAutoStartPlaytest();
#endif
    }

    private void OnDisable()
    {
        if (_characterStage != null) _characterStage.SetVisible(false);
        EventCenter.RemoveListener(PlayerEvents.Changed, RefreshStatus);
    }

    protected override void OnDestroy()
    {
        if (_characterStage != null) _characterStage.Dispose();
        CapsuleOffsets.Clear(); // 面板单实例缓存，销毁即清静态引用
        base.OnDestroy();
    }

    private void EnsureCharacterPreview()
    {
        if (_characterStage != null) return;
        var container = transform.Find("Character") as RectTransform;
        if (container == null) return;
        _staticCharacter = container.GetComponent<Image>();
        var preview = new GameObject("AnimatedPawn", typeof(RectTransform), typeof(RawImage), typeof(AspectRatioFitter));
        preview.transform.SetParent(container, false);
        _characterPreview = preview.GetComponent<RawImage>();
        _characterPreview.raycastTarget = false;
        _characterPreview.enabled = false;
        var fit = preview.GetComponent<AspectRatioFitter>();
        fit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        fit.aspectRatio = 512f / 768f;
        _characterStage = HomeCharacterStage.Create();
    }

    private void Update()
    {
        if (_characterStage == null || !_characterStage.Ready || !_characterStage.HasCharacter || _characterPreview == null
            || _characterPreview.enabled) return;
        _characterPreview.texture = _characterStage.Texture;
        _characterPreview.enabled = true;
        if (_staticCharacter != null) _staticCharacter.enabled = false;
    }

    /// <summary>从 PlayerMgr 拉取最新玩家数据刷新货币栏（金币/钻石碎片）</summary>
    private void RefreshStatus()
    {
        var player = PlayerMgr.Instance;
        SetStatus(player.Coins.ToString(), player.Shards.ToString());
    }

    /// <summary>更新货币栏显示（传 null 跳过对应项）</summary>
    public void SetStatus(string coins, string diamonds)
    {
        if (coinsText != null && coins != null) coinsText.text = coins;
        if (diamondsText != null && diamonds != null) diamondsText.text = diamonds;
    }

    /// <summary>
    /// 微信胶囊避让：与胶囊水平重叠且顶边过高的顶部元素整体下移到胶囊底缘之下；
    /// 无胶囊（编辑器/未模拟）时还原原位。首次遇到元素缓存其原始 offset。
    /// </summary>
    private void ApplyCapsuleAvoidance()
    {
        Rect? capsule = WeChatService.SystemInfo?.GetMenuButtonRect();
        Canvas rootCanvas = GetComponentInParent<Canvas>()?.rootCanvas;
        float scaleFactor = rootCanvas != null ? rootCanvas.scaleFactor : 1f;
        if (scaleFactor <= 0f) scaleFactor = 1f;

        var corners = new Vector3[4];
        foreach (string target in CapsuleTargets)
        {
            if (!(transform.Find(target) is RectTransform t)) continue;
            if (!CapsuleOffsets.TryGetValue(t, out Vector2 original))
            {
                original = t.anchoredPosition;
                CapsuleOffsets[t] = original;
            }

            if (capsule == null)
            {
                t.anchoredPosition = original;
                continue;
            }

            t.GetWorldCorners(corners); // Overlay 画布：世界坐标即屏幕像素（y 自底向上）
            bool overlapX = corners[2].x >= capsule.Value.x && corners[0].x <= capsule.Value.xMax;
            float elemTopFromTop = Screen.height - corners[1].y;
            float shiftPx = capsule.Value.yMax + 24f * scaleFactor - elemTopFromTop;
            t.anchoredPosition = overlapX && shiftPx > 0f
                ? original - new Vector2(0f, shiftPx / scaleFactor)
                : original;
        }
    }

    private void OnStartClicked()
    {
        // 开局体力闸门：不足 1 点拦截（轻震动反馈），扣除成功才进入跳一跳
        if (!PlayerMgr.Instance.SpendEnergy(1))
        {
            WeChatService.Vibrate(false);
            Debug.Log("[UIHomeView] 体力不足，无法开始游戏");
            return;
        }
        JumpGame.StartGame();
    }
}
