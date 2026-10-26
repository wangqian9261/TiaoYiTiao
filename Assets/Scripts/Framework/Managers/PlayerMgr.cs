using System;
using UnityEngine;

/// <summary>
/// 玩家基础数据管理：头像id / 金币 / 积分 / 体力。
/// 存档经 DataMgr XOR 混淆落盘（key: player_save），任一变更即时保存并派发 PlayerEvents.Changed。
/// 体力规则：开局经 UI 闸门扣点（SpendEnergy），按真实墙钟自动恢复（RegenSecondsPerPoint 一点，
/// 离线/切后台同样补点，上限 MaxEnergy 封顶）；每秒巡检，只有实际到点才写盘/派发。
/// 注意：OnInit 依赖 DataMgr 与 MonoDriver（GameLauncher 已保证先于本类初始化）。
/// </summary>
public class PlayerMgr : PureSingleton<PlayerMgr>
{
    /// <summary>体力上限</summary>
    public const int MaxEnergy = 5;
    /// <summary>每恢复 1 点体力的真实秒数</summary>
    public const int RegenSecondsPerPoint = 300;

    private PlayerSaveData _data;

    // ---------- 数据属性 ----------

    public int AvatarId => _data.avatarId;
    public int Coins => _data.coins;
    public long Score => _data.score;
    public int Energy => _data.energy;
    public int EnergyMax => MaxEnergy;
    public int Shards => _data.shards;
    public int Diamonds => _data.diamonds;

    /// <summary>体验皮肤剩余秒（无/过期返回 0）</summary>
    public long TrialSkinRemainSeconds
    {
        get
        {
            if (_data == null || _data.trialSkinKindId <= 0) return 0;
            long remain = _data.trialSkinExpiresUtc - NowUtc;
            return remain > 0 ? remain : 0;
        }
    }
    public bool HasActiveTrialSkin => TrialSkinRemainSeconds > 0;

    /// <summary>距下次恢复体力还需的真实秒数（已满返回 0；调试显示用）</summary>
    public long EnergyRegenCountdown
    {
        get
        {
            if (_data == null || _data.energy >= MaxEnergy) return 0;
            long remain = RegenSecondsPerPoint - (NowUtc - _data.lastRegenUtc);
            return remain > 0 ? remain : 0;
        }
    }

    protected override void OnInit()
    {
        _data = DataMgr.Instance.Load<PlayerSaveData>(PlayerSaveData.SaveKey, true) ?? NewDefaultData();
        EnsureRegen(); // 离线补点（关机时间照常折算）
        MonoDriver.Instance.Interval(1f, EnsureRegen, true);
    }

    // ---------- 变更 API ----------

    public void SetAvatar(int avatarId)
    {
        if (_data == null || avatarId == _data.avatarId) return;
        _data.avatarId = avatarId;
        Apply();
    }

    /// <summary>金币增减（负数=扣减，clamp ≥0）</summary>
    public void AddCoins(int delta)
    {
        if (_data == null || delta == 0) return;
        long v = Math.Max(0, (long)_data.coins + delta);
        int clamped = (int)Math.Min(int.MaxValue, v);
        if (clamped == _data.coins) return;
        _data.coins = clamped;
        Apply();
    }

    /// <summary>积分累加（单局结算入口；负数可扣回，clamp ≥0）</summary>
    public void AddScore(long delta)
    {
        if (_data == null || delta == 0) return;
        long v = Math.Max(0, _data.score + delta);
        if (v == _data.score) return;
        _data.score = v;
        Apply();
    }

    /// <summary>体力增减（clamp 0..Max；加满时重置恢复基准）</summary>
    public void AddEnergy(int delta)
    {
        if (_data == null || delta == 0) return;
        int v = Mathf.Clamp(_data.energy + delta, 0, MaxEnergy);
        if (v == _data.energy) return;
        _data.energy = v;
        if (_data.energy >= MaxEnergy) _data.lastRegenUtc = NowUtc;
        Apply();
    }

    /// <summary>皮肤碎片累加（砖上物品获取，负数可扣回，clamp ≥0）</summary>
    public void AddShards(int delta)
    {
        if (_data == null || delta == 0) return;
        int v = Mathf.Max(0, _data.shards + delta);
        if (v == _data.shards) return;
        _data.shards = v;
        Apply();
    }

    /// <summary>钻石增减（转盘奖励；负数=扣减，clamp ≥0）</summary>
    public void AddDiamonds(int delta)
    {
        if (_data == null || delta == 0) return;
        long v = Math.Max(0, (long)_data.diamonds + delta);
        int clamped = (int)Math.Min(int.MaxValue, v);
        if (clamped == _data.diamonds) return;
        _data.diamonds = clamped;
        Apply();
    }

    /// <summary>
    /// 体验皮肤：同款有效期内延长 hours 小时；否则授予新档位（转盘"随机稀有皮肤体验 24 小时"）。
    /// "已永久拥有时转换为金币"路径依赖皮肤商店（未就绪，休眠）。
    /// </summary>
    public void GrantTrialSkin(int kindId, int hours)
    {
        if (_data == null || kindId <= 0 || hours <= 0) return;
        long add = hours * 3600L;
        if (_data.trialSkinKindId == kindId && NowUtc < _data.trialSkinExpiresUtc)
            _data.trialSkinExpiresUtc += add; // 已有体验时间则延长
        else
        {
            _data.trialSkinKindId = kindId;
            _data.trialSkinExpiresUtc = NowUtc + add;
        }
        Apply();
    }

