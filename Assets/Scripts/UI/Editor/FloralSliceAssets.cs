using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Codely.Newtonsoft.Json.Linq;

/// <summary>Editor-only nine-slice imports and fixed ornament assembly. No gameplay logic.</summary>
public static class FloralSliceAssets
{
    const string Data="Assets/Scripts/UI/Editor/FloralSliceData/";
    static JObject composites;
    static readonly Dictionary<string,Dictionary<string,Sprite>> cache=new Dictionary<string,Dictionary<string,Sprite>>();
    public static string ArtDir(string group) => group=="result"?"Assets/GameRes/ResultUI/Sliced/":
        "Assets/GameRes/Texture/"+(group=="home"?"Home":group=="loading"?"Loading":"LevelUI")+"/Sliced/";
    public static Dictionary<string,Sprite> Load(string group)
    {
        composites=JObject.Parse(File.ReadAllText(Data+"composites.json"));
        var manifest=JObject.Parse(File.ReadAllText(Data+"manifest.json"));
        var result=new Dictionary<string,Sprite>();
        foreach(string page in manifest["groups"][group]["atlases"].Values<string>())
        {
            string path=ArtDir(group)+page+".png";
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
            var imp=(TextureImporter)AssetImporter.GetAtPath(path);
            imp.textureType=TextureImporterType.Sprite;imp.spriteImportMode=SpriteImportMode.Multiple;
            imp.mipmapEnabled=false;imp.alphaIsTransparency=true;imp.isReadable=false;
            imp.maxTextureSize=1024;imp.textureCompression=TextureImporterCompression.Uncompressed;
            imp.filterMode=FilterMode.Bilinear;imp.wrapMode=TextureWrapMode.Clamp;imp.npotScale=TextureImporterNPOTScale.None;
            imp.spritePixelsPerUnit=100;
            var settings=new TextureImporterSettings();imp.ReadTextureSettings(settings);settings.spriteMeshType=SpriteMeshType.FullRect;imp.SetTextureSettings(settings);
            var atlas=JObject.Parse(File.ReadAllText(Data+page+".json"));
            imp.spritesheet=atlas["frames"].Select(f=>new SpriteMetaData{
                name=(string)f["name"],rect=new Rect((float)f["x"],(float)atlas["size"][1]-(float)f["y"]-(float)f["h"],(float)f["w"],(float)f["h"]),
                alignment=(int)SpriteAlignment.Center,pivot=new Vector2(.5f,.5f),
                border=new Vector4((float)f["border"][0],(float)f["border"][1],(float)f["border"][2],(float)f["border"][3])}).ToArray();
            // This Tuanjie project has no 2D Sprite DataProvider package. Its built-in
            // importer exposes the equivalent name/file-ID mapping via serialization.
            // The legacy spritesheet setter alone assigns zero IDs when frames move.
            var so=new SerializedObject(imp);so.Update();var entries=so.FindProperty("m_SpriteSheet.m_Sprites");var names=so.FindProperty("m_SpriteSheet.m_NameFileIdTable");
            if(entries==null||names==null)throw new InvalidOperationException("Unsupported sprite ID serialization; abort before prefab changes");
            names.arraySize=entries.arraySize;var used=new HashSet<long>();
            for(int n=0;n<entries.arraySize;n++){
                var entry=entries.GetArrayElementAtIndex(n);string name=entry.FindPropertyRelative("m_Name").stringValue;
                byte[] hash;using(var md5=MD5.Create())hash=md5.ComputeHash(Encoding.UTF8.GetBytes("FloralNineSlice/v1/"+group+"/"+name));
                long id=BitConverter.ToInt64(hash,0)&long.MaxValue;if(id==0||!used.Add(id))throw new InvalidOperationException("Sprite ID collision");
                entry.FindPropertyRelative("m_InternalID").longValue=id;entry.FindPropertyRelative("m_SpriteID").stringValue=BitConverter.ToString(hash).Replace("-","").ToLowerInvariant();
                var pair=names.GetArrayElementAtIndex(n);pair.FindPropertyRelative("first").stringValue=name;pair.FindPropertyRelative("second").longValue=id;
            }
            so.ApplyModifiedPropertiesWithoutUndo();imp.SaveAndReimport();
            if(AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().Count()!=atlas["frames"].Count())throw new InvalidOperationException("Sprite import incomplete: "+path);
            foreach(var s in AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>())result.Add(s.name,s);
        }
        foreach(var p in composites.Properties().Where(p=>p.Name.StartsWith(group+"/")))
            result[p.Name.Substring(group.Length+1)]=result[(string)p.Value["base"]];
        cache[group]=result;return result;
    }
    public static bool Has(string group,string key)=>composites?[group+"/"+key]!=null;
    public static Sprite Sprite(string group,string key)=>cache[group][key];
    public static void Decorate(Image owner,string group,string key,float scale=1)
    {
        var c=composites?[group+"/"+key];if(c==null)return;
        // Rebuilding Home retains existing button nodes, so clear only our named children.
        for(int n=owner.transform.childCount-1;n>=0;n--){var t=owner.transform.GetChild(n);if(t.name.StartsWith("Decor_"))UnityEngine.Object.DestroyImmediate(t.gameObject);}
        if((bool?)c["scale_to_height"]==true)scale=owner.rectTransform.sizeDelta.y/(float)c["size"][1];
        owner.sprite=cache[group][(string)c["base"]];owner.preserveAspect=false;
        bool fixedOutline=(bool?)c["fixed_outline"]==true;
        owner.type=fixedOutline?Image.Type.Simple:Image.Type.Sliced;owner.pixelsPerUnitMultiplier=1/scale;
        int decorationOrder=0;
        owner.enabled=!fixedOutline;
        if(fixedOutline){
            var rect=c["base_rect"];var go=new GameObject("Decor_BaseOutline",typeof(RectTransform),typeof(Image));var r=(RectTransform)go.transform;r.SetParent(owner.transform,false);r.SetSiblingIndex(decorationOrder++);
            r.anchorMin=r.anchorMax=r.pivot=new Vector2(.5f,.5f);r.sizeDelta=new Vector2((float)rect[2],(float)rect[3])*scale;
            r.anchoredPosition=new Vector2((float)rect[0]+(float)rect[2]/2-(float)c["size"][0]/2,(float)c["size"][1]/2-(float)rect[1]-(float)rect[3]/2)*scale;
            var im=go.GetComponent<Image>();im.sprite=owner.sprite;im.raycastTarget=false;
        }
        foreach(var d in c["decorations"])
        {
            var go=new GameObject("Decor_"+(string)d["sprite"],typeof(RectTransform),typeof(Image));
            var r=(RectTransform)go.transform;r.SetParent(owner.transform,false);r.SetSiblingIndex(decorationOrder++);r.pivot=new Vector2(.5f,.5f);
            r.anchorMin=r.anchorMax=new Vector2((float)d["anchor"][0],(float)d["anchor"][1]);
            r.sizeDelta=new Vector2((float)d["size"][0],(float)d["size"][1])*scale;
            r.anchoredPosition=new Vector2((float)d["offset"][0],(float)d["offset"][1])*scale;
            if((bool?)d["stretch_x"]==true){r.anchorMin=new Vector2(0,r.anchorMin.y);r.anchorMax=new Vector2(1,r.anchorMax.y);r.sizeDelta=new Vector2(-2*(float)d["inset"]*scale,r.sizeDelta.y);}
            var im=go.GetComponent<Image>();im.sprite=cache[group][(string)d["sprite"]];im.raycastTarget=false;
            im.type=(bool?)d["tile"]==true?Image.Type.Tiled:Image.Type.Simple;im.pixelsPerUnitMultiplier=1/scale;
        }
    }
}
