using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 幸运转盘弹窗（Popup 层，全局遮罩阻断世界输入）：
/// 开盘即预掷奖励 → 盘面 3 秒缓动旋转落定对应格 → 发奖飘字 → 自动关闭。
/// 提前点遮罩关闭（OnClose）时若未发奖则照常补发——奖励永不丢失。
/// 奖池来自全局经济配置（LevelEconomyConfig：钻石 / 金币 / 通用碎片 / 稀有皮肤体验 / 本关金币翻倍）；
/// 金币在有限关卡走本关累计（结算入账，受"本关金币翻倍"覆盖），旧模式即时入账。
/// 关闭时通知 JumpGame 解除陷阱计时冻结（暂停蓄力、陷阱计时和关卡逻辑）。
/// </summary>
public class PanelLuckyWheel : UIPanel
{
    public sealed class RunContext
    {
        public readonly JumpGame Owner;
        public readonly int Version;
        public RunContext(JumpGame owner, int version) { Owner = owner; Version = version; }
        public bool IsValid => Owner != null && Owner.IsRunActive(Version);
    }
    private RunContext _run;
    public RectTransform wheelRect;   // 转盘图（旋转体）
    public Image pointerImage;         // 顶部指针（程序化金三角 sprite，OnCreate 生成）
    public Text resultText;           // 结果文案

    private int _resultIndex = -1;
    private bool _granted;
    private int _spinVersion;

    public override void OnCreate()
    {
        if (pointerImage != null) pointerImage.sprite = BuildPointerSprite();
    }

    public override void OnOpen(object param = null)
    {
        _run = param as RunContext;
        // Legacy callers also bind once, never resolve a different game at payout time.
        if (_run == null && JumpGame.Instance != null)
            _run = new RunContext(JumpGame.Instance, JumpGame.Instance.RunVersion);
        _granted = false;
        _resultIndex = RollResult();
        if (resultText != null) resultText.text = "";
        if (wheelRect != null) wheelRect.localRotation = Quaternion.identity;
        SpinAsync(++_spinVersion).Forget();
    }

    /// <summary>全局经济配置（缺失回退代码默认值；JumpGame 开局已预热）</summary>
    private static LevelEconomyConfig Economy
        => LevelRuntime.Economy != null ? LevelRuntime.Economy : LevelEconomyConfig.Fallback;

    /// <summary>按全局奖池权重预掷奖励格（盘面仅演出；提前关闭也不影响已定结果）</summary>
    private static int RollResult()
    {
        var cfg = Economy;
        if (cfg.wheelWeights == null || cfg.wheelWeights.Length == 0) return 0;
        int total = 0;
        foreach (int w in cfg.wheelWeights) total += w;
        if (total <= 0) return 0;
        int r = RandomUtil.Range(1, total + 1);
        for (int i = 0; i < cfg.wheelWeights.Length; i++)
        {
            if ((r -= cfg.wheelWeights[i]) <= 0) return i;
        }
        return 0;
    }

    /// <summary>3 秒缓动旋转（ease-out 三次曲线）：3 整圈后停在结果格中心（顶部指针处）</summary>
    private async UniTaskVoid SpinAsync(int version)
    {
        float segAngle = 360f / JumpConfig.WheelSegments;
        float target = 360f * 3f + segAngle * _resultIndex + segAngle * 0.5f;
        float t = 0f;
        while (t < JumpConfig.WheelSpinSeconds)
        {
            if (version != _spinVersion || wheelRect == null || _run == null || !_run.IsValid) return;
            if (!_run.Owner.UserPaused && !_run.Owner.AppPaused)
                t += Time.unscaledDeltaTime;
            float p = Mathf.Clamp01(t / JumpConfig.WheelSpinSeconds);
            float eased = 1f - Mathf.Pow(1f - p, 3f);
            wheelRect.localRotation = Quaternion.Euler(0f, 0f, -target * eased);
            await UniTask.Yield();
        }
        await WaitForResumeAsync(version);
        if (version != _spinVersion || wheelRect == null || _run == null || !_run.IsValid) return;
        wheelRect.localRotation = Quaternion.Euler(0f, 0f, -target);
        Grant();
        CloseAfterShow().Forget();
    }

