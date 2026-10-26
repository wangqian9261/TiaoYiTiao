using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using Codely.Newtonsoft.Json.Linq;

public static class UIResultViewSetup
{
    public const string PrefabPath = "Assets/GameRes/Prefabs/Panel_JumpResult.prefab";
    const string Data = "Assets/Scripts/UI/Editor/ResultUIData/";
    const string ArtPath = "Assets/GameRes/ResultUI/";
    static Dictionary<string, Sprite> sprites;
    static Font font;
    [Serializable] class Frame { public string name; public int x,y,w,h; }
    [Serializable] class Atlas { public Frame[] frames; }
    [MenuItem("ResultUI/Rebuild Victory and Failure")]
    public static void CreateAll()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Stop Play first");
        sprites=FloralSliceAssets.Load("result");
        font=AssetDatabase.LoadAssetAtPath<Font>("Assets/GameRes/Fonts/HuayangResultUI-Medium.otf");
        if(font==null||sprites.Count<8)throw new Exception("Missing result font or eight sprites");
        var layout=JObject.Parse(File.ReadAllText(Data+"layout.json"));
        // Build in a preview scene; never dirty or save the user's active scene.
        var scene=UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
        var root=new GameObject("Panel_JumpResult",typeof(RectTransform),typeof(Canvas),typeof(GraphicRaycaster),typeof(PanelJumpResult));
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root,scene);
        try
        {
            var r=(RectTransform)root.transform;Stretch(r);
            var view=root.GetComponent<PanelJumpResult>();view.PanelKey=JumpConfig.ResultPanelKey;view.Layer=UILayer.Page;
            view.overlayCanvas=root.GetComponent<Canvas>();view.overlayCanvas.overrideSorting=true;view.overlayCanvas.sortingOrder=410;
            var mask=Group(r,"Dim",750,1334);Stretch(mask);view.dim=mask.gameObject.AddComponent<Image>();view.dim.color=new Color(0,0,0,.6f);view.dim.raycastTarget=true;
            var design=Group(r,"Design750x1334",750,1334);view.panelRoot=design;view.reveal=design.gameObject.AddComponent<CanvasGroup>();
            view.victory=Build(design,"Victory",layout["victory"],true);
            view.defeat=Build(design,"Failure",layout["defeat"],false);view.defeat.root.SetActive(false);
            view.titleText=view.victory.title;view.scoreText=view.victory.score;view.coinsText=view.victory.coins;
            view.retryBtn=view.victory.retry;view.homeBtn=view.victory.home;view.reviveBtn=view.defeat.revive;
            PrefabUtility.SaveAsPrefabAsset(root,PrefabPath);
        }
        finally { UnityEngine.Object.DestroyImmediate(root);UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene); }
        AssetDatabase.SaveAssetIfDirty(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
    }
    static PanelJumpResult.Presentation Build(RectTransform parent,string name,JToken data,bool win)
    {
        var root=Group(parent,name,750,1334);var p=new PanelJumpResult.Presentation{root=root.gameObject};
        var actions=new Dictionary<string,RectTransform>();var boxes=new Dictionary<string,float[]>();
        foreach(var row in data["images"])
        {
            string id=(string)row[0],sprite=(string)row[1];var xy=row[2].ToObject<float[]>();
            var rt=Group(root,id,xy[2],xy[3]);Place(rt,xy);var im=rt.gameObject.AddComponent<Image>();im.sprite=sprites[sprite];im.raycastTarget=false;FloralSliceAssets.Decorate(im,"result",sprite);
            if(id=="coin")p.coinIcon=rt.gameObject;
            if(id=="primary"||id=="retry"||id=="home")
            {
                im.raycastTarget=true;var b=rt.gameObject.AddComponent<Button>();b.targetGraphic=im;
                var nav=b.navigation;nav.mode=Navigation.Mode.None;b.navigation=nav;
                var c=b.colors;c.pressedColor=new Color(.87f,.87f,.87f);c.highlightedColor=Color.white;c.selectedColor=Color.white;c.disabledColor=new Color(.7f,.7f,.7f,.8f);b.colors=c;
                if(id=="home")p.home=b;else if(id=="retry"||win)p.retry=b;else p.revive=b;
                actions[id]=rt;boxes[id]=xy;
            }
        }
        foreach(var row in data["texts"])
        {
            string id=(string)row[0],text=(string)row[1],color=(string)row[4];var xy=row[2].ToObject<float[]>();int sz=(int)row[3];
            string action=id.EndsWith("_label")?id.Substring(0,id.Length-6):"";
            RectTransform target=root;
            if(actions.ContainsKey(action)){target=actions[action];xy[0]-=boxes[action][0];xy[1]-=boxes[action][1];}
            if(id=="coins_label") text="总金币";
            var t=Label(target,id,text,xy,sz,color);
            if(id=="coins_label")p.rewardLabel=t;
            if(id=="title")p.title=t;if(id=="score")p.score=t;if(id=="coins")p.coins=t;
            if((win&&id=="primary_label")||(!win&&id=="retry_label"))p.retryLabel=t;
        }
        p.status=Label(root,"Status","",new float[]{75,win?1200:1270,600,40},25,"cream");
        p.breakdown=Label(root,"RewardBreakdown","",new float[]{75,win?760:788,600,36},26,"cocoa");
        // The original result font is a tiny subset without the new reward/combo glyphs.
        var rewardFont=AssetDatabase.LoadAssetAtPath<Font>("Assets/GameRes/Fonts/HuayangBattle-SC-Medium.otf");
        p.breakdown.font=rewardFont;
        p.status.font=rewardFont;
        foreach(var label in root.GetComponentsInChildren<Text>(true))
            if(label.name=="coins_label") label.font=rewardFont;
        return p;
    }
    static RectTransform Group(Transform p,string name,float w,float h)
    {
        var go=new GameObject(name,typeof(RectTransform));var r=(RectTransform)go.transform;r.SetParent(p,false);
        r.anchorMin=r.anchorMax=r.pivot=new Vector2(.5f,.5f);r.sizeDelta=new Vector2(w,h);return r;
    }
    static void Stretch(RectTransform r){r.anchorMin=Vector2.zero;r.anchorMax=Vector2.one;r.offsetMin=r.offsetMax=Vector2.zero;}
    static void Place(RectTransform r,float[] xy){r.anchorMin=r.anchorMax=new Vector2(0,1);r.sizeDelta=new Vector2(xy[2],xy[3]);r.anchoredPosition=new Vector2(xy[0]+xy[2]/2,-xy[1]-xy[3]/2);}
    static Text Label(RectTransform p,string name,string value,float[] xy,int size,string color)
    {
        var r=Group(p,name,xy[2],xy[3]);Place(r,xy);var t=r.gameObject.AddComponent<Text>();
        t.font=font;t.fontSize=size;t.fontStyle=FontStyle.Bold;t.text=value;t.alignment=TextAnchor.MiddleCenter;t.raycastTarget=false;
        t.resizeTextForBestFit=true;t.resizeTextMinSize=20;t.resizeTextMaxSize=size;t.horizontalOverflow=HorizontalWrapMode.Wrap;t.verticalOverflow=VerticalWrapMode.Truncate;
        t.color=color=="cream"?new Color32(255,245,216,255):color=="gold"?new Color32(191,74,0,255):new Color32(84,23,3,255);
        var shadow=r.gameObject.AddComponent<Shadow>();shadow.effectColor=new Color32(63,16,3,160);shadow.effectDistance=new Vector2(0,-2);
        if(name=="title"||name=="score"||name=="coins") { var edge=r.gameObject.AddComponent<Outline>();edge.effectColor=new Color32(255,190,58,255);edge.effectDistance=new Vector2(1.2f,-1.2f); }
        return t;
    }
}
