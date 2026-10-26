using System;
using System.Collections.Generic;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 数据管理：配置表（GameRes/Config 下 JSON，经 ResMgr 加载常驻缓存）+
/// 玩家存档（经 WeChatService.Storage 落地：微信构建即 wx.setStorageSync，编辑器即 PlayerPrefs）。
/// 存档支持 XOR+Base64 混淆（防随手窥看，非加密）。
/// 注意：使用存档能力前需保证 WeChatService.Init 已被调用（GameLauncher 已保证）。
/// </summary>
public class DataMgr : PureSingleton<DataMgr>
{
    private const string ObfuscateKey = "Game3D001_Framework_XOR";

    private readonly Dictionary<string, TextAsset> _configCache = new Dictionary<string, TextAsset>();

    /// <summary>
    /// 异步加载配置表并反序列化。key 如 "Config/demo"（对应 GameRes/Config/demo.json）。
    /// 配置资产常驻内存（引用计数由 DataMgr 持有，不释放）。
    /// </summary>
    public async UniTask<T> LoadConfigAsync<T>(string configKey) where T : class
    {
        if (_configCache.TryGetValue(configKey, out var cached) && cached != null)
            return JsonUtility.FromJson<T>(cached.text);

        TextAsset asset = await ResMgr.Instance.LoadAssetAsync<TextAsset>(configKey);
        if (asset == null)
        {
            Debug.LogError($"[DataMgr] 配置缺失: {configKey}");
            return null;
        }
        _configCache[configKey] = asset;
        try
        {
            return JsonUtility.FromJson<T>(asset.text);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            return null;
        }
    }

    /// <summary>保存存档（JSON 序列化后落平台存储）</summary>
    public void Save<T>(string key, T data, bool obfuscate = false)
    {
        if (data == null) return;
        string json = JsonUtility.ToJson(data);
        if (obfuscate) json = XorObfuscate(json);
        WeChatService.Storage.Set(key, json);
    }

    /// <summary>读取存档（不存在返回 null；损坏数据返回 null 并保留原值）</summary>
    public T Load<T>(string key, bool obfuscated = false) where T : class
    {
        string raw = WeChatService.Storage.Get(key);
        if (string.IsNullOrEmpty(raw)) return null;
        if (obfuscated)
        {
            raw = XorDeobfuscate(raw);
            if (raw == null)
            {
                Debug.LogWarning($"[DataMgr] 存档混淆数据损坏: {key}");
                return null;
            }
        }
        try
        {
            return JsonUtility.FromJson<T>(raw);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            return null;
        }
    }

    public void Delete(string key) => WeChatService.Storage.Delete(key);

    /// <summary>当前配置缓存数量（调试）</summary>
    public int ConfigCacheCount => _configCache.Count;

    private static string XorObfuscate(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        var key = Encoding.UTF8.GetBytes(ObfuscateKey);
        for (int i = 0; i < bytes.Length; i++) bytes[i] ^= key[i % key.Length];
        return Convert.ToBase64String(bytes);
    }

    private static string XorDeobfuscate(string input)
    {
        try
        {
            var bytes = Convert.FromBase64String(input);
            var key = Encoding.UTF8.GetBytes(ObfuscateKey);
            for (int i = 0; i < bytes.Length; i++) bytes[i] ^= key[i % key.Length];
            return Encoding.UTF8.GetString(bytes);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
