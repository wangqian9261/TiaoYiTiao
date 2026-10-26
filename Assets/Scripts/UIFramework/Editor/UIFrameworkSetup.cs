using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// UIFramework 场景与演示资产一键搭建工具。
/// 菜单1：在当前场景构建 UIRoot 六层Canvas + UIMask + GameLauncher；
/// 菜单2：生成 GameRes 下的演示面板 Prefab 与占位图标。
/// 两个菜单均幂等，可重复执行。
/// </summary>
public static class UIFrameworkSetup
{
    private const string PrefabDir = "Assets/GameRes/Prefabs";
    private const string TextureDir = "Assets/GameRes/Texture";

    [MenuItem("UIFramework/1. Build Scene Hierarchy")]
    public static void BuildSceneHierarchy()
    {
        var scene = EditorSceneManager.GetActiveScene();

        // 移除模板遗留的测试 Canvas（白色测试图，SortOrder 0 会垫底渲染）
        var oldCanvas = GameObject.Find("Canvas");
        if (oldCanvas != null && oldCanvas.transform.parent == null)
        {
            Object.DestroyImmediate(oldCanvas);
            Debug.Log("[UIFrameworkSetup] 已移除旧测试 Canvas");
        }

        // UIRoot 容器（无Canvas，仅组织层级）
        var uiRoot = GameObject.Find("UIRoot");
        if (uiRoot == null)
            uiRoot = new GameObject("UIRoot");
        var rootTrans = uiRoot.transform;

        // 六个 Overlay Canvas。CanvasMask(15) 的语义：
        //   Page(10) 被遮罩挡住；Popup(20) 空白区域点击透到遮罩实现"点遮罩关闭"；
        //   Tip/Guide/Top(30/40/50) 在遮罩之上不受遮罩影响。
        // 修复原设计 UIMask 裸挂在 UIRoot 下（无 Canvas，Image 不渲染、Button 无法 Raycast）的问题。
        CreateLayerCanvas(rootTrans, "CanvasPage", 10);
        var maskCanvas = CreateLayerCanvas(rootTrans, "CanvasMask", 15);
        CreateLayerCanvas(rootTrans, "CanvasPopup", 20);
        CreateLayerCanvas(rootTrans, "CanvasTip", 30);
        CreateLayerCanvas(rootTrans, "CanvasGuide", 40);
        CreateLayerCanvas(rootTrans, "CanvasTop", 50);

        // UIMask：半透明黑 + Button 捕获点击，默认隐藏
        var maskTrans = rootTrans.Find("CanvasMask/UIMask");
        if (maskTrans == null)
        {
            var maskGo = new GameObject("UIMask", typeof(Image), typeof(Button));
            maskGo.transform.SetParent(maskCanvas, false);
            Stretch((RectTransform)maskGo.transform);
            var img = maskGo.GetComponent<Image>();
            img.color = new Color(0f, 0f, 0f, 0.6f);
            img.raycastTarget = true;
            maskGo.SetActive(false);
        }

        // EventSystem 兜底
        if (Object.FindObjectOfType<EventSystem>() == null)
        {
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            Debug.Log("[UIFrameworkSetup] 已创建 EventSystem");
        }

        // GameLauncher
        if (GameObject.Find("GameLauncher") == null)
        {
            var launcher = new GameObject("GameLauncher");
            launcher.AddComponent<GameLauncher>();
            Debug.Log("[UIFrameworkSetup] 已创建 GameLauncher");
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[UIFrameworkSetup] 场景层级搭建完成并已保存: " + scene.path);
    }

    [MenuItem("UIFramework/2. Create Demo Prefabs")]
    public static void CreateDemoPrefabs()
    {
        EnsureDir(PrefabDir);
        EnsureDir(TextureDir);
        AssetDatabase.Refresh();

        // 占位金币图标（64x64 圆形，纯代码生成，导入为 Sprite）
        string goldPath = $"{TextureDir}/Reward_Gold.png";
        if (!File.Exists(goldPath))
        {
            File.WriteAllBytes(goldPath, MakeGoldCoinPng(64));
            AssetDatabase.ImportAsset(goldPath);
        }
        var importer = AssetImporter.GetAtPath(goldPath) as TextureImporter;
        if (importer != null && importer.textureType != TextureImporterType.Sprite)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.SaveAndReimport();
        }

        // ---------- Panel_Home（Page 层演示） ----------
        if (AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDir}/Panel_Home.prefab") == null)
        {
            var root = new GameObject("Panel_Home", typeof(RectTransform), typeof(Image), typeof(PanelHome));
            Stretch((RectTransform)root.transform);
            var bg = root.GetComponent<Image>();
            bg.color = new Color(0.08f, 0.12f, 0.20f, 1f);
            bg.raycastTarget = true;

            AddText((RectTransform)root.transform, "Title", "Home", new Vector2(0, 500), new Vector2(600, 120), 64, Color.white);
            var openBtn = AddButton((RectTransform)root.transform, "OpenRewardBtn", "Open Reward",
                new Vector2(0, -200), new Vector2(420, 120));
            root.GetComponent<PanelHome>().openRewardBtn = openBtn;

            SavePrefab(root, $"{PrefabDir}/Panel_Home.prefab");
        }

        // ---------- Panel_Reward（Popup 层演示：入场缩放动画 + 异步图标加载） ----------
        if (AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDir}/Panel_Reward.prefab") == null)
        {
            var root = new GameObject("Panel_Reward", typeof(RectTransform), typeof(PanelReward));
            Stretch((RectTransform)root.transform);

            var panelRootGo = new GameObject("panelRoot", typeof(RectTransform), typeof(Image));
            panelRootGo.transform.SetParent(root.transform, false);
            var panelRoot = (RectTransform)panelRootGo.transform;
            panelRoot.anchorMin = panelRoot.anchorMax = new Vector2(0.5f, 0.5f);
            panelRoot.sizeDelta = new Vector2(600, 800);
            var board = panelRootGo.GetComponent<Image>();
            board.color = new Color(0.22f, 0.18f, 0.14f, 1f);
            board.raycastTarget = true;

            var iconGo = new GameObject("rewardIcon", typeof(RectTransform), typeof(Image));
            iconGo.transform.SetParent(panelRoot, false);
            var iconRect = (RectTransform)iconGo.transform;
            iconRect.anchorMin = iconRect.anchorMax = new Vector2(0.5f, 0.65f);
            iconRect.sizeDelta = new Vector2(160, 160);
            var icon = iconGo.GetComponent<Image>();
            icon.raycastTarget = false; // sprite 由 OnCreate 异步加载

            AddText(panelRoot, "RewardTitle", "Reward", new Vector2(0, 150), new Vector2(400, 80), 56, Color.white);
            var closeBtn = AddButton(panelRoot, "CloseBtn", "X", new Vector2(240, 340), new Vector2(80, 80));

            var comp = root.GetComponent<PanelReward>();
            comp.panelRoot = panelRoot;
            comp.rewardIcon = icon;
            comp.closeBtn = closeBtn;

            SavePrefab(root, $"{PrefabDir}/Panel_Reward.prefab");
        }

        AssetDatabase.SaveAssets();
        Debug.Log("[UIFrameworkSetup] 演示Prefab与资源生成完成");
    }

