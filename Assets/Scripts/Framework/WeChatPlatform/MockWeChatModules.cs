using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 编辑器 Mock 模块集合：流程联调用；由 WeChatService 在非微信环境自动装载。
/// DebugConsole 的 wxmock GM 指令可触发模拟回调（模拟切前后台、改写广告结果等）。
/// </summary>
public class MockLifecycle : IPlatformLifecycle
{
    public void Register()
    {
        Debug.Log("[Mock] 生命周期已注册：编辑器内可用 GM 指令模拟切后台/前台");
    }

    /// <summary>GM：模拟切回前台（等同微信 OnShow）</summary>
    public static void SimulateShow() => EventCenter.Dispatch(EventKeys.WXShow);

    /// <summary>GM：模拟切到后台（等同微信 OnHide）</summary>
    public static void SimulateHide() => EventCenter.Dispatch(EventKeys.WXHide);
}

/// <summary>两端共用的 PlayerPrefs 存储（微信构建由 SDK 桥接 wx.setStorageSync）</summary>
public class PlayerPrefsStorage : IPlatformStorage
{
    public void Set(string key, string value) => PlayerPrefs.SetString(key, value);

    public string Get(string key, string defaultValue = "")
        => PlayerPrefs.HasKey(key) ? PlayerPrefs.GetString(key) : defaultValue;

    public void Delete(string key) => PlayerPrefs.DeleteKey(key);

    public void Clear() => PlayerPrefs.DeleteAll();
}

public class MockShare : IPlatformShare
{
    public static ShareContent LastContent;

    public void Share(ShareContent content)
    {
        LastContent = content;
        Debug.Log($"[Mock] 主动分享: {content?.Title} ({content?.Query})");
    }

    public void EnablePassiveShare(ShareContent content)
        => Debug.Log($"[Mock] 配置被动分享默认内容: {content?.Title}");
}

public class MockLogin : IPlatformLogin
{
    public async UniTask<LoginResult> LoginAsync()
    {
        await UniTask.Delay(100); // 模拟一次网络往返
        return new LoginResult { Ok = true, Code = "mock_code_" + RandomUtil.Range(100000, 999999) };
    }

    public async UniTask<UserInfoResult> GetUserInfoAsync()
    {
        await UniTask.Delay(100);
        return new UserInfoResult { Ok = true, NickName = "MockPlayer", AvatarUrl = "https://example.com/avatar.png" };
    }
}

public class MockAds : IPlatformAds
{
    /// <summary>GM 指令改写下一次激励视频结果（wxmock reward fail/ok），用完自动复位</summary>
    public static bool NextOk = true;

    public async UniTask<RewardVideoResult> ShowRewardVideoAsync(string adUnitId)
    {
        Debug.Log($"[Mock] 激励视频 {adUnitId}，本次结果 Ok={NextOk}");
        await UniTask.Delay(300); // 模拟观看时长
        bool ok = NextOk;
        NextOk = true;
        return new RewardVideoResult { Shown = true, Ok = ok };
    }

    public void ShowInterstitial(string adUnitId) => Debug.Log($"[Mock] 插屏 {adUnitId}");

    public void ShowBanner(string adUnitId, int width, int height, int top)
        => Debug.Log($"[Mock] Banner {adUnitId} {width}x{height}@top{top}");

    public void HideBanner() => Debug.Log("[Mock] 隐藏 Banner");
}

public class MockReport : IPlatformReport
{
    public static int EventCount;
    public static int MonitorCount;

    public void ReportEvent(string eventId, Dictionary<string, string> fields)
    {
        EventCount++;
        Debug.Log($"[Mock] 事件上报 {eventId}（{fields?.Count ?? 0} fields）");
    }

    public void ReportMonitor(string name, double value)
    {
        MonitorCount++;
        Debug.Log($"[Mock] 监控上报 {name}={value}");
    }
}

/// <summary>编辑器模拟微信胶囊按钮（GM `wx capsule on|off` 切换，验证主页顶部避让）</summary>
public class MockSystemInfo : IPlatformSystemInfo
{
    public static bool SimulateCapsule;

    public Rect? GetMenuButtonRect()
    {
        if (!SimulateCapsule) return null;
        // iPhone 经验值（逻辑像素）：胶囊 top≈48、高 32、右缘距屏 7、宽 87
        float scale = Screen.width / 375f;
        float w = 87f * scale;
        float h = 32f * scale;
        float top = 48f * scale;
        float left = Screen.width - 7f * scale - w;
        return new Rect(left, top, w, h); // Unity Screen 像素，左上原点 y 向下
    }
}
