using UnityEngine;

/// <summary>
/// 跳一跳→玩家数据桥：订阅单局结算事件，把得分自动累加进玩家积分（PlayerMgr）。
/// 纯事件驱动零侵入，不改动 JumpGame / JumpSaveData
/// （totalScore 为跳跳局内统计，积分为玩家货币，两者语义并存互不重构）。
/// </summary>
public static class JumpPlayerSync
{
    private static bool _subscribed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        if (_subscribed) return; // 事件只挂一次（跨播放防重复）
        _subscribed = true;
        EventCenter.AddListener<JumpResultArgs>(JumpEvents.Died, OnJumpDied);
    }

    private static void OnJumpDied(JumpResultArgs args)
    {
        if (args == null || args.Score <= 0) return;
        PlayerMgr.Instance.AddScore(args.Score);
        Debug.Log($"[JumpPlayerSync] 积分 +{args.Score} → {PlayerMgr.Instance.Score}");
    }
}