    private static Transform CreateLayerCanvas(Transform parent, string name, int sortOrder)
    {
        var found = parent.Find(name);
        if (found != null)
        {
            ConfigureCanvas(found.gameObject, sortOrder);
            return found;
        }

        var go = new GameObject(name, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        go.transform.SetParent(parent, false);
        ConfigureCanvas(go, sortOrder);
        return go.transform;
    }

    private static void ConfigureCanvas(GameObject go, int sortOrder)
    {
        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortOrder;

        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080, 1920);
        scaler.matchWidthOrHeight = 0.5f;
    }

    private static void SavePrefab(GameObject root, string path)
    {
        PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
        Debug.Log($"[UIFrameworkSetup] 已生成 {path}");
    }

    private static Button AddButton(RectTransform parent, string name, string label, Vector2 pos, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var rect = (RectTransform)go.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = pos;
        rect.sizeDelta = size;

        var img = go.GetComponent<Image>();
        img.color = new Color(0.20f, 0.45f, 0.80f, 1f);
        img.raycastTarget = true;

        AddText(rect, "Text", label, Vector2.zero, size, 40, Color.white);
        return go.GetComponent<Button>();
    }

    private static Text AddText(RectTransform parent, string name, string content, Vector2 pos, Vector2 size, int fontSize, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        var rect = (RectTransform)go.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = pos;
        rect.sizeDelta = size;

        var text = go.GetComponent<Text>();
        text.font = GetBuiltInFont();
        text.fontSize = fontSize;
        text.color = color;
        text.alignment = TextAnchor.MiddleCenter;
        text.text = content;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        return text;
    }

    private static Font GetBuiltInFont()
    {
        try
        {
            return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }
        catch
        {
            return Resources.GetBuiltinResource<Font>("Arial.ttf");
        }
    }

    private static byte[] MakeGoldCoinPng(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var gold = new Color(1f, 0.78f, 0.22f);
        var rim = new Color(0.78f, 0.56f, 0.10f);
        float c = (size - 1) / 2f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x - c, dy = y - c;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                Color col = r > c ? Color.clear : (r > c * 0.9f ? rim : gold);
                tex.SetPixel(x, y, col);
            }
        }
        tex.Apply();
        byte[] png = tex.EncodeToPNG();
        Object.DestroyImmediate(tex);
        return png;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static void EnsureDir(string dir)
    {
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);
    }
}
