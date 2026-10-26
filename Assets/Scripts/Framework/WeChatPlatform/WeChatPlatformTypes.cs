using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

// ===================== 平台能力接口（业务层唯一依赖，禁止直接调 WX.*） =====================

/// <summary>生命周期：把平台前后台切换翻译为 EventCenter 事件（EventKeys.WXShow / WXHide）</summary>
public interface IPlatformLifecycle
{
    /// <summary>注册平台生命周期监听（GameLauncher 启动时调用一次）</summary>
    void Register();
}

/// <summary>
/// 本地存储：两端统一 PlayerPrefs——微信构建由 WX SDK 的 weapp-adapter 自动桥接
/// 到 wx.setStorageSync（单key与总容量约10MB，超限抛错；大存档请分key或上云）。
/// </summary>
public interface IPlatformStorage
{
    void Set(string key, string value);
    string Get(string key, string defaultValue = "");
    void Delete(string key);
    void Clear();
}

public class ShareContent
{
    public string Title;
    public string ImageUrl;
    public string Query;
}

public interface IPlatformShare
{
    /// <summary>主动拉起分享</summary>
    void Share(ShareContent content);

    /// <summary>配置被动分享（用户点右上角菜单转发时的默认内容）</summary>
    void EnablePassiveShare(ShareContent content);
}

public class LoginResult
{
    public bool Ok;
    /// <summary>微信code，交由服务端换取 openid/session_key</summary>
    public string Code;
    public string Error;
}

public class UserInfoResult
{
    public bool Ok;
    public string NickName;
    public string AvatarUrl;
    public string Error;
}

public interface IPlatformLogin
{
    UniTask<LoginResult> LoginAsync();

    /// <summary>注意：真机须在用户点击链路中调用（微信授权要求）</summary>
    UniTask<UserInfoResult> GetUserInfoAsync();
}

public class RewardVideoResult
{
    /// <summary>isEnded：完整看完，可发奖</summary>
    public bool Ok;
    /// <summary>广告是否成功拉起</summary>
    public bool Shown;
    public string Error;
}

public interface IPlatformAds
{
    /// <summary>展示激励视频：完整看完返回 Ok=true（发奖），中途关闭 Ok=false</summary>
    UniTask<RewardVideoResult> ShowRewardVideoAsync(string adUnitId);

    void ShowInterstitial(string adUnitId);

    /// <summary>展示 Banner（屏幕顶部坐标 top，水平居中）</summary>
    void ShowBanner(string adUnitId, int width, int height, int top);

    void HideBanner();
}

public interface IPlatformReport
{
    /// <summary>自定义事件上报（微信数据分析后台查看）</summary>
    void ReportEvent(string eventId, Dictionary<string, string> fields);

    /// <summary>监控指标上报</summary>
    void ReportMonitor(string name, double value);
}

/// <summary>
/// 系统信息：微信 UI 遮挡物（右上角胶囊按钮等）的避让信息。
/// 业务层经 WeChatService.SystemInfo 访问；编辑器 Mock 可模拟胶囊供避让演练。
/// </summary>
public interface IPlatformSystemInfo
{
    /// <summary>胶囊按钮矩形（Unity Screen 像素，左上原点 y 向下）；不可用返回 null</summary>
    Rect? GetMenuButtonRect();
}
