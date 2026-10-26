using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 音频管理：BGM 单通道（UniTask 淡入淡出，操作串行化防互相打断）+ SFX PlayOneShot 轮转池。
/// 独立音量、总静音。音频资产经 ResMgr 加载（key 如 "Audio/click"，微信构建下 AB/CDN 自动生效）。
/// WebGL/微信限制：用户首次触摸前不允许出声 —— 以 AudioListener.pause 门控，
/// 等待 InputMgr 派发的 EventKeys.FirstTouch 解锁并补播待播BGM；编辑器直接解锁。
/// 注意：SetMuted 只影响BGM音量与后续SFX，正在播的SFX会自然播完（时长通常很短）。
/// </summary>
public class AudioMgr : PureSingleton<AudioMgr>
{
    private const int SfxSourceCount = 6;
    private const float DefaultFade = 0.5f;

    private GameObject _root;
    private AudioSource _bgmSource;
    private readonly AudioSource[] _sfxSources = new AudioSource[SfxSourceCount];
    private int _sfxIndex;
    private string _currentBgmKey;
    private string _pendingBgmKey;
    // BGM 操作互斥：串行化 PlayBgm/StopBgm，防止并发淡入淡出互相写音量。
    // 注：不用 UniTask 链式返回（UniTask 单次消费语义，同一实例不能被多次 await）
    private bool _bgmBusy;
    // Envelope is independent of user gain, so a running fade cannot undo a volume/mute change.
    private float _bgmGain;

    public bool Unlocked { get; private set; }
    public float BgmVolume { get; private set; } = 0.8f;
    public float SfxVolume { get; private set; } = 1f;
    public bool Muted { get; private set; }
    public bool IsBgmPlaying => _bgmSource != null && _bgmSource.isPlaying;
    public string CurrentBgmKey => _currentBgmKey;

