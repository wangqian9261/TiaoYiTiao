using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using Codely.Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>Batch entry points. Throws on failure; never touches player saves.</summary>
public static class SpecialTileRegression
{
    static int checks;
    static void Check(bool ok, string label) { checks++; if (!ok) throw new Exception("SPECIAL_TILE_FAIL: " + label); }
    static void Near(float a, float b, string label) => Check(Mathf.Abs(a-b)<.0001f, label);
    public static void RunBaseline()
    {
        Check(LevelPackIo.SchemaVersion == 2, "schema 2");
        Check(string.Join(",", Enum.GetNames(typeof(TileKind))) == "Start,Normal,Ice,Collapse,Sprint,End", "six tile kinds");
        Debug.Log("SPECIAL_TILE_BASELINE_PASS");
    }
    static LevelDefinition Def(DifficultyTier tier = DifficultyTier.Hard, int n = 40, uint seed = 77)
        => new LevelDefinition { LevelId = 777, GridCount = n, Difficulty = tier, Seed = seed };
    static GeneratedLevel Generate(LevelDefinition def, SpecialTileSettings effects = null)
    {
        var gen = effects == null ? new LevelGenerator() : new LevelGenerator(effects);
        var level = gen.Generate(def, DifficultyProfiles.Get(def.Difficulty));
        Check(level != null && gen.LastReport.Pass, "generation " + def.Difficulty + "/" + def.GridCount + "/" + def.Seed + ": " +
            string.Join("; ", gen.LastReport.Issues.Select(i=>i.Message)));
        return level;
    }
    public static LevelDefinition Manual(TileKind kind, int grade, int n = 30, int index = 6)
    {
        var d = Def(DifficultyTier.Easy, n);
        d.Overrides = Enumerable.Range(1,n-2).Select(i=>new SpecialTileOverride {
            Index=i, Kind=i==index?kind:TileKind.Normal, EffectLevelId=i==index?grade:0 }).ToArray();
        return d;
    }
    static bool Rejected(LevelDefinition d, SpecialTileSettings e = null)
    { var g=e==null?new LevelGenerator():new LevelGenerator(e); g.Generate(d,DifficultyProfiles.Get(d.Difficulty)); return !g.LastReport.Pass; }