    /// <summary>消耗体力：不足返回 false（调用方拦截开局）；满→未满瞬间起算恢复计时</summary>
    public bool SpendEnergy(int cost)
    {
        if (_data == null) return false;
        if (cost <= 0) return true;
        if (_data.energy < cost) return false;
        bool wasFull = _data.energy >= MaxEnergy;
        _data.energy -= cost;
        if (wasFull) _data.lastRegenUtc = NowUtc;
        Apply();
        return true;
    }

    /// <summary>清档重置为默认值</summary>
    public void ResetAll()
    {
        DataMgr.Instance.Delete(PlayerSaveData.SaveKey);
        _data = NewDefaultData();
        Apply();
    }

    // ---------- 体力恢复 ----------

    private void EnsureRegen()
    {
        if (_data == null || _data.energy >= MaxEnergy) return;
        long elapsed = NowUtc - _data.lastRegenUtc;
        if (elapsed < RegenSecondsPerPoint) return;
        long gained = elapsed / RegenSecondsPerPoint;
        _data.energy = (int)Math.Min(MaxEnergy, _data.energy + gained);
        if (_data.energy < MaxEnergy)
            _data.lastRegenUtc += gained * RegenSecondsPerPoint; // 保留余量继续计
        else
            _data.lastRegenUtc = NowUtc;
        Apply();
        Debug.Log($"[PlayerMgr] 体力恢复 +{gained} → {_data.energy}/{MaxEnergy}");
    }

    /// <summary>调试：把恢复基准回拨 seconds 秒并立即巡检（验证离线补点）</summary>
    private void RewindRegen(long seconds)
    {
        if (_data == null || seconds <= 0) return;
        _data.lastRegenUtc -= seconds;
        EnsureRegen();
    }

    private void Apply()
    {
        DataMgr.Instance.Save(PlayerSaveData.SaveKey, _data, true);
        EventCenter.Dispatch(PlayerEvents.Changed);
    }

    private static PlayerSaveData NewDefaultData()
        => new PlayerSaveData
        {
            avatarId = 0,
            coins = 0,
            score = 0,
            energy = MaxEnergy,
            lastRegenUtc = NowUtc,
        };

    private static long NowUtc => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    // ---------- GM 指令 ----------

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void RegisterGm()
    {
        DebugConsoleMgr.RegisterCommand("player",
            "player [info]|coins <n>|score <n>|energy <n>|shards <n>|diamonds <n>|avatar <n>|rewind <秒>|reset（玩家数据调试）",
            OnGmCommand);
    }

    private static void OnGmCommand(string args)
    {
        var m = PlayerMgr.Instance;
        if (m._data == null)
        {
            Debug.LogWarning("[PlayerGM] PlayerMgr 尚未初始化");
            return;
        }

        string[] parts = (args ?? "").Split(' ');
        string cmd = parts.Length > 0 ? parts[0].ToLowerInvariant() : "";
        switch (cmd)
        {
            case "":
            case "info":
                Debug.Log($"[PlayerGM] 头像 {m.AvatarId} · 金币 {m.Coins} · 积分 {m.Score} · 碎片 {m.Shards} · 钻石 {m.Diamonds} · 体力 {m.Energy}/{m.EnergyMax}"
                    + (m.HasActiveTrialSkin ? $" · 体验皮肤#{m._data.trialSkinKindId} 剩 {m.TrialSkinRemainSeconds / 60} 分钟" : "")
                    + (m.Energy < m.EnergyMax ? $"（{m.EnergyRegenCountdown}s 后恢复 1 点）" : "（已满）"));
                break;
            case "coins":
                if (parts.Length > 1 && int.TryParse(parts[1], out int c)) m.AddCoins(c - m.Coins);
                else Debug.LogWarning("[PlayerGM] 用法: player coins <n>");
                break;
            case "score":
                if (parts.Length > 1 && long.TryParse(parts[1], out long s)) m.AddScore(s - m.Score);
                else Debug.LogWarning("[PlayerGM] 用法: player score <n>");
                break;
            case "energy":
                if (parts.Length > 1 && int.TryParse(parts[1], out int e)) m.AddEnergy(e - m.Energy);
                else Debug.LogWarning("[PlayerGM] 用法: player energy <n>");
                break;
            case "shards":
                if (parts.Length > 1 && int.TryParse(parts[1], out int sh)) m.AddShards(sh - m.Shards);
                else Debug.LogWarning("[PlayerGM] 用法: player shards <n>");
                break;
            case "diamonds":
                if (parts.Length > 1 && int.TryParse(parts[1], out int dia)) m.AddDiamonds(dia - m.Diamonds);
                else Debug.LogWarning("[PlayerGM] 用法: player diamonds <n>");
                break;
            case "avatar":
                if (parts.Length > 1 && int.TryParse(parts[1], out int a)) m.SetAvatar(a);
                else Debug.LogWarning("[PlayerGM] 用法: player avatar <n>");
                break;
            case "rewind": // 回拨恢复基准验证离线补点
                if (parts.Length > 1 && long.TryParse(parts[1], out long sec)) m.RewindRegen(sec);
                else Debug.LogWarning("[PlayerGM] 用法: player rewind <秒>");
                break;
            case "reset":
                m.ResetAll();
                Debug.Log("[PlayerGM] 已清档重置");
                break;
            default:
                Debug.LogWarning($"[PlayerGM] 未知子命令: {cmd}");
                break;
        }
    }
}

/// <summary>玩家数据事件Key（点分式，风格对齐 EventKeys / JumpEvents）</summary>
public static class PlayerEvents
{
    /// <summary>任一玩家数据变更（无参；订阅方自行拉取最新值刷新）</summary>
    public const string Changed = "Player.Changed";
}