    /// <summary>发放奖励（按全局奖池类型分发；金币走本关累计 / 旧模式即时入账）</summary>
    private void Grant()
    {
        if (_granted || _resultIndex < 0 || _run == null || !_run.IsValid) return;
        _granted = true;
        var cfg = Economy;
        int i = _resultIndex;
        int prizeKind = (cfg.wheelPrizeKinds != null && i < cfg.wheelPrizeKinds.Length)
            ? cfg.wheelPrizeKinds[i] : (int)WheelPrizeKind.Coins;
        int amount = (cfg.wheelAmounts != null && i < cfg.wheelAmounts.Length) ? cfg.wheelAmounts[i] : 0;
        string label = (cfg.wheelLabels != null && i < cfg.wheelLabels.Length) ? cfg.wheelLabels[i] : "";
        var game = _run.Owner;

        switch ((WheelPrizeKind)prizeKind)
        {
            case WheelPrizeKind.Diamonds:
                PlayerMgr.Instance.AddDiamonds(amount);
                EventCenter.Dispatch(JumpEvents.Toast, $"转盘奖励：钻石 +{amount}");
                break;
            case WheelPrizeKind.Coins:
                if (game != null && game.Plan != null)
                {
                    game.AddRunCoins(amount); // 本关累计：结算统一入账，受"本关金币翻倍"影响
                    EventCenter.Dispatch(JumpEvents.Toast, $"转盘奖励：金币 +{amount}");
                }
                else if (game != null)
                {
                    int coins = game.ApplyRunMult(amount); // 旧无限模式：即时入账
                    PlayerMgr.Instance.AddCoins(coins);
                    game.AddRunCoins(coins); // HUD total only; legacy wallet was credited exactly once above.
                    EventCenter.Dispatch(JumpEvents.Toast, $"转盘奖励：金币 +{coins}");
                }
                break;
            case WheelPrizeKind.Shards:
                PlayerMgr.Instance.AddShards(amount);
                EventCenter.Dispatch(JumpEvents.Toast, $"转盘奖励：通用碎片 +{amount}");
                break;
            case WheelPrizeKind.TrialSkin:
                // 随机稀有皮肤体验；已同款体验中则延长时长（转换金币路径依赖皮肤商店，暂休眠）
                int kindId = RandomUtil.Range(1, Mathf.Max(1, cfg.trialSkinKindCount) + 1);
                PlayerMgr.Instance.GrantTrialSkin(kindId, cfg.trialSkinHours);
                EventCenter.Dispatch(JumpEvents.Toast,
                    $"转盘奖励：稀有皮肤 #{kindId} 体验 {cfg.trialSkinHours} 小时");
                break;
            case WheelPrizeKind.CoinDouble:
                if (game != null)
                {
                    if (game.Plan != null) game.SetRunCoinDouble(); // 覆盖通关金币与全部金币砖（含触发前已得）
                    else game.SetRunRewardDoubled();                 // 旧模式：本局收益 ×2
                }
                break;
        }
        if (resultText != null) resultText.text = $"获得：{label}";
    }

    private async UniTaskVoid CloseAfterShow()
    {
        int version = _spinVersion;
        float elapsed = 0f;
        while (elapsed < .9f)
        {
            if (this == null || version != _spinVersion || _run == null || !_run.IsValid) return;
            if (!_run.Owner.UserPaused && !_run.Owner.AppPaused)
                elapsed += Time.unscaledDeltaTime;
            await UniTask.Yield();
        }
        await WaitForResumeAsync(version);
        if (this == null || version != _spinVersion || _run == null || !_run.IsValid) return;
        UIMgr.Instance.ClosePanel(JumpConfig.WheelPanelKey, true).Forget();
    }

    private async UniTask WaitForResumeAsync(int version)
    {
        while (this != null && version == _spinVersion && _run != null && _run.IsValid
            && (_run.Owner.UserPaused || _run.Owner.AppPaused))
            await UniTask.Yield();
    }

    public override void OnClose()
    {
        _spinVersion++;
        Grant(); // Same-run manual close grants once; abandoned runs must not reward the next run.
        if (_run != null && _run.IsValid)
            _run.Owner.NotifyWheelClosed();
        _run = null;
    }

    /// <summary>程序化金色指针三角形（透明底，免额外美术资产）</summary>
    private static Sprite BuildPointerSprite()
    {
        const int size = 64;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var rows = tex.GetPixels32();
        var clear = new Color32(0, 0, 0, 0);
        var gold = new Color32(255, 200, 90, 255);
        for (int i = 0; i < rows.Length; i++) rows[i] = clear;
        for (int y = 0; y < size; y++) // y=0 为尖端（Sprite 底部）→ 朝下三角
        {
            int half = Mathf.RoundToInt(22f * (y / (float)(size - 1)));
            int cx = size / 2;
            for (int x = cx - half; x <= cx + half; x++)
                rows[y * size + x] = gold;
        }
        tex.SetPixels32(rows);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 64f);
    }
}