    protected override void OnInit()
    {
        BgmVolume = GamePreferences.MusicVolume;
        SfxVolume = GamePreferences.SoundVolume;
        _root = new GameObject("AudioRoot");
        Object.DontDestroyOnLoad(_root);

        _bgmSource = _root.AddComponent<AudioSource>();
        _bgmSource.playOnAwake = false;
        _bgmSource.loop = true;
        _bgmSource.volume = 0f;

        for (int i = 0; i < SfxSourceCount; i++)
        {
            var s = _root.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.loop = false;
            s.volume = SfxVolume;
            _sfxSources[i] = s;
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        // 微信小游戏：首触前门控全部音频，等待 FirstTouch 解锁
        AudioListener.pause = true;
        EventCenter.AddListener(EventKeys.FirstTouch, OnFirstTouch);
#else
        Unlocked = true;
#endif
    }

    private void OnFirstTouch()
    {
        if (Unlocked) return;
        Unlocked = true;
        AudioListener.pause = false;
        Debug.Log("[AudioMgr] 首次触摸：音频已解锁");
        if (!string.IsNullOrEmpty(_pendingBgmKey))
        {
            string key = _pendingBgmKey;
            _pendingBgmKey = null;
            PlayBgm(key).Forget();
        }
    }

    /// <summary>播放/切换 BGM（淡入淡出；未解锁时自动挂起待播；并发调用自动串行）</summary>
    public async UniTask PlayBgm(string key, float fade = DefaultFade)
    {
        if (!Unlocked)
        {
            _pendingBgmKey = key;
            Debug.Log($"[AudioMgr] 音频未解锁，BGM 待播: {key}");
            return;
        }
        await WaitForBgmFree();
        try
        {
            await PlayBgmInternalAsync(key, fade);
        }
        finally
        {
            _bgmBusy = false;
        }
    }

    /// <summary>停止 BGM（淡出；并发调用自动串行）</summary>
    public async UniTask StopBgm(float fade = DefaultFade)
    {
        if (_bgmSource == null || !_bgmSource.isPlaying) return;
        await WaitForBgmFree();
        try
        {
            await StopBgmInternalAsync(fade);
        }
        finally
        {
            _bgmBusy = false;
        }
    }

    private async UniTask WaitForBgmFree()
    {
        while (_bgmBusy) await UniTask.Yield();
        _bgmBusy = true;
    }

    private async UniTask PlayBgmInternalAsync(string key, float fade)
    {
        if (_currentBgmKey == key && _bgmSource.isPlaying)
            return;

        AudioClip clip = await ResMgr.Instance.LoadAssetAsync<AudioClip>(key);
        if (clip == null)
        {
            Debug.LogError($"[AudioMgr] BGM加载失败: {key}");
            return;
        }
        _currentBgmKey = key;

        // 淡出旧 BGM
        if (_bgmSource.isPlaying && fade > 0)
        {
            float start = _bgmGain;
            float t = 0;
            while (t < fade)
            {
                t += Time.unscaledDeltaTime;
                _bgmGain = Mathf.Lerp(start, 0f, Mathf.Clamp01(t / fade));
                ApplyBgmVolume();
                await UniTask.Yield();
            }
            _bgmSource.Stop();
        }

        _bgmSource.clip = clip;
        _bgmGain = 0f;
        ApplyBgmVolume();
        _bgmSource.Play();

        if (fade > 0)
        {
            float t = 0;
            while (t < fade)
            {
                t += Time.unscaledDeltaTime;
                _bgmGain = Mathf.Clamp01(t / fade);
                ApplyBgmVolume();
                await UniTask.Yield();
            }
        }
        _bgmGain = 1f;
        ApplyBgmVolume();
    }

    private async UniTask StopBgmInternalAsync(float fade)
    {
        if (!_bgmSource.isPlaying) return;
        float start = _bgmGain;
        float t = 0;
        while (t < fade && _bgmSource.isPlaying)
        {
            t += Time.unscaledDeltaTime;
            _bgmGain = Mathf.Lerp(start, 0f, Mathf.Clamp01(t / fade));
            ApplyBgmVolume();
            await UniTask.Yield();
        }
        _bgmSource.Stop();
        _bgmGain = 0f;
        ApplyBgmVolume();
        _currentBgmKey = null;
    }

    /// <summary>播放一次性音效（PlayOneShot，天然支持重叠播放）</summary>
    public void PlaySfx(string key, float volumeScale = 1f)
    {
        if (!Unlocked || Muted) return;
        PlaySfxAsync(key, volumeScale).Forget();
    }

    private async UniTask PlaySfxAsync(string key, float volumeScale)
    {
        AudioClip clip = await ResMgr.Instance.LoadAssetAsync<AudioClip>(key);
        if (clip == null)
        {
            Debug.LogError($"[AudioMgr] SFX加载失败: {key}");
            return;
        }
        // 轮转取源：PlayOneShot 不占用 clip 槽，仅作为 isPlaying 统计载体
        var src = _sfxSources[_sfxIndex];
        _sfxIndex = (_sfxIndex + 1) % SfxSourceCount;
        src.volume = Muted ? 0f : SfxVolume;
        src.PlayOneShot(clip, Mathf.Clamp01(volumeScale));
    }

    public void SetBgmVolume(float volume)
    {
        BgmVolume = GamePreferences.NormalizeVolume(volume,.8f);
        GamePreferences.MusicVolume = BgmVolume;
        ApplyBgmVolume();
    }

    public void SetSfxVolume(float volume)
    {
        SfxVolume = GamePreferences.NormalizeVolume(volume,1f);
        GamePreferences.SoundVolume = SfxVolume;
        foreach (var source in _sfxSources)
            if (source != null) source.volume = Muted ? 0f : SfxVolume;
    }

    public void SetMuted(bool muted)
    {
        Muted = muted;
        ApplyBgmVolume();
        foreach (var source in _sfxSources)
            if (source != null) source.volume = muted ? 0f : SfxVolume;
    }

    private void ApplyBgmVolume()
    {
        if (_bgmSource != null) _bgmSource.volume = Muted ? 0f : BgmVolume * _bgmGain;
    }
}
