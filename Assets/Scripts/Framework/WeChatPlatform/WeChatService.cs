using UnityEngine;
#if UNITY_EDITOR || UNITY_WEBGL || WEIXINMINIGAME
using WeChatWASM;
#endif

/// <summary>
/// 微信平台统一门面：业务层只经由它访问平台能力，禁止直接调 WX.*。
/// 编辑器自动装载 Mock；微信小游戏构建装载真实实现。
/// 真机实现按宏裁剪编译（wx-runtime DLL 仅覆盖 Editor/WebGL/WeixinMiniGame），
/// 其他目标（Standalone 等）只保留 Mock，保证全目标可编译。
/// </summary>
public static class WeChatService
{
    // 微信能力可用的编译目标（编辑器=editor DLL；WebGL/微信小游戏=runtime DLL）
#if UNITY_EDITOR || UNITY_WEBGL || WEIXINMINIGAME
    private const bool WeChatCompiled = true;
#else
    private const bool WeChatCompiled = false;
#endif

    /// <summary>是否微信小游戏环境（编辑器=false；微信构建即 WebGL 目标=true）</summary>
    public static bool IsWeChatEnv
    {
        get
        {
#if UNITY_EDITOR
            return false;
#elif UNITY_WEBGL || WEIXINMINIGAME
            return true;
#else
            return false;
#endif
        }
    }

    public static IPlatformLifecycle Lifecycle { get; private set; }
    public static IPlatformStorage Storage { get; private set; }
    public static IPlatformShare Share { get; private set; }
    public static IPlatformLogin Login { get; private set; }
    public static IPlatformAds Ads { get; private set; }
    public static IPlatformReport Report { get; private set; }
    public static IPlatformSystemInfo SystemInfo { get; private set; }

    private static bool _inited;

    /// <summary>GameLauncher 启动时调用（幂等）</summary>
    public static void Init()
    {
        if (_inited) return;
        _inited = true;

#if UNITY_EDITOR || UNITY_WEBGL || WEIXINMINIGAME
        if (IsWeChatEnv)
        {
            Lifecycle = new WeChatLifecycle();
            Share = new WeChatShare();
            Login = new WeChatLogin();
            Ads = new WeChatAds();
            Report = new WeChatReport();
            SystemInfo = new WeChatSystemInfo();
            Debug.Log("[WeChatService] 微信小游戏环境：已装载真实平台模块");
        }
        else
#endif
        {
            Lifecycle = new MockLifecycle();
            Share = new MockShare();
            Login = new MockLogin();
            Ads = new MockAds();
            Report = new MockReport();
            SystemInfo = new MockSystemInfo();
            Debug.Log("[WeChatService] 编辑器环境：已装载 Mock 平台模块（DebugConsole 的 wxmock 指令可模拟微信回调）");
        }

        // 存储两端同源：PlayerPrefs（微信构建由 SDK 自动桥接 wx.setStorageSync）
        Storage = new PlayerPrefsStorage();
        Lifecycle.Register();
    }

    /// <summary>微信本地存储占用报告（DebugConsole GM 用）</summary>
    public static string GetStorageReport()
    {
#if UNITY_EDITOR || UNITY_WEBGL || WEIXINMINIGAME
        if (IsWeChatEnv)
        {
            var info = WX.GetStorageInfoSync();
            return $"微信本地存储: {info?.keys?.Length ?? 0} keys, {info?.currentSize ?? 0}/{info?.limitSize ?? 0} KB";
        }
#endif
        return "编辑器/非微信目标：存档走 PlayerPrefs";
    }

    /// <summary>震动反馈（仅微信小游戏环境有实际效果）</summary>
    public static void Vibrate(bool longVibration = false)
    {
        if (!GamePreferences.VibrationEnabled) return;
#if UNITY_WEBGL || WEIXINMINIGAME
        if (!IsWeChatEnv) return;
        if (longVibration) WX.VibrateLong(new VibrateLongOption());
        else WX.VibrateShort(new VibrateShortOption());
#endif
    }
}
