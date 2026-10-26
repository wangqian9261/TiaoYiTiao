using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;

/// <summary>Home-only settings: reusable nine-slice frames and anchored fixed-size ornaments.</summary>
public static class UISettingsViewSetup
{
    const string Tex="Assets/GameRes/Texture/Settings/";
    const string Data="Assets/Scripts/UI/Editor/SettingsAtlas/";
    public const string PrefabPath="Assets/GameRes/Prefabs/UISettingsView.prefab";
    static Dictionary<string,Sprite> sprites;
    static Font font;
    [Serializable] class Frame{public string name;public int x,y,w,h;public int[] border;}
    [Serializable] class Atlas{public int[] size;public Frame[] frames;}
    [Serializable] class Node{public string name,asset,parent,render_type;public float[] xywh,anchor;}
    [Serializable] class LabelSpec{public string text;public float[] xywh;public int fontSize;}
    [Serializable] class Layout{public Node[] nodes;public LabelSpec[] labels;}
    [MenuItem("SettingsUI/Rebuild Home Settings")]
    public static void CreateAll()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling)throw new Exception("Stop Play and compilation before rebuilding settings");
        var atlas=JsonUtility.FromJson<Atlas>(File.ReadAllText(Data+"settings-sliced-1.json"));
        if(atlas.size==null||atlas.size.Length!=2||atlas.size.Any(n=>n<=0||n>1024||(n&(n-1))!=0))throw new Exception("Settings atlas dimensions must be powers of two and <=1024");
        var layout=JsonUtility.FromJson<Layout>(File.ReadAllText(Data+"layout-nine-slice.json"));
        string path=Tex+"settings-sliced-1.png";AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
        var imp=(TextureImporter)AssetImporter.GetAtPath(path);Configure(imp);imp.spriteImportMode=SpriteImportMode.Multiple;
        imp.spritePixelsPerUnit=100;
        imp.spritesheet=atlas.frames.Select(f=>new SpriteMetaData{name=f.name,rect=new Rect(f.x,atlas.size[1]-f.y-f.h,f.w,f.h),alignment=(int)SpriteAlignment.Center,pivot=new Vector2(.5f,.5f),border=new Vector4(f.border[0],f.border[1],f.border[2],f.border[3])}).ToArray();
        imp.SaveAndReimport();sprites=AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().ToDictionary(x=>x.name);
        font=AssetDatabase.LoadAssetAtPath<Font>("Assets/GameRes/Fonts/HuayangSettings-SC-Medium.otf");
        if(font==null||sprites.Count!=atlas.frames.Length)throw new Exception("Settings font/sprite bindings missing");
        var existing=AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        var existingBg=existing!=null?existing.GetComponentsInChildren<Image>(true).FirstOrDefault(i=>i.name=="Background"):null;
        // User explicitly confirmed: no background texture; preserve existing black mask.
        Sprite bg=null;
        var scene=UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();GameObject root=null;
        try{
            root=new GameObject("UISettingsView",typeof(RectTransform),typeof(UISettingsView));UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root,scene);
            var r=(RectTransform)root.transform;Stretch(r);var view=root.GetComponent<UISettingsView>();view.PanelKey=UISettingsView.Key;view.Layer=UILayer.Page;
            var b=Group(r,"Background",750,1334);var bi=b.gameObject.AddComponent<Image>();bi.sprite=bg;bi.color=existingBg!=null?existingBg.color:Color.white;bi.raycastTarget=true;
            var af=b.gameObject.AddComponent<AspectRatioFitter>();af.aspectMode=AspectRatioFitter.AspectMode.EnvelopeParent;af.aspectRatio=750f/1334f;
            var d=Group(r,"Design750x1334",750,1334);view.designRoot=d;
            var parents=new Dictionary<string,RectTransform>{{"design",d}};
            foreach(var n in layout.nodes){
                var rect=Group(parents[n.parent],n.name,n.xywh[2],n.xywh[3]);rect.anchorMin=rect.anchorMax=new Vector2(n.anchor[0],1-n.anchor[1]);rect.pivot=new Vector2(0,1);rect.anchoredPosition=new Vector2(n.xywh[0],-n.xywh[1]);
                var i=rect.gameObject.AddComponent<Image>();i.sprite=sprites[n.asset];i.type=n.render_type=="sliced"?Image.Type.Sliced:Image.Type.Simple;i.raycastTarget=false;parents.Add(n.name,rect);
                if(n.name=="Close"){i.raycastTarget=true;view.closeButton=rect.gameObject.AddComponent<Button>();view.closeButton.targetGraphic=i;}
            }
            foreach(var l in layout.labels)Label(d,"Text_"+l.text,l.text,l.xywh,l.fontSize);
            view.musicSlider=MakeSlider(d,"MusicSlider",527);view.soundSlider=MakeSlider(d,"SoundSlider",700);
            var toggleRoot=Group(d,"VibrationToggle",158,80);Place(toggleRoot,503,1016,158,80);
            var track=toggleRoot.gameObject.AddComponent<Image>();track.sprite=sprites["toggle_track_on"];track.type=Image.Type.Sliced;track.raycastTarget=true;
            var toggle=toggleRoot.gameObject.AddComponent<Toggle>();toggle.targetGraphic=track;toggle.transition=Selectable.Transition.None;toggle.toggleTransition=Toggle.ToggleTransition.None;toggle.isOn=true;
            var knob=Group(toggleRoot,"Handle",65,67);var ki=knob.gameObject.AddComponent<Image>();ki.sprite=sprites["toggle_handle"];ki.raycastTarget=false;knob.anchoredPosition=new Vector2(46,0);
            view.vibrationToggle=toggle;view.vibrationTrack=track;view.vibrationHandle=knob;view.vibrationOn=sprites["toggle_track_on"];view.vibrationOff=sprites["toggle_track_off"];
            var saved=PrefabUtility.SaveAsPrefabAsset(root,PrefabPath);if(saved==null)throw new Exception("Prefab save failed");AssetDatabase.SaveAssetIfDirty(saved);
        }finally{if(root!=null)UnityEngine.Object.DestroyImmediate(root);UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);}
    }
    static Slider MakeSlider(RectTransform d,string name,float centerY)
    {
        var root=Group(d,name,320,84);Place(root,345,centerY-42,320,84);var hit=root.gameObject.AddComponent<Image>();hit.color=Color.clear;hit.raycastTarget=true;
        var slider=root.gameObject.AddComponent<Slider>();slider.minValue=0;slider.maxValue=1;slider.wholeNumbers=false;slider.transition=Selectable.Transition.None;
        var rail=Group(root,"Track",320,40);var ri=rail.gameObject.AddComponent<Image>();ri.sprite=sprites["slider_track"];ri.type=Image.Type.Sliced;ri.raycastTarget=false;
        var fillArea=Group(root,"FillArea",304,26);var fill=Group(fillArea,"Fill",304,26);Stretch(fill);var fi=fill.gameObject.AddComponent<Image>();fi.sprite=sprites["slider_fill"];fi.type=Image.Type.Filled;fi.fillMethod=Image.FillMethod.Horizontal;fi.fillOrigin=0;fi.raycastTarget=false;
        var handleArea=Group(root,"HandleArea",304,0);var handle=Group(handleArea,"Handle",61,63);var hi=handle.gameObject.AddComponent<Image>();hi.sprite=sprites["slider_handle"];hi.raycastTarget=true;
        slider.fillRect=fill;slider.handleRect=handle;slider.targetGraphic=hi;slider.direction=Slider.Direction.LeftToRight;slider.SetValueWithoutNotify(.6f);return slider;
    }
    static void Configure(TextureImporter imp)
    {
        imp.textureType=TextureImporterType.Sprite;imp.mipmapEnabled=false;imp.alphaIsTransparency=true;imp.isReadable=false;imp.maxTextureSize=1024;
        imp.textureCompression=TextureImporterCompression.Uncompressed;imp.filterMode=FilterMode.Bilinear;imp.wrapMode=TextureWrapMode.Clamp;imp.npotScale=TextureImporterNPOTScale.None;
        var settings=new TextureImporterSettings();imp.ReadTextureSettings(settings);settings.spriteMeshType=SpriteMeshType.FullRect;imp.SetTextureSettings(settings);
    }
    static RectTransform Group(Transform p,string name,float w,float h){var go=new GameObject(name,typeof(RectTransform));var r=(RectTransform)go.transform;r.SetParent(p,false);r.anchorMin=r.anchorMax=r.pivot=new Vector2(.5f,.5f);r.sizeDelta=new Vector2(w,h);return r;}
    static void Place(RectTransform r,float x,float y,float w,float h){r.anchorMin=r.anchorMax=new Vector2(0,1);r.sizeDelta=new Vector2(w,h);r.anchoredPosition=new Vector2(x+w/2,-y-h/2);}
    static void Stretch(RectTransform r){r.anchorMin=Vector2.zero;r.anchorMax=Vector2.one;r.offsetMin=r.offsetMax=Vector2.zero;}
    static void Label(RectTransform p,string name,string text,float[] v,int size){var r=Group(p,name,v[2],v[3]);Place(r,v[0],v[1],v[2],v[3]);var t=r.gameObject.AddComponent<Text>();t.font=font;t.fontSize=size;t.text=text;t.color=new Color32(74,29,11,255);t.alignment=TextAnchor.MiddleCenter;t.raycastTarget=false;t.horizontalOverflow=HorizontalWrapMode.Overflow;t.verticalOverflow=VerticalWrapMode.Overflow;}
}
