using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Rebuild the approved characterless garden Home, preserving the prefab GUID and button components.</summary>
public static class UIHomeViewSetup
{
    const string TexDir="Assets/GameRes/Texture/Home";
    const string PrefabPath="Assets/GameRes/Prefabs/UIHomeView.prefab";
    const string FontPath="Assets/GameRes/Fonts/HuayangUI-SC-Medium.otf";
    const float Sx=1080f/750f, Sy=1920f/1334f;
    static readonly string[] Slices={"background_clean","brand_title_huayang_tiaoqu","button_primary_base","button_round_base","currency_capsule_base","icon_settings","icon_coin","icon_diamond","icon_dress","icon_level_path","icon_trophy"};
    static readonly Color Cocoa=new Color32(102,31,12,255);

    [MenuItem("HomeUI/1. Create Assets")]
    public static void CreateAll()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play mode before rebuilding UIHome.");
        AssetDatabase.Refresh();
        FloralSliceAssets.Load("home");
        var font=AssetDatabase.LoadAssetAtPath<Font>(FontPath);
        if(font==null)throw new FileNotFoundException("Missing bundled Home font",FontPath);
        var root=PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            var rr=(RectTransform)root.transform;
            var positions=new Dictionary<string,Rect>();
            foreach(var n in new[]{"Character","MushroomPlatform","YellowPlatform","GreenPlatform"})
            {
                var old=rr.Find(n);if(old!=null)UnityEngine.Object.DestroyImmediate(old.gameObject);
            }
            var gift=rr.Find("GiftBtn");if(gift!=null)gift.gameObject.SetActive(false);
            var bg=GetRect(rr,"Background");bg.SetAsFirstSibling();
            var bgi=Get<Image>(bg);bgi.sprite=Sprite("background_clean");bgi.color=Color.white;
            bgi.type=Image.Type.Simple;bgi.preserveAspect=false;bgi.raycastTarget=true;
            bg.anchorMin=bg.anchorMax=bg.pivot=new Vector2(.5f,.5f);
            bg.anchoredPosition=Vector2.zero;bg.sizeDelta=new Vector2(1080,1920);
            var fit=Get<AspectRatioFitter>(bg);fit.aspectMode=AspectRatioFitter.AspectMode.EnvelopeParent;fit.aspectRatio=750f/1334f;

