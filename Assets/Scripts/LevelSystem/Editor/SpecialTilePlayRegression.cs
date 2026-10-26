using System;
using System.Linq;
using System.Reflection;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Real Play Mode state-machine checks, isolated empty scene, no wallet/progress writes.</summary>
[InitializeOnLoad]
public static class SpecialTilePlayRegression
{
    const string Pending = "SpecialTilePlayRegression.pending";
    static int checks;
    static SpecialTilePlayRegression()
    {
        EditorApplication.playModeStateChanged += change => {
            if(change == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Pending,false))
            { SessionState.SetBool(Pending,false); Run().Forget(); }
        };
    }
    public static void Start()
    {
        SessionState.SetBool(Pending,true);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        EditorApplication.EnterPlaymode();
    }
    static void Check(bool ok,string label) { checks++; if(!ok)throw new Exception("PLAY_FAIL: "+label); }
    static void Set(object obj,string field,object value) => obj.GetType().GetField(field,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(obj,value);
    static T Get<T>(object obj,string field) => (T)obj.GetType().GetField(field,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(obj);
    static void Call(object obj,string method,params object[] args) => obj.GetType().GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(obj,args);
    static GeneratedLevel Level(TileKind kind,int grade)
    {
        var d=SpecialTileRegression.Manual(kind,grade);
        return new LevelGenerator().Generate(d,DifficultyProfiles.Get(d.Difficulty));
    }
    static JumpGame Make(TileKind kind,int grade)
    {
        var go=new GameObject("SpecialTileRuntimeTest");
        var game=go.AddComponent<JumpGame>();
        typeof(JumpGame).GetProperty("Instance").GetSetMethod(true).Invoke(null,new object[]{game});
        var characterGo=new GameObject("TestCharacter");
        characterGo.transform.SetParent(go.transform);
        var character=characterGo.AddComponent<JumpCharacter>();
        Call(character,"BuildLayers"); Call(character,"BuildFallback");
        Set(game,"_character",character); Set(game,"_fx",JumpFx.Create(go.transform));
        Set(game,"_plan",LevelRuntime.BuildPlan(Level(kind,grade),1,true));
        Set(game,"_economy",ScriptableObject.CreateInstance<LevelEconomyConfig>());
        Call(game,"ResetWorld");
        Stand(game,6);
        return game;
    }
    static void Stand(JumpGame game,int index)
    {
        var blocks=Get<System.Collections.Generic.List<JumpBlock>>(game,"_blocks");
        Set(game,"_current",blocks[index]); Set(game,"_next",blocks[index+1]);
        Get<JumpCharacter>(game,"_character").SnapTo(blocks[index].TopCenter);
        Get<LevelTileEffectResolver>(game,"_effectTimer").Reset();
    }
    static void Cleanup(JumpGame game)
    {
        if(game==null)return;
        Call(game,"ClearWorld");
        UnityEngine.Object.Destroy(Get<LevelEconomyConfig>(game,"_economy"));
        UnityEngine.Object.Destroy(game.gameObject);
    }
    static async UniTask Until(Func<bool> condition,float timeout)
    {
        float end=Time.realtimeSinceStartup+timeout;
        while(!condition()) { if(Time.realtimeSinceStartup>end) throw new Exception("PLAY_FAIL: timed out"); await UniTask.Yield(); }
    }
    static async UniTaskVoid Run()
    {
        try
        {
            Time.timeScale=1; checks=0;
            for(int id=1;id<=5;id++)
            {
                var game=Make(TileKind.Ice,id);
                var character=Get<JumpCharacter>(game,"_character");
                var cfg=game.Plan.Source.Effects.GetIce(id);
                Vector3 start=character.transform.position;
                Call(game,"TickSpecialTile",cfg.StartDelay);
                Check(Vector3.Distance(start,character.transform.position)<.0001f,"ice delay L"+id);
                Call(game,"BeginCharge"); Check(game.State==JumpState.Charging,"ice charge allowed");
                Call(game,"TickSpecialTile",.25f);
                Check(Mathf.Abs(Vector3.Distance(start,character.transform.position)-cfg.Speed*.25f)<.0001f,"ice speed L"+id);
                var next=Get<JumpBlock>(game,"_next");
                var current=Get<JumpBlock>(game,"_current");
                var axis=game.Plan.TravelAxis(6)==TileAxis.X?Vector3.right:Vector3.forward;
                float distance=Vector3.Dot(next.TopCenter-character.transform.position,axis);
                Call(game,"DoJump",Mathf.InverseLerp(JumpConfig.MinJumpDist,JumpConfig.MaxJumpDist,distance));
                await Until(()=>game.State==JumpState.Ready,2);
                Check(game.LevelProgress==7,"ice actual-position jump L"+id);
                Cleanup(game); await UniTask.Yield();

                game=Make(TileKind.Ice,id);
                Call(game,"TickSpecialTile",cfg.StartDelay+20f);
                Check(game.State==JumpState.Dead,"ice edge failure L"+id);
                Cleanup(game); await UniTask.Yield();

                game=Make(TileKind.Collapse,id);
                float seconds=game.Plan.Source.Effects.GetCollapse(id).Seconds;
                Call(game,"TickSpecialTile",seconds-.01f);
                Check(game.State==JumpState.Ready,"collapse before deadline L"+id);
                Call(game,"TickSpecialTile",.011f);
                Check(game.State==JumpState.Dead && !Get<JumpBlock>(game,"_current").CloudRoot.gameObject.activeSelf,"collapse timeout L"+id);
                Cleanup(game); await UniTask.Yield();

                game=Make(TileKind.Collapse,id);
                Call(game,"TickSpecialTile",seconds-.1f);
                character=Get<JumpCharacter>(game,"_character"); next=Get<JumpBlock>(game,"_next");
                distance=Vector3.Distance(character.transform.position,next.TopCenter);
                Call(game,"DoJump",Mathf.InverseLerp(JumpConfig.MinJumpDist,JumpConfig.MaxJumpDist,distance));
                await Until(()=>game.State==JumpState.Ready,2);
                Call(game,"TickSpecialTile",10f);
                Check(game.LevelProgress==7 && game.State==JumpState.Ready,"collapse timely departure L"+id);
                Cleanup(game); await UniTask.Yield();

                game=Make(TileKind.Sprint,id);
                Set(game,"_combo",10); Set(game,"_maxCombo",10);
                var source=Get<JumpBlock>(game,"_current");
                Call(game,"LandOnBlock",source.TopCenter,source,0f,source.Size*.5f,true);
                int score=game.Score, combo=game.Combo;
                Check(game.State==JumpState.Airborne,"sprint input locked L"+id);
                Call(game,"BeginCharge"); Check(game.State==JumpState.Airborne,"sprint rejects charge");
                await UniTask.Delay(90);
                game.SetPaused(true); await UniTask.Yield();
                Vector3 frozen=Get<JumpCharacter>(game,"_character").transform.position;
                await UniTask.Delay(120,ignoreTimeScale:true);
                Check(Vector3.Distance(frozen,Get<JumpCharacter>(game,"_character").transform.position)<.0001f,"sprint pause");
                game.SetPaused(false);
                await Until(()=>game.State==JumpState.Ready,3);
                Check(game.LevelProgress==6+id,"sprint steps L"+id);
                Check(game.Score==score && game.Combo==combo && Get<int>(game,"_maxCombo")==combo,"sprint preserves score/combo");
                Check(Vector3.Distance(Get<JumpCharacter>(game,"_character").transform.position,Get<JumpBlock>(game,"_current").TopCenter)<.0001f,"sprint guaranteed center");
                Cleanup(game); await UniTask.Yield();
            }
            var restart=Make(TileKind.Sprint,5);
            Call(restart,"SprintAsync",restart.Plan.Source.Effects.GetSprint(5));
            await UniTask.Delay(80);
            Call(restart,"ResetWorld");
            await UniTask.Delay(1600);
            Check(restart.State==JumpState.Ready && restart.LevelProgress==0 && restart.Combo==0,"restart cancels old sprint");
            Cleanup(restart); await UniTask.Yield();
            var pause=Make(TileKind.Collapse,5);
            Call(pause,"OnApplicationPause",true);
            Call(pause,"OnTick");
            Check(Get<LevelTileEffectResolver>(pause,"_effectTimer").Elapsed==0,"background freezes timer");
            Call(pause,"OnApplicationPause",false);
            Cleanup(pause);
            Check(LevelRuntime.ReportRun(LevelRuntime.BuildPlan(Level(TileKind.Ice,1),1,true),99,999,true)==null,"playtest skips progress");
            var freeRetry=Make(TileKind.Ice,1);
            LevelRuntime.RequestPlaytest(LevelPackIo.ToJson(LevelPackIo.ToPackedLevel(freeRetry.Plan.Source)));
            var panel=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(UIResultViewSetup.PrefabPath)).GetComponent<PanelJumpResult>();
            Set(panel,"_shown",panel.victory);
            Check(Get<PlayerSaveData>(PlayerMgr.Instance,"_data")==null,"isolated test has no initialized wallet");
            Call(panel,"OnRetryClick");
            Check(Get<bool>(panel,"_actionBusy"),"playtest retry works without energy or wallet");
            Check(Get<PlayerSaveData>(PlayerMgr.Instance,"_data")==null,"playtest retry does not initialize wallet");
            var homeRoot=new GameObject("TestPlaytestHome"); homeRoot.SetActive(false);
            var home=homeRoot.AddComponent<UIHomeView>();
            Set(freeRetry,"_booted",true);
            int before=freeRetry.RunVersion;
            Call(home,"OnStartClicked");
            Check(freeRetry.RunVersion>before,"playtest session home entry is also free");
            UnityEngine.Object.Destroy(home.gameObject);
            LevelRuntime.ClearPlaytest(); Cleanup(freeRetry); UnityEngine.Object.Destroy(panel.gameObject);
            Debug.Log("SPECIAL_TILE_PLAY_PASS checks="+checks);
            EditorApplication.Exit(0);
        }
        catch(Exception error)
        { Debug.LogException(error); Time.timeScale=1; EditorApplication.Exit(1); }
    }
}
