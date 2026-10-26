using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
#if UNITY_EDITOR || UNITY_WEBGL || WEIXINMINIGAME
using WeChatWASM;
#endif

/// <summary>
/// 真机实现：仅微信小游戏环境下由 WeChatService 装载，编辑器内永不实例化。
/// 编译目标裁剪：wx-runtime 系 DLL 仅覆盖 Editor / WebGL / WeixinMiniGame，
/// 因此整体包在本文件顶部宏内——其他目标（Standalone 等）不编译，保证跨目标可编译。
/// </summary>
#if UNITY_EDITOR || UNITY_WEBGL || WEIXINMINIGAME
public class WeChatLifecycle : IPlatformLifecycle
{
    private bool _registered;

    public void Register()
    {
        if (_registered) return;
        _registered = true;
        WX.OnShow(res => EventCenter.Dispatch(EventKeys.WXShow));
        WX.OnHide(res => EventCenter.Dispatch(EventKeys.WXHide));
        Debug.Log("[WeChatLifecycle] OnShow/OnHide 已接入 EventCenter");
    }
}

public class WeChatShare : IPlatformShare
{
    public void Share(ShareContent content)
    {
        WX.ShareAppMessage(new ShareAppMessageOption
        {
            title = content?.Title,
            imageUrl = content?.ImageUrl,
            query = content?.Query,
        });
    }

    public void EnablePassiveShare(ShareContent content)
    {
        WX.OnShareAppMessage(new WXShareAppMessageParam
        {
            title = content?.Title,
            imageUrl = content?.ImageUrl,
            query = content?.Query,
        });
    }
}

public class WeChatLogin : IPlatformLogin
{
    public UniTask<LoginResult> LoginAsync()
    {
        var tcs = new UniTaskCompletionSource<LoginResult>();
        WX.Login(new LoginOption
        {
            success = res => tcs.TrySetResult(new LoginResult
            {
                Ok = !string.IsNullOrEmpty(res?.code),
                Code = res?.code,
                Error = res?.errMsg,
            }),
            fail = err => tcs.TrySetResult(new LoginResult { Ok = false, Error = err?.errMsg }),
        });
        return tcs.Task;
    }

    public UniTask<UserInfoResult> GetUserInfoAsync()
    {
        var tcs = new UniTaskCompletionSource<UserInfoResult>();
        WX.GetUserInfo(new GetUserInfoOption
        {
            success = res => tcs.TrySetResult(new UserInfoResult
            {
                Ok = true,
                NickName = res?.userInfo?.nickName,
                AvatarUrl = res?.userInfo?.avatarUrl,
            }),
            fail = err => tcs.TrySetResult(new UserInfoResult { Ok = false, Error = err?.errMsg }),
        });
        return tcs.Task;
    }
}

public class WeChatAds : IPlatformAds
{
    private WXBannerAd _bannerAd;

    public async UniTask<RewardVideoResult> ShowRewardVideoAsync(string adUnitId)
    {
        var ad = WX.CreateRewardedVideoAd(new WXCreateRewardedVideoAdParam { adUnitId = adUnitId });
        var tcs = new UniTaskCompletionSource<RewardVideoResult>();
        Action<WXRewardedVideoAdOnCloseResponse> onClose = res =>
            tcs.TrySetResult(new RewardVideoResult { Shown = true, Ok = res != null && res.isEnded });

        ad.OnClose(onClose);
        ad.Show(
            res => Debug.Log("[WeChatAds] 激励视频拉起成功"),
            err =>
            {
                // 首次拉起失败通常为未加载完成：预载后重试一次
                ad.Load(
                    ok => ad.Show(_ => { }, e2 => tcs.TrySetResult(Failed(e2?.errMsg))),
                    e1 => tcs.TrySetResult(Failed(e1?.errMsg)));
            });

        var result = await tcs.Task;
        ad.OffClose(onClose);
        ad.Destroy();
        return result;
    }

    public void ShowInterstitial(string adUnitId)
    {
        var ad = WX.CreateInterstitialAd(new WXCreateInterstitialAdParam { adUnitId = adUnitId });
        ad.Show(
            res => Debug.Log("[WeChatAds] 插屏展示成功"),
            err => ad.Load(
                ok => ad.Show(_ => { }, e2 => Debug.LogWarning("[WeChatAds] 插屏展示失败: " + e2?.errMsg)),
                e1 => Debug.LogWarning("[WeChatAds] 插屏加载失败: " + e1?.errMsg)));
    }

    public void ShowBanner(string adUnitId, int width, int height, int top)
    {
        _bannerAd = WX.CreateBannerAd(new WXCreateBannerAdParam
        {
            adUnitId = adUnitId,
            style = new Style
            {
                left = Mathf.Max(0, (Screen.width - width) / 2),
                top = top,
                width = width,
                height = height,
            },
        });
        _bannerAd.Show(
            res => Debug.Log("[WeChatAds] Banner 展示成功"),
            err => Debug.LogWarning("[WeChatAds] Banner 展示失败: " + err?.errMsg));
    }

    public void HideBanner()
    {
        _bannerAd?.Hide();
    }

    private static RewardVideoResult Failed(string error)
        => new RewardVideoResult { Shown = false, Ok = false, Error = error };
}

public class WeChatReport : IPlatformReport
{
    public void ReportEvent(string eventId, Dictionary<string, string> fields)
        => WX.ReportEvent(eventId, fields ?? new Dictionary<string, string>());

    public void ReportMonitor(string name, double value)
        => WX.ReportMonitor(name, value);
}

/// <summary>胶囊按钮矩形（wx 逻辑像素 → Unity Screen 像素换算，业务层免单位换心）</summary>
public class WeChatSystemInfo : IPlatformSystemInfo
{
    public Rect? GetMenuButtonRect()
    {
        var r = WX.GetMenuButtonBoundingClientRect();
        if (r == null || r.width <= 0f || r.height <= 0f) return null;
        var sys = WX.GetSystemInfoSync();
        if (sys == null || sys.windowWidth <= 0) return null;

        double scale = Screen.width / (double)sys.windowWidth; // wx 逻辑像素 → Unity Screen 像素
        return new Rect(
            (float)(r.left * scale), (float)(r.top * scale),
            (float)(r.width * scale), (float)(r.height * scale));
    }
}
#endif