            RectTransform Art(string path,string asset,Rect r,bool bottom=false,bool button=false)
            {
                var rt=Layout(rr,path,r,bottom,positions);
                var im=Get<Image>(rt);im.sprite=Sprite(asset);im.color=Color.white;im.type=Image.Type.Simple;
                im.preserveAspect=true;im.raycastTarget=button;
                FloralSliceAssets.Decorate(im,"home",asset,Sx);
                if(button)
                {
                    var btn=Get<Button>(rt);btn.targetGraphic=im;btn.interactable=true;
                    btn.transition=Selectable.Transition.ColorTint;
                    var c=btn.colors;c.normalColor=Color.white;c.highlightedColor=Color.white;
                    c.pressedColor=new Color(.9f,.87f,.88f,1);c.fadeDuration=.1f;btn.colors=c;
                }
                return rt;
            }
            Text Label(string path,string value,Rect r,int px,Color color,bool bold=false)
            {
                var rt=Layout(rr,path,r,false,positions);var t=Get<Text>(rt);
                t.text=value;t.font=font;t.fontSize=Mathf.RoundToInt(px*Sx);t.fontStyle=bold?FontStyle.Bold:FontStyle.Normal;
                t.color=color;t.alignment=TextAnchor.MiddleCenter;t.raycastTarget=false;
                t.horizontalOverflow=HorizontalWrapMode.Wrap;t.verticalOverflow=VerticalWrapMode.Truncate;
                t.resizeTextForBestFit=true;t.resizeTextMinSize=Mathf.RoundToInt(18*Sx);t.resizeTextMaxSize=t.fontSize;
                return t;
            }
            Art("BrandTitle","brand_title_huayang_tiaoqu",new Rect(178,292,394,170));
            Art("CoinCapsule","currency_capsule_base",new Rect(139,210,198,58));
            Art("CoinCapsule/CoinIcon","icon_coin",new Rect(141,213,52,52));
            var coins=Label("CoinCapsule/CoinsText","0",new Rect(201,219,124,40),27,Cocoa,true);
            Art("DiamondCapsule","currency_capsule_base",new Rect(351,213,178,52));
            Art("DiamondCapsule/DiamondIcon","icon_diamond",new Rect(355,216,50,45));
            var diamonds=Label("DiamondCapsule/DiamondsText","0",new Rect(408,219,106,40),27,Cocoa,true);
            var settings=Art("SettingsBtn","button_round_base",new Rect(544,208,76,80),button:true);
            Art("SettingsBtn/SettingsIcon","icon_settings",new Rect(561,220,42,42));
            var start=Art("StartBtn","button_primary_base",new Rect(115,939,520,188),true,true);
            var startText=Label("StartBtn/StartText","开始游戏",new Rect(185,1000,380,88),61,new Color32(255,248,220,255),true);
            var outline=Get<Outline>(startText.transform);outline.effectColor=new Color32(182,101,24,255);outline.effectDistance=new Vector2(2.2f,-2.2f);outline.useGraphicAlpha=true;
            var dress=Art("DressBtn","button_round_base",new Rect(109,1131,150,146),true,true);
            Art("DressBtn/DressIcon","icon_dress",new Rect(143,1150,82,67));
            Label("DressBtn/DressLabel","装扮",new Rect(134,1210,100,40),26,Cocoa,true);
            var levels=Art("LevelsBtn","button_round_base",new Rect(300,1131,150,146),true,true);
            Art("LevelsBtn/LevelsIcon","icon_level_path",new Rect(336,1150,78,63));
            Label("LevelsBtn/LevelsLabel","关卡",new Rect(325,1210,100,40),26,Cocoa,true);
            var rank=Art("RankingBtn","button_round_base",new Rect(491,1131,150,146),true,true);
            Art("RankingBtn/RankingIcon","icon_trophy",new Rect(533,1150,66,66));
            Label("RankingBtn/RankingLabel","排行",new Rect(516,1210,100,40),26,Cocoa,true);
            var v=root.GetComponent<UIHomeView>();
            v.coinsText=coins;v.diamondsText=diamonds;v.settingsBtn=settings.GetComponent<Button>();
            v.startBtn=start.GetComponent<Button>();v.dressBtn=dress.GetComponent<Button>();
            v.levelsBtn=levels.GetComponent<Button>();v.rankBtn=rank.GetComponent<Button>();
            // Gift is not part of the approved Home. Keep the inactive node/reference and any existing listeners.
            PrefabUtility.SaveAsPrefabAsset(root,PrefabPath);
        }
        finally {PrefabUtility.UnloadPrefabContents(root);}
        AssetDatabase.SaveAssetIfDirty(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
        Debug.Log("[UIHomeViewSetup] Garden Home updated: 11 sprites, separate text, no character preview. Loading unchanged.");
    }

    static RectTransform Layout(RectTransform root,string path,Rect rect,bool bottom,Dictionary<string,Rect> positions)
    {
        int slash=path.LastIndexOf('/');
        var parent=slash<0?root:(RectTransform)root.Find(path.Substring(0,slash));
        var rt=GetRect(parent,slash<0?path:path.Substring(slash+1));
        rt.gameObject.SetActive(true);rt.localScale=Vector3.one;rt.localRotation=Quaternion.identity;rt.pivot=new Vector2(.5f,.5f);
        if(slash<0)
        {
            rt.anchorMin=rt.anchorMax=new Vector2(.5f,bottom?0:1);
            rt.anchoredPosition=new Vector2((rect.center.x-375)*Sx,(bottom?1334-rect.center.y:-rect.center.y)*Sy);
        }
        else
        {
            var pr=positions[path.Substring(0,slash)];rt.anchorMin=rt.anchorMax=new Vector2(.5f,.5f);
            rt.anchoredPosition=new Vector2((rect.center.x-pr.center.x)*Sx,(pr.center.y-rect.center.y)*Sy);
        }
        rt.sizeDelta=new Vector2(rect.width*Sx,rect.height*Sy);positions[path]=rect;return rt;
    }
    static RectTransform GetRect(Transform parent,string name)
    {
        var t=parent.Find(name) as RectTransform;
        if(t==null){t=(RectTransform)new GameObject(name,typeof(RectTransform)).transform;t.SetParent(parent,false);}
        return t;
    }
    static T Get<T>(Transform t) where T:Component => t.GetComponent<T>()??t.gameObject.AddComponent<T>();
    static Sprite Sprite(string n)=>FloralSliceAssets.Has("home",n)?FloralSliceAssets.Sprite("home",n):AssetDatabase.LoadAssetAtPath<Sprite>(TexDir+"/"+n+".png");
    static void ConfigureSprite(string n)
    {
        var path=TexDir+"/"+n+".png";
        var imp=AssetImporter.GetAtPath(path) as TextureImporter;
        if(imp==null)throw new FileNotFoundException("Missing Home slice",path);
        imp.textureType=TextureImporterType.Sprite;imp.spriteImportMode=SpriteImportMode.Single;
        imp.alphaIsTransparency=n!="background_clean";imp.mipmapEnabled=false;
        imp.wrapMode=TextureWrapMode.Clamp;imp.filterMode=FilterMode.Bilinear;
        imp.npotScale=TextureImporterNPOTScale.None;imp.maxTextureSize=n=="background_clean"?2048:1024;
        // Fine flowers and pearls visibly block under the former ASTC 6x6 defaults.
        // High quality uses finer platform compression without introducing an uncompressed full-screen texture.
        imp.textureCompression=TextureImporterCompression.CompressedHQ;imp.compressionQuality=100;
        imp.spriteBorder=Vector4.zero;
        var settings=new TextureImporterSettings();imp.ReadTextureSettings(settings);settings.spriteMeshType=SpriteMeshType.FullRect;imp.SetTextureSettings(settings);
        imp.SaveAndReimport();
    }
}