    [MenuItem("LevelSystem/10. Special Tiles Regression")]
    public static void RunAll()
    {
        checks=0; RunBaseline();
        var effects = new SpecialTileSettings();
        Check(effects.Validate().Pass, "default config");
        var missingConfig=new LevelGenerator(null);
        Check(missingConfig.Generate(Def(),DifficultyProfiles.Get(DifficultyTier.Hard))==null && !missingConfig.LastReport.Pass,"explicit missing config must not fall back");
        for (int id=1;id<=5;id++)
        {
            var ice=effects.GetIce(id); var collapse=effects.GetCollapse(id); var sprint=effects.GetSprint(id);
            Near(ice.StartDelay,new[]{.8f,.65f,.5f,.35f,.2f}[id-1],"ice delay");
            Near(ice.Speed,new[]{.15f,.2f,.25f,.3f,.35f}[id-1],"ice speed");
            Near(collapse.Seconds,new[]{2.5f,2.2f,1.9f,1.6f,1.3f}[id-1],"collapse seconds");
            Check(sprint.Steps==id,"sprint steps"); Near(sprint.ChargeSeconds,.35f,"charge"); Near(sprint.HopSeconds,.2f,"hop");
            var timer=new LevelTileEffectResolver();
            Near(timer.AdvanceIce(ice,ice.StartDelay),0,"ice waits");
            Near(timer.AdvanceIce(ice,.4f),ice.Speed*.4f,"ice forward");
            timer.Reset(); Check(!timer.AdvanceCollapse(collapse,collapse.Seconds-.01f),"collapse before deadline");
            Check(timer.AdvanceCollapse(collapse,.011f),"collapse at deadline");
            timer.Reset(); Near(timer.Elapsed,0,"leaving resets");
            var sprintPlan=LevelRuntime.BuildPlan(Generate(Manual(TileKind.Sprint,id)),1,true);
            Check(sprintPlan.EffectLevelIds[6]==id && sprintPlan.Kinds[6]==TileKind.Sprint,"plan preserves grade");
        }
        Check(!LevelTileEffectResolver.Outside(.49f,0,1) && LevelTileEffectResolver.Outside(.51f,0,1),"edge fall");
        effects.Ice = effects.Ice.Concat(new[]{new IceLevel { Id=6,StartDelay=.1f,Speed=.4f }}).ToArray();
        var weighted=Def(); weighted.SpecialRules=new[]{ new SpecialTileRule {Kind=TileKind.Ice,Weight=100,Levels=new[]{new EffectLevelWeight(6,100)}}};
        var extended=Generate(weighted,effects);
        Check(extended.Tiles.Where(t=>t.Kind==TileKind.Ice).All(t=>t.EffectLevelId==6),"grade 6 weights");
        Generate(Manual(TileKind.Ice,6),effects);
        Array.Reverse(effects.Ice);
        Check(Generate(weighted,effects).LayoutHash==extended.LayoutHash,"ID order independent");
        effects.Ice[0].Id=1; Check(!effects.Validate().Pass,"duplicate ID rejected");

        foreach (int index in new[]{-1,0,29,30,200})
        { var d=Def(DifficultyTier.Easy,30); d.Overrides=new[]{new SpecialTileOverride{Index=index}}; Check(Rejected(d),"bad override index "+index); }
        Check(Rejected(Manual(TileKind.Ice,99)),"unknown grade");
        Check(Rejected(Manual(TileKind.Start,0)),"start override");
        Check(Rejected(Manual(TileKind.Ice,1,index:5)),"initial safe zone");
        Check(Rejected(Manual(TileKind.Collapse,1,index:26)),"final safe zone");
        var conflict=Manual(TileKind.Sprint,5); conflict.Overrides[8].Kind=TileKind.Ice; conflict.Overrides[8].EffectLevelId=1;
        Check(Rejected(conflict),"sprint channel conflict");
        var adjacent=Manual(TileKind.Ice,1); adjacent.Overrides[6].Kind=TileKind.Collapse; adjacent.Overrides[6].EffectLevelId=1;
        Check(Rejected(adjacent),"adjacent specials");
        var two=Manual(TileKind.Sprint,1); two.Overrides[11].Kind=TileKind.Sprint; two.Overrides[11].EffectLevelId=1;
        Check(Rejected(two),"one sprint maximum");
        Check(Rejected(Manual(TileKind.Sprint,5,index:25)),"sprint overflow");
        var normal=Generate(Manual(TileKind.Normal,0)); Check(normal.Tiles.All(t=>!SpecialTiles.IsSpecial(t.Kind)),"normal override");

        var economy=ScriptableObject.CreateInstance<LevelEconomyConfig>();
        int[] combos={0,2,3,4,5,7,8,11,12,30}, percentages={0,0,10,10,25,25,50,50,100,100};
        for(int i=0;i<combos.Length;i++)
        {
            var reward=economy.Calculate(true,false,1.5f,combos[i]);
            Check(reward.BaseCoins==150 && reward.ComboCoins==Mathf.RoundToInt(150*percentages[i]/100f),"economy boundary "+combos[i]);
            Check(economy.Calculate(false,false,2,combos[i]).Total==0,"failed zero");
            Check(economy.Calculate(true,true,2,combos[i]).Total==0,"playtest zero");
        }
        UnityEngine.Object.DestroyImmediate(economy);

        var probe=Generate(Manual(TileKind.Collapse,4));
        var packed=LevelPackIo.ToPackedLevel(probe);
        Check(LevelPackIo.VerifyPackedLevel(packed),"valid packed level");
        Check(LevelPackIo.ToJson(LevelPackIo.ToPackedLevel(LevelPackIo.ToGeneratedLevel(packed)))==LevelPackIo.ToJson(packed),"domain roundtrip");
        var draft=LevelPackIo.ToDefinitionData(probe.Definition);
        Check(LevelPackIo.ToJson(LevelPackIo.ToDefinitionData(LevelPackIo.ToDefinition(LevelPackIo.FromJson<LevelDefinitionData>(LevelPackIo.ToJson(draft)))))==LevelPackIo.ToJson(draft),"draft rules overrides roundtrip");
        var obj=JObject.Parse(LevelPackIo.ToJson(packed));
        int mutations=0;
        foreach (var leaf in obj.Descendants().OfType<JValue>().ToArray())
        {
            if(leaf.Value==null) continue;
            var saved=leaf.Value;
            if(saved is bool b) leaf.Value=!b;
            else if(saved is string text) leaf.Value=text+"x";
            else if(saved is float || saved is double || saved is decimal) leaf.Value=Convert.ToDouble(saved)+.25;
            else leaf.Value=Convert.ToInt64(saved)+1;
            Check(!LevelPackIo.VerifyPackedLevel(LevelPackIo.FromJson<PackedLevelData>(obj.ToString())),"tamper "+leaf.Path);
            leaf.Value=saved; mutations++;
        }
        var pack=new LevelPackIo().Export(new[]{probe},"2.0.0-test");
        var parse=typeof(LevelRuntime).GetMethod("ParsePack",BindingFlags.NonPublic|BindingFlags.Static);
        Check(parse.Invoke(null,new object[]{pack.Data,pack.Version,pack.Sha256})!=null,"runtime schema2");
        pack.Data.buildTimeUtc++;
        Check(parse.Invoke(null,new object[]{pack.Data,pack.Version,pack.Sha256})==null,"package SHA rejects metadata tamper");
        pack.Data.schemaVersion=1;
        Check(parse.Invoke(null,new object[]{pack.Data,pack.Version,pack.Sha256})==null,"runtime rejects schema1");
        Check(!LevelBakePipeline.RollbackTo("1.0.0"),"cannot roll back schema1");
        LevelRuntime.RequestPlaytest(LevelPackIo.ToJson(packed));
        var transition=typeof(LevelPlaytestBootstrap).GetMethod("OnPlayModeChanged",BindingFlags.NonPublic|BindingFlags.Static);
        transition.Invoke(null,new object[]{PlayModeStateChange.ExitingEditMode});
        Check(LevelRuntime.HasPlaytestDraft,"entering Play retains requested preview");
        transition.Invoke(null,new object[]{PlayModeStateChange.ExitingPlayMode});
        Check(!LevelRuntime.HasPlaytestDraft,"exiting Play clears preview");

        int layouts=0; var simulator=new LevelSimulator();
        foreach(DifficultyTier tier in Enum.GetValues(typeof(DifficultyTier)))
        {
            var rates=new List<float>();
            foreach(int n in new[]{20,40,60})
            {
                for(uint seed=1;seed<=30;seed++)
                {
                    var level=Generate(Def(tier,n,seed*7919)); layouts++;
                    Check(Generate(Def(tier,n,seed*7919)).LayoutHash==level.LayoutHash,"deterministic");
                    Check(LevelPackIo.VerifyPackedLevel(LevelPackIo.ToPackedLevel(level)),"packed regression");
                    Check(level.Tiles.Count(t=>t.Kind==TileKind.Sprint)<=1,"sprint cap");
                    for(int j=1;j<=5;j++)Check(level.Tiles[j].Kind==TileKind.Normal,"opening safe");
                    for(int j=n-4;j<n-1;j++)Check(level.Tiles[j].Kind==TileKind.Normal,"finish safe");
                    if(seed<=3)
                    {
                        var sim=simulator.Simulate(level,1000,seed);
                        rates.Add(sim.ClearRate);
                        Debug.Log($"SIM {tier} n={n} seed={seed} rate={sim.ClearRate:P1} deaths="+string.Join(",",sim.DeathCauses.Select(kv=>$"{kv.Key}:{kv.Value}")));
                    }
                }
            }
            Debug.Log($"SIM_TIER {tier} mean={rates.Average():P1} target={DifficultyProfiles.TargetFirstTryRate(tier):P0}");
        }
        Debug.Log($"SPECIAL_TILE_REGRESSION_PASS checks={checks} layouts={layouts} mutations={mutations} simulations=45000");
    }

