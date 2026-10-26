using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 跳一跳音频包装：开局探测可用 key（AI 音效尚未生成时静默跳过，避免每次播放刷加载错误）；
/// BGM 缺失时沿用现有 bgm_loop。播放本身仍走 AudioMgr。
/// </summary>
public static class JumpAudio
{
    private static readonly HashSet<string> _available = new HashSet<string>();
    private static readonly HashSet<string> _warned = new HashSet<string>();
    private static bool _probed;

    /// <summary>探测全部游戏音频 key 是否存在（幂等，只探一次）</summary>
    public static async UniTask ProbeAsync()
    {
        if (_probed) return;
        _probed = true;
        string[] keys =
        {
            JumpConfig.SfxLand, JumpConfig.SfxFly, JumpConfig.SfxPerfect,
            JumpConfig.SfxBonus, JumpConfig.SfxFall, JumpConfig.BgmKey
        };
        foreach (string key in keys)
        {
            var clip = await ResMgr.Instance.LoadAssetAsync<AudioClip>(key);
            if (clip == null) continue;
            _available.Add(key);
            ResMgr.Instance.ReleaseAsset(key); // 探测即放；正式播放走 AudioMgr 自身的加载与缓存
        }
        Debug.Log($"[JumpAudio] 可用音频: {(_available.Count == 0 ? "（无，全部静默跳过）" : string.Join(",", _available))}");
    }

    public static void Play(string key, float volumeScale = 1f)
    {
        if (!_available.Contains(key))
        {
            if (_warned.Add(key))
                Debug.LogWarning($"[JumpAudio] 音频缺失，已静默跳过: {key}（AI 音效未生成？）");
            return;
        }
        AudioMgr.Instance.PlaySfx(key, volumeScale);
    }

    /// <summary>开局切 BGM；bgm_jump 未生成时沿用现有 BGM</summary>
    public static async UniTask SwitchBgmAsync()
    {
        if (!_available.Contains(JumpConfig.BgmKey))
        {
            Debug.LogWarning("[JumpAudio] bgm_jump 未生成，沿用 bgm_loop");
            return;
        }
        await AudioMgr.Instance.PlayBgm(JumpConfig.BgmKey);
    }
}
