using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Framework 演示资产一键生成（幂等）：
/// - GameRes/Audio/click.wav（程序化合成点击音效，验证 AudioMgr SFX 路径）
/// - GameRes/Audio/bgm_loop.wav（程序化合成简短 BGM，验证 AudioMgr BGM 路径）
/// - GameRes/Prefabs/PoolCube.prefab（对象池演示模板）
/// - GameRes/Config/demo.json（配置表，验证 DataMgr 加载）
/// </summary>
public static class FrameworkDemoAssets
{
    private const string AudioDir = "Assets/GameRes/Audio";
    private const string PrefabDir = "Assets/GameRes/Prefabs";
    private const string ConfigDir = "Assets/GameRes/Config";

    [MenuItem("Framework/1. Create Demo Assets")]
    public static void CreateAll()
    {
        EnsureDir(AudioDir);
        EnsureDir(PrefabDir);
        EnsureDir(ConfigDir);

        string clickPath = $"{AudioDir}/click.wav";
        if (!File.Exists(clickPath))
        {
            File.WriteAllBytes(clickPath, MakeClickClip());
            ImportAudio(clickPath);
            Debug.Log("[FrameworkDemo] 生成 click.wav");
        }

        string bgmPath = $"{AudioDir}/bgm_loop.wav";
        if (!File.Exists(bgmPath))
        {
            File.WriteAllBytes(bgmPath, MakeBgmClip());
            ImportAudio(bgmPath);
            Debug.Log("[FrameworkDemo] 生成 bgm_loop.wav");
        }

        string cubePath = $"{PrefabDir}/PoolCube.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(cubePath) == null)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "PoolCube";
            PrefabUtility.SaveAsPrefabAsset(cube, cubePath);
            Object.DestroyImmediate(cube);
            Debug.Log("[FrameworkDemo] 生成 PoolCube.prefab");
        }

        string configPath = $"{ConfigDir}/demo.json";
        if (!File.Exists(configPath))
        {
            File.WriteAllText(configPath, "{\"coinPerLevel\":100,\"maxHp\":50,\"tips\":\"hello framework\"}");
            AssetDatabase.ImportAsset(configPath);
            Debug.Log("[FrameworkDemo] 生成 demo.json");
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[FrameworkDemo] 演示资产就绪");
    }

    private static void ImportAudio(string path)
    {
        AssetDatabase.ImportAsset(path);
        if (AssetImporter.GetAtPath(path) is AudioImporter importer)
        {
            importer.loadInBackground = false; // 短音效优先同步预载
            importer.SaveAndReimport();
        }
    }

    /// <summary>0.06s 2kHz 衰减正弦 + 白噪冲击 → 按钮/点击音效</summary>
    private static byte[] MakeClickClip()
    {
        const int sampleRate = 44100;
        const float duration = 0.06f;
        int length = (int)(sampleRate * duration);
        var data = new float[length];
        var rng = new System.Random(42);
        for (int i = 0; i < length; i++)
        {
            float t = (float)i / sampleRate;
            float envelope = Mathf.Exp(-t * 55f);
            float tone = Mathf.Sin(2f * Mathf.PI * 2000f * t) * 0.6f;
            float noise = ((float)rng.NextDouble() * 2f - 1f) * 0.25f * Mathf.Exp(-t * 120f);
            data[i] = (tone + noise) * envelope * 0.8f;
        }
        return Encode(data, sampleRate);
    }

    /// <summary>约6秒 C 大调琶音软垫（淡入淡出），验证 BGM 链路（非无缝循环，仅演示用）</summary>
    private static byte[] MakeBgmClip()
    {
        const int sampleRate = 44100;
        const float duration = 6f;
        int length = (int)(sampleRate * duration);
        var data = new float[length];
        // C4 E4 G4 C5 琶音基础频率
        float[] chord = { 261.63f, 329.63f, 392.00f, 523.25f };
        for (int i = 0; i < length; i++)
        {
            float t = (float)i / sampleRate;
            float fadeIn = Mathf.Clamp01(t / 0.5f);
            float fadeOut = Mathf.Clamp01((duration - t) / 0.5f);
            float note = chord[(int)(t * 1.2f) % chord.Length];
            float tone = Mathf.Sin(2f * Mathf.PI * note * t) * 0.25f
                       + Mathf.Sin(2f * Mathf.PI * note * 2f * t) * 0.06f;
            data[i] = tone * fadeIn * fadeOut;
        }
        return Encode(data, sampleRate);
    }

    private static byte[] Encode(float[] data, int sampleRate)
    {
        int dataSize = data.Length * 2;
        var ms = new System.IO.MemoryStream();
        using (var w = new System.IO.BinaryWriter(ms))
        {
            w.Write('R'); w.Write('I'); w.Write('F'); w.Write('F');
            w.Write(36 + dataSize);
            w.Write('W'); w.Write('A'); w.Write('V'); w.Write('E');
            w.Write('f'); w.Write('m'); w.Write('t'); w.Write(' ');
            w.Write(16);                  // fmt chunk size
            w.Write((short)1);            // PCM
            w.Write((short)1);            // mono
            w.Write(sampleRate);
            w.Write(sampleRate * 2);      // byte rate
            w.Write((short)2);            // block align
            w.Write((short)16);           // bits per sample
            w.Write('d'); w.Write('a'); w.Write('t'); w.Write('a');
            w.Write(dataSize);
            for (int i = 0; i < data.Length; i++)
                w.Write((short)(Mathf.Clamp(data[i], -1f, 1f) * 32767f));
        }
        return ms.ToArray();
    }

    private static void EnsureDir(string dir)
    {
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);
    }
}