    public static void MigrateAndBake()
    {
        SpecialTileEditorConfig.LoadAsset();
        var economy=AssetDatabase.LoadAssetAtPath<LevelEconomyConfig>("Assets/GameRes/Config/LevelEconomyConfig.asset");
        EditorUtility.SetDirty(economy);
        UIResultViewSetup.CreateAll();
        // Remove the unused socket from the pooled block without replacing its authored cloud/platform.
        const string blockPath="Assets/GameRes/Jump/Block/jump_block.prefab";
        var root=PrefabUtility.LoadPrefabContents(blockPath);
        try {
            var socket=root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name=="ItemRoot");
            if(socket!=null) UnityEngine.Object.DestroyImmediate(socket.gameObject);
            PrefabUtility.SaveAsPrefabAsset(root,blockPath);
        } finally { PrefabUtility.UnloadPrefabContents(root); }
        foreach(var d in LevelDraftStore.LoadAll()) LevelDraftStore.Save(d);
        AssetDatabase.SaveAssets();
        var result=LevelBakePipeline.BakeAll("2.0.0",Debug.Log);
        if(!result.Success) throw new Exception(string.Join("\n",result.Failures));
        foreach(var line in result.SimSummary) Debug.Log(line);
        var imported=LevelBakePipeline.LoadPackForPreview("2.0.0");
        Check(imported!=null && imported.LevelCount==result.LevelCount,"reimport");
        Debug.Log($"SPECIAL_TILE_BAKE_PASS levels={result.LevelCount} sha256={result.PackSha256}");
    }

    public static void Calibrate()
    {
        var samples = new List<GeneratedLevel>();
        foreach (DifficultyTier tier in Enum.GetValues(typeof(DifficultyTier)))
            foreach (int n in new[] {20,40,60})
                for(uint s=1;s<=3;s++) samples.Add(Generate(Def(tier,n,s*7919)));
        foreach(float sigma in new[]{54f,56f,58f,60f,62f})
        {
            var sim=new LevelSimulator { SigmaHoldMs=sigma };
            foreach(DifficultyTier tier in Enum.GetValues(typeof(DifficultyTier)))
            {
                var rates=samples.Where(l=>l.Definition.Difficulty==tier).Select(l=>sim.Simulate(l,1000,l.Definition.Seed/7919).ClearRate);
                Debug.Log($"CALIBRATION sigma={sigma} tier={tier} rate={rates.Average():P1}");
            }
        }
    }

    public static void BuildAndAudit()
    {
        UIResultViewSetup.CreateAll();
        AssetDatabase.DeleteAsset("Assets/GameRes/Texture/Wheel");
        var target=(BuildTarget)Enum.Parse(typeof(BuildTarget),"MiniGame");
        UIFrameworkABBuilder.BuildAllBundles(target,"Assets/StreamingAssets/ab");
        Check(File.Exists("Assets/StreamingAssets/ab/ab"),"asset bundle build exists");
        foreach(var path in Directory.GetFiles("Assets/StreamingAssets/ab","*.manifest"))
        {
            string manifest=File.ReadAllText(path);
            Check(!manifest.Contains("Jump/Items/") && !manifest.Contains("Jump/Traps/") && !manifest.Contains("PanelLuckyWheel"),"legacy resources absent: "+path);
        }
        Check(!File.Exists("Assets/StreamingAssets/ab/jump_items") && !File.Exists("Assets/StreamingAssets/ab/jump_traps"),"obsolete bundles absent");
        Debug.Log("SPECIAL_TILE_RESOURCE_BUILD_PASS target="+target);
    }

    public static void CheckMalformedDraft()
    {
        // Isolated fixture in the worktree; always restore the index and remove test output.
        const string fixture=LevelDraftStore.DraftDir+"/level_special_tile_test_invalid.json";
        const string version="99.0.999";
        string pack=LevelBakePipeline.PackDir+"/pack_99_0_999.json";
        Check(!File.Exists(fixture) && !File.Exists(pack),"test paths unused");
        string index=File.ReadAllText(LevelBakePipeline.IndexPath());
        bool blocked=false;
        try {
            File.WriteAllText(fixture,"{ malformed json");
            var result=LevelBakePipeline.BakeAll(version);
            blocked=!result.Success && result.Failures.Any(f=>f.Contains("level_special_tile_test_invalid.json"));
        } finally {
            if(File.Exists(fixture))File.Delete(fixture);
            AssetDatabase.DeleteAsset(pack);
            AssetDatabase.DeleteAsset(LevelBakePipeline.PackPathSha(version));
            if(File.Exists(fixture+".meta"))File.Delete(fixture+".meta");
            File.WriteAllText(LevelBakePipeline.IndexPath(),index);
            AssetDatabase.ImportAsset(LevelBakePipeline.IndexPath());
        }
        Check(blocked,"malformed draft must block entire publish with filename");
        Debug.Log("SPECIAL_TILE_DRAFT_FAILURE_PASS");
    }

    public static void FinalVerification()
    {
        RunAll();
        CheckMalformedDraft();
        var index=LevelBakePipeline.LoadIndex();
        var pack=LevelBakePipeline.LoadPackForPreview(index.latest);
        Check(index.schemaVersion==2 && index.latest=="2.0.0" && pack.LevelCount==12,"12 rebaked schema2 levels");
        Check(pack.Levels.Select(l=>l.Definition.LevelId).SequenceEqual(LevelDraftStore.LoadAll().Select(d=>d.LevelId)),"IDs/order preserved");
        string json=File.ReadAllText(LevelBakePipeline.PackDir+"/pack_2_0_0.json");
        Check(LevelPackIo.ComputeSha256(json)==index.entries.First(e=>e.version==index.latest).sha256,"published SHA");
        Check(File.ReadAllText(LevelBakePipeline.PackPathSha("2.0.0")).Trim()==LevelPackIo.ComputeSha256(json),"SHA file");
        var parse=typeof(LevelRuntime).GetMethod("ParsePack",BindingFlags.NonPublic|BindingFlags.Static);
        Check(parse.Invoke(null,new object[]{pack.Data,"2.0.0",LevelPackIo.ComputeSha256(json)})!=null,"production package runtime import");
        BuildAndAudit();
        Debug.Log("SPECIAL_TILE_FINAL_VERIFICATION_PASS");
    }
}
