using UnityEditor;
using UnityEngine;

public static class SpecialTileEditorConfig
{
    public static SpecialTileConfig LoadAsset()
    {
        var asset = AssetDatabase.LoadAssetAtPath<SpecialTileConfig>(SpecialTileConfig.AssetPath);
        if (asset != null) return asset;
        asset = ScriptableObject.CreateInstance<SpecialTileConfig>();
        AssetDatabase.CreateAsset(asset, SpecialTileConfig.AssetPath);
        AssetDatabase.SaveAssets();
        return asset;
    }
    public static SpecialTileSettings LoadSettings() => LoadAsset().Settings;
}
