using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

public static class SpecialTileVisualCheck
{
    public static void VerifyEverything()
    { SpecialTileRegression.FinalVerification(); RenderAndPlay(); }
    public static void RenderAndPlay()
    { Render(); SpecialTilePlayRegression.Start(); }

    public static void Render()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        RenderSettings.ambientLight=Color.white;
        var light=new GameObject("Key").AddComponent<Light>(); light.type=LightType.Directional;
        light.transform.rotation=Quaternion.Euler(40,-30,0);
        var camera=new GameObject("EvidenceCamera").AddComponent<Camera>();
        camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=new Color(.09f,.12f,.18f);
        camera.orthographic=true; camera.orthographicSize=2.7f;
        camera.transform.position=new Vector3(0,7,-7); camera.transform.LookAt(new Vector3(0,0,.7f));
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/GameRes/Jump/Block/jump_block.prefab");
        var types=new[]{TileKind.Ice,TileKind.Collapse,TileKind.Sprint};
        for(int i=0;i<3;i++)
        {
            var go=UnityEngine.Object.Instantiate(prefab); go.transform.position=new Vector3((i-1)*2.4f,0,0);
            var block=go.GetComponent<JumpBlock>(); block.Setup(6,1.6f,types[i],5,Vector3.forward);
            if(i==1)block.SetCollapseProgress(.65f);
            if(i==2)block.ShowSprintPath(new[]{go.transform.position,go.transform.position+Vector3.forward*2});
            var label=new GameObject("Label").AddComponent<TextMesh>();
            label.text=SpecialTiles.Name(types[i])+" L5"; label.font=AssetDatabase.LoadAssetAtPath<Font>("Assets/GameRes/Fonts/HuayangBattle-SC-Medium.otf");
            label.GetComponent<MeshRenderer>().sharedMaterial=label.font.material;
            label.fontSize=40; label.characterSize=.08f; label.anchor=TextAnchor.MiddleCenter;
            label.transform.position=go.transform.position+new Vector3(0,.1f,-1.2f); label.transform.rotation=camera.transform.rotation;
            // Reuse test: collapse must not poison a later normal pooled spawn.
            if(i==0)
            {
                var test=UnityEngine.Object.Instantiate(prefab).GetComponent<JumpBlock>();
                test.Setup(6,1,TileKind.Collapse,1,Vector3.forward); test.Collapse(); test.OnDespawn();
                test.Setup(1,1,TileKind.Normal,0,Vector3.forward);
                if(!test.CloudRoot.gameObject.activeSelf || test.Kind!=TileKind.Normal)throw new Exception("Pool reset failed");
                UnityEngine.Object.DestroyImmediate(test.gameObject);
            }
        }
        Capture(camera,1200,700,"/private/tmp/special-tiles-surfaces.png");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        camera=new GameObject("ResultCamera").AddComponent<Camera>();
        camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=new Color(.16f,.12f,.20f);
        camera.transform.position=new Vector3(0,0,-10);
        var resultPrefab=AssetDatabase.LoadAssetAtPath<GameObject>(UIResultViewSetup.PrefabPath);
        var panel=UnityEngine.Object.Instantiate(resultPrefab).GetComponent<PanelJumpResult>();
        panel.overlayCanvas.renderMode=RenderMode.ScreenSpaceCamera;
        panel.overlayCanvas.worldCamera=camera; panel.overlayCanvas.planeDistance=1;
        var rt=new RenderTexture(750,1334,24); camera.targetTexture=rt;
        Canvas.ForceUpdateCanvases();
        panel.OnOpen(new JumpResultArgs {LevelCleared=true,Score=320,CoinsGained=300,BaseCoins=150,ComboCoins=150,MaxCombo=12});
        panel.reveal.alpha=1;
        Canvas.ForceUpdateCanvases();
        Debug.Log($"VISUAL_SIZE screen={Screen.width}x{Screen.height} safe={Screen.safeArea} canvas={((RectTransform)panel.transform).rect} designScale={panel.panelRoot.localScale}");
        // Batch Editor uses a 640x480 GameView; render the authored portrait layout in world space.
        panel.overlayCanvas.renderMode=RenderMode.WorldSpace;
        var canvasRect=(RectTransform)panel.transform;
        canvasRect.sizeDelta=new Vector2(750,1334); canvasRect.position=Vector3.zero; canvasRect.localScale=Vector3.one;
        camera.orthographic=true; camera.orthographicSize=667;
        panel.panelRoot.localScale=Vector3.one;
        panel.panelRoot.anchoredPosition=Vector2.zero;
        if(!panel.victory.breakdown.text.Contains("150") || panel.victory.breakdown.font.name!="HuayangBattle-SC-Medium")
            throw new Exception("Result breakdown binding failed");
        Capture(camera,750,1334,"/private/tmp/special-tiles-result.png");
        panel.OnOpen(new JumpResultArgs {LevelCleared=false,Score=99,MaxCombo=7});
        if(panel.defeat.coins.text!="7" || panel.defeat.coinIcon.activeSelf || panel.defeat.breakdown.gameObject.activeSelf)
            throw new Exception("Failed run must show combo instead of coin reward");
        panel.reveal.alpha=1; panel.panelRoot.localScale=Vector3.one; panel.panelRoot.anchoredPosition=Vector2.zero;
        Capture(camera,750,1334,"/private/tmp/special-tiles-failure.png");
        UnityEngine.Object.DestroyImmediate(rt);
        Debug.Log("SPECIAL_TILE_VISUAL_RENDER_PASS");
    }
    private static void Capture(Camera camera,int width,int height,string path)
    {
        var rt=new RenderTexture(width,height,24); camera.targetTexture=rt;
        Canvas.ForceUpdateCanvases(); camera.Render();
        var previous=RenderTexture.active; RenderTexture.active=rt;
        var image=new Texture2D(width,height,TextureFormat.RGB24,false);
        image.ReadPixels(new Rect(0,0,width,height),0,0); image.Apply();
        File.WriteAllBytes(path,image.EncodeToPNG());
        RenderTexture.active=previous; camera.targetTexture=null;
        UnityEngine.Object.DestroyImmediate(image); UnityEngine.Object.DestroyImmediate(rt);
    }
}
