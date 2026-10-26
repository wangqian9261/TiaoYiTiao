using UnityEngine;

/// <summary>Local UI preferences; isolated keys never touch player economy or progress.</summary>
public static class GamePreferences
{
    const string MusicKey="Huayang.Settings.Music";
    const string SoundKey="Huayang.Settings.Sound";
    const string VibrationKey="Huayang.Settings.Vibration";
    public static float MusicVolume
    {
        get => NormalizeVolume(PlayerPrefs.GetFloat(MusicKey,.8f),.8f);
        set => PlayerPrefs.SetFloat(MusicKey,NormalizeVolume(value,.8f));
    }
    public static float SoundVolume
    {
        get => NormalizeVolume(PlayerPrefs.GetFloat(SoundKey,1f),1f);
        set => PlayerPrefs.SetFloat(SoundKey,NormalizeVolume(value,1f));
    }
    public static bool VibrationEnabled
    {
        get => PlayerPrefs.GetInt(VibrationKey,1)!=0;
        set => PlayerPrefs.SetInt(VibrationKey,value?1:0);
    }
    public static float NormalizeVolume(float value,float fallback)
        => float.IsNaN(value)||float.IsInfinity(value)?fallback:Mathf.Clamp01(value);
    public static void Save() => PlayerPrefs.Save();
}
