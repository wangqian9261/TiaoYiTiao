using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Idempotent Floral HUD assembler. Uses two <=1024 atlases; never changes global CanvasScaler.</summary>
public static class UIBattleViewSetup
{
    public const string TexDir = "Assets/GameRes/Texture/LevelUI";
    public const string PrefabPath = "Assets/GameRes/Prefabs/UIBattleView.prefab";
    private static Dictionary<string, Sprite> _sprites;
    private static Font _font;
    private static readonly Color Cocoa = new Color(.392f,.157f,.071f);
    [Serializable] private class Frame { public string name; public int x,y,w,h; public float px=.5f,py=.5f; }
    [Serializable] private class Atlas { public Frame[] frames; }

    [MenuItem("BattleUI/1. Create Assets")]
    public static void CreateAll()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play before rebuilding HUD");
        _sprites=FloralSliceAssets.Load("battle");
        // Existing dynamic digits are unchanged, including their Sprite identifiers.
        foreach(var sprite in AssetDatabase.LoadAllAssetsAtPath(TexDir+"/digits-1024.png").OfType<Sprite>())_sprites.Add(sprite.name,sprite);
        _font=AssetDatabase.LoadAssetAtPath<Font>("Assets/GameRes/Fonts/HuayangBattle-SC-Medium.otf");
        if(_font==null||_sprites.Count<42)throw new Exception("Missing font or sprites");
        var root=PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            var rt=(RectTransform)root.transform;
            for(int i=rt.childCount-1;i>=0;i--)UnityEngine.Object.DestroyImmediate(rt.GetChild(i).gameObject);
            Stretch(rt);
            var view=root.GetComponent<UIBattleView>();
            view.PanelKey=JumpConfig.HudPanelKey;view.Layer=UILayer.Top;
            var top=Group(rt,"TopHUD",750,360); top.anchorMin=top.anchorMax=new Vector2(.5f,1);top.pivot=new Vector2(.5f,1);
            var bottom=Group(rt,"BottomChargeGroup",750,500);bottom.anchorMin=bottom.anchorMax=new Vector2(.5f,0);bottom.pivot=new Vector2(.5f,0);
            top.localScale=bottom.localScale=Vector3.one*1.44f;
            view.topHUD=top; view.bottomChargeGroup=bottom;
            Art(top,"HeaderBase","hud_header_base",115,57,610,112);
            view.pauseButton=ButtonAt(top,"PauseButton","",32,77,105,96,"pause_button_base");
            Art(top,"PauseIcon","pause_icon",72,100,32,40);
            view.levelNameText=TextAt(top,"LevelDifficultyText","第1关 · 简单",232,100,284,40,31);
            view.levelLabel=view.levelNameText.gameObject.AddComponent<CanvasGroup>();
            view.scoreText=TextAt(top,"ScoreText","0分",565,100,124,40,31);
            Art(top,"ProgressTrack","progress_track_base",51,170,656,52);
            view.progressFill=Art(top,"ProgressFill","progress_fill_pink",99,184,476,22);
            view.progressFill.type=Image.Type.Filled;view.progressFill.fillMethod=Image.FillMethod.Horizontal;view.progressFill.fillOrigin=0;view.progressFill.fillAmount=0;
            view.progressMarker=Art(top,"ProgressMarker","progress_marker_flower",80,174,38,38).rectTransform;
            view.progressMarker.gameObject.SetActive(false);
            view.progressText=TextAt(top,"ProgressText","0/40",586,182,88,28,23);
            Art(top,"CoinCounterBase","coin_counter_base",523,221,205,48);Art(top,"CoinIcon","coin_icon",521,219,52,52);
            view.coinsText=TextAt(top,"CoinText","本关金币 0",579,232,135,25,20);
            var combo=Group(top,"ComboFeedback",400,95);Place(combo,100,216,400,95);
            view.comboFeedback=combo.gameObject.AddComponent<CanvasGroup>();view.comboFeedback.alpha=0;
            Art(combo,"ComboWord","combo_word",280,6,110,67);
            Art(combo,"Bow","combo_bow",315,57,48,33);
            view.comboDigits=Number(combo,"ComboDigits",280,42,64,true,"combo_");
            var perfect=Group(top,"PerfectFeedback",360,70);Place(perfect,250,294,360,70);
            view.perfectFeedback=perfect.gameObject.AddComponent<CanvasGroup>();view.perfectFeedback.alpha=0;
            Art(perfect,"PerfectWord","perfect_word",0,2,99,61);
            Art(perfect,"Plus","perfect_plus",98,9,43,43);
            view.perfectDigits=Number(perfect,"BonusDigits",140,33,56,false,"perfect_");
            Art(bottom,"ChargeDisc","charge_disc_base",200,111,350,310);
            var charge=Group(bottom,"ChargeVisual",750,500);Place(charge,0,0,750,500);view.chargeVisual=charge.gameObject;
            view.targetRing=Art(charge,"TargetSector","charge_target_ring",225,107,300,300);Radial(view.targetRing);
            view.chargeNeedle=Art(charge,"Needle","charge_needle",360,127,30,145).rectTransform;
            view.chargeNeedle.pivot=new Vector2(.5f,15f/145f);view.chargeNeedle.anchoredPosition=new Vector2(375,-257);
            Art(charge,"PivotPearl","charge_pivot_pearl",353.5f,235.5f,43,43);
            view.instructionText=TextAt(bottom,"IdleHint","长按蓄力\n松手起跳",258,197,234,116,37);
            view.instructionText.lineSpacing=1.2f;
            var cancel=Group(bottom,"CancelZone",160,64);Place(cancel,295,29,160,64);view.cancelZone=cancel;
            view.cancelBase=Art(cancel,"CancelBase","cancel_button_base",18.5f,3,123,58);
            view.cancelText=TextAt(cancel,"CancelText","取消",8,13,144,40,26);
            view.bonusHintText=TextAt(bottom,"ReverseHint","反向力度",255,432,240,35,24);
            view.bonusHintText.gameObject.SetActive(false);charge.gameObject.SetActive(false);cancel.gameObject.SetActive(false);
            var trap=Group(rt,"TrapHint",132,124);view.trapHint=trap;
            Art(trap,"TimerBase","trap_timer_base",0,0,132,124);
            view.timerRing=Art(trap,"TimerRing","trap_timer_ring",21,3,108,108);Radial(view.timerRing);
            Art(trap,"ClockIcon","trap_clock_icon",60,20,26,31);
            view.trapText=TextAt(trap,"TimeText","1.5s",42,54,64,31,24);view.trapText.color=Color.white;
            trap.gameObject.SetActive(false);
            var floats=Group(rt,"FloatLayer",0,0);Stretch(floats);view.floatLayer=floats;
            MakePause(rt,view);
            PrefabUtility.SaveAsPrefabAsset(root,PrefabPath);
        }
        finally{PrefabUtility.UnloadPrefabContents(root);}
        AssetDatabase.SaveAssetIfDirty(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
        Debug.Log("BATTLE_PREFAB_REBUILT: 42 atlas sprites; canvas unchanged");
    }

    private static void MakePause(RectTransform parent,UIBattleView view)
    {
        var overlay=Group(parent,"PauseOverlay",0,0);Stretch(overlay);view.pauseOverlay=overlay.gameObject;
        var mask=overlay.gameObject.AddComponent<Image>();mask.color=new Color(.12f,.07f,.15f,.65f);mask.raycastTarget=true;
        var card=Group(overlay,"PauseCard",760,940);
        var plate=card.gameObject.AddComponent<Image>();plate.color=new Color(1f,.965f,.89f);plate.raycastTarget=true;
        Art(card,"Header","hud_header_base",40,10,680,125);
        TextAt(card,"Title","暂停",230,53,300,70,46);
        var menu=Group(card,"PauseMenu",760,760);Place(menu,0,150,760,760);view.pauseMenu=menu.gameObject;
        view.resumeButton=ButtonAt(menu,"Resume","继续游戏",180,10,400,150);
        view.restartButton=ButtonAt(menu,"Restart","重开本关",180,175,400,150);
        view.settingsButton=ButtonAt(menu,"Settings","设置",180,340,400,150);
        view.homeButton=ButtonAt(menu,"Home","返回主菜单",180,505,400,150);
        var settings=Group(card,"SettingsMenu",760,760);Place(settings,0,150,760,760);view.settingsMenu=settings.gameObject;
        view.musicSlider=MakeSlider(settings,"Music","背景音乐",130);
        view.soundSlider=MakeSlider(settings,"Sound","游戏音效",340);
        view.settingsCloseButton=ButtonAt(settings,"CloseSettings","返回暂停",180,550,400,150);
        settings.gameObject.SetActive(false);overlay.gameObject.SetActive(false);
    }
    private static Slider MakeSlider(RectTransform parent,string name,string label,float y)
    {
        TextAt(parent,name+"Label",label,90,y-70,580,50,38);
        var r=Group(parent,name+"Slider",540,60);Place(r,110,y,540,60);
        var slider=r.gameObject.AddComponent<Slider>();slider.minValue=0;slider.maxValue=1;
        var bg=Group(r,"Track",540,20);bg.anchoredPosition=Vector2.zero;
        bg.gameObject.AddComponent<Image>().color=new Color(.75f,.66f,.63f);
        var fill=Group(bg,"Fill",540,20);Stretch(fill);var fi=fill.gameObject.AddComponent<Image>();fi.color=new Color(.96f,.39f,.61f);fi.raycastTarget=false;
        var handleArea=Group(r,"HandleArea",540,60);Stretch(handleArea);
        var handle=Group(handleArea,"Handle",52,52);handle.gameObject.AddComponent<Image>().color=new Color(1f,.81f,.35f);
        slider.fillRect=fill;slider.handleRect=handle;slider.targetGraphic=handle.GetComponent<Image>();slider.direction=Slider.Direction.LeftToRight;
        return slider;
    }
    private static SpriteNumber Number(RectTransform parent,string name,float x,float y,float height,bool right,string prefix)
    {
        var rt=Group(parent,name,0,height);rt.anchorMin=rt.anchorMax=new Vector2(0,1);rt.anchoredPosition=new Vector2(x,-y);
        var n=rt.gameObject.AddComponent<SpriteNumber>();n.height=height;n.rightAligned=right;
        n.digits=Enumerable.Range(0,10).Select(i=>_sprites[prefix+i]).ToArray();return n;
    }
    private static void Radial(Image image) {image.type=Image.Type.Filled;image.fillMethod=Image.FillMethod.Radial360;image.fillOrigin=(int)Image.Origin360.Top;image.fillClockwise=true;image.fillAmount=1;}
    private static RectTransform Group(Transform p,string name,float w,float h)
    {
        var go=new GameObject(name,typeof(RectTransform));var r=(RectTransform)go.transform;r.SetParent(p,false);
        r.anchorMin=r.anchorMax=new Vector2(.5f,.5f);r.sizeDelta=new Vector2(w,h);return r;
    }
    private static void Place(RectTransform r,float x,float y,float w,float h)
    {
        r.anchorMin=r.anchorMax=new Vector2(0,1);r.pivot=new Vector2(.5f,.5f);r.sizeDelta=new Vector2(w,h);r.anchoredPosition=new Vector2(x+w*.5f,-y-h*.5f);
    }
    private static Image Art(RectTransform p,string name,string sprite,float x,float y,float w,float h)
    {
        var r=Group(p,name,w,h);Place(r,x,y,w,h);var i=r.gameObject.AddComponent<Image>();i.sprite=_sprites[sprite];i.raycastTarget=false;FloralSliceAssets.Decorate(i,"battle",sprite);return i;
    }
    private static Text TextAt(RectTransform p,string name,string text,float x,float y,float w,float h,int size)
    {
        var r=Group(p,name,w,h);Place(r,x,y,w,h);var t=r.gameObject.AddComponent<Text>();t.font=_font;t.fontSize=size;t.color=Cocoa;
        t.alignment=TextAnchor.MiddleCenter;t.text=text;t.raycastTarget=false;t.horizontalOverflow=HorizontalWrapMode.Overflow;t.verticalOverflow=VerticalWrapMode.Overflow;return t;
    }
    private static Button ButtonAt(RectTransform p,string name,string text,float x,float y,float w,float h,string sprite="cancel_button_base")
    {
        var image=Art(p,name,sprite,x,y,w,h);image.raycastTarget=true;var b=image.gameObject.AddComponent<Button>();b.targetGraphic=image;
        if(!string.IsNullOrEmpty(text))TextAt(image.rectTransform,"Label",text,10,10,w-20,h-20,36);
        return b;
    }
    private static void Stretch(RectTransform r){r.anchorMin=Vector2.zero;r.anchorMax=Vector2.one;r.pivot=new Vector2(.5f,.5f);r.offsetMin=r.offsetMax=Vector2.zero;}
}
