using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 跳一跳资产一键生成（全幂等，可重复执行）：
/// - GameRes/Jump/Block/jump_block.prefab（方块池模板：block_giftbox 礼盒浮台 + ItemRoot 物品容器）
/// - GameRes/Prefabs/Panel_JumpResult.prefab（Page 层结果页）
/// - Panel_Home.prefab 增补"跳一跳 · 开始"按钮（已存在则只补序列化引用）
/// 生成路径均在 GameRes 下：编辑器模拟加载与微信 AB 构建同构。
/// 模板版本升级：检测到旧版（坐垫/云朵瓣簇/浮岛板/礼物盒装饰/爱心彩蛋）时自动删除重建。
/// 2026-10-14：云朵浮台替换为 block_cushion 坐垫模型（旧云朵瓣簇/BlockBase.mat/jump_cloud.jpeg 已清理）。
/// 2026-10-15：坐垫替换为 block_giftbox 礼盒方块（普通块显示模型贴图，终点/陷阱纯色覆盖）。
/// 关卡 HUD 已移交 UIBattleViewSetup（菜单 BattleUI/1，生成 UIBattleView.prefab 替换旧 Panel_JumpHud）。
/// </summary>
public static class JumpGameSetup
{
    private const string BlockDir = "Assets/GameRes/Jump/Block";
    private const string PrefabDir = "Assets/GameRes/Prefabs";

    [MenuItem("JumpGame/1. Create Assets")]
    public static void CreateAll()
    {
        EnsureDir(BlockDir);
        EnsureDir(PrefabDir);
        AssetDatabase.Refresh();

        // 旧云朵资产清理（云朵瓣簇基材 / 云朵参考图）
        DeleteAssetIfExists($"{BlockDir}/BlockBase.mat");
        DeleteAssetIfExists($"{BlockDir}/jump_cloud.jpeg");
        // 旧装饰材质清理（礼物盒遗留 / 爱心彩蛋砖遗留——砖上物品已改为独立 3D 模型悬浮）
        DeleteAssetIfExists($"{BlockDir}/GiftGold.mat");
        DeleteAssetIfExists($"{BlockDir}/GiftRibbon.mat");
        DeleteAssetIfExists($"{BlockDir}/HeartHot.mat");

        // ---------- 方块池模板（旧结构：坐垫/云朵瓣簇/浮岛板/爱心彩蛋 → 自动升级为礼盒版） ----------
        string blockPath = $"{BlockDir}/jump_block.prefab";
        var existingBlock = AssetDatabase.LoadAssetAtPath<JumpBlock>(blockPath);
        if (existingBlock != null && (existingBlock.CloudRoot == null || existingBlock.ItemRoot == null
            || existingBlock.platformPrefab == null)) // 旧模板（含 cushionPrefab 命名版）无 platformPrefab 引用 → 升级重建
        {
            AssetDatabase.DeleteAsset(blockPath);
            Debug.Log("[JumpGameSetup] 检测到旧版方块模板（坐垫/云朵结构），已删除并升级为 block_giftbox 礼盒版");
            existingBlock = null;
        }
        if (existingBlock == null)
        {
            var platformPrefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{BlockDir}/block_giftbox.prefab");
            if (platformPrefab == null)
            {
                Debug.LogError("[JumpGameSetup] 缺少 block_giftbox.prefab（GameRes/Jump/Block，礼盒方块平台模型）");
                return;
            }
            var root = new GameObject("jump_block");
            var block = root.AddComponent<JumpBlock>();
            block.platformPrefab = platformPrefab;
            block.BuildPlatform(); // 礼盒归一（footprint 1×1、顶面 y=0）+ ItemRoot 空容器；运行时 Setup 按 Size 缩放
            PrefabUtility.SaveAsPrefabAsset(root, blockPath);
            Object.DestroyImmediate(root);
            Debug.Log($"[JumpGameSetup] 已生成 {blockPath}（block_giftbox 礼盒版）");
        }

        // ---------- HUD（Top 层）已移交 UIBattleViewSetup（菜单 BattleUI/1）----------
        // v3 关卡 UI（UIBattleView）替换旧 Panel_JumpHud，本工具不再生成 HUD。

        // ---------- 结果页（Page 层） ----------
        string resultPath = $"{PrefabDir}/Panel_JumpResult.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(resultPath) == null)
        {
            var root = new GameObject("Panel_JumpResult", typeof(RectTransform), typeof(Image), typeof(PanelJumpResult));
            Stretch((RectTransform)root.transform);
            var dim = root.GetComponent<Image>();
            dim.color = new Color(0.04f, 0.05f, 0.10f, 0.82f); // 压暗死亡世界但不全遮
            dim.raycastTarget = true; // 挡穿透，防误触世界输入

            var panelRoot = new GameObject("PanelRoot", typeof(RectTransform), typeof(Image));
            var boardRect = (RectTransform)panelRoot.transform;
            boardRect.SetParent(root.transform, false);
            boardRect.sizeDelta = new Vector2(640f, 820f);
            boardRect.anchoredPosition = Vector2.zero;
            panelRoot.GetComponent<Image>().color = new Color(0.10f, 0.13f, 0.22f, 0.96f);

            var title = AddText(boardRect, "TitleText", "游戏结束", new Vector2(0f, 320f), new Vector2(560f, 70f), 46, Color.white);
            var score = AddText(boardRect, "ScoreText", "0", new Vector2(0f, 200f), new Vector2(560f, 130f), 92, Color.white);
            var best = AddText(boardRect, "BestText", "最高纪录 0", new Vector2(0f, 105f), new Vector2(560f, 50f), 30, new Color(0.72f, 0.78f, 0.92f));
            var stats = AddText(boardRect, "StatsText", "", new Vector2(0f, 45f), new Vector2(560f, 44f), 26, new Color(0.62f, 0.68f, 0.82f));
            var encourage = AddText(boardRect, "EncourageText", "", new Vector2(0f, -25f), new Vector2(560f, 50f), 28, new Color(1f, 0.85f, 0.55f));

            var retry = AddButton(boardRect, "RetryBtn", "再来一局", new Vector2(0f, -170f), new Vector2(460f, 112f), new Color(0.98f, 0.42f, 0.52f), 42);
            var home = AddButton(boardRect, "HomeBtn", "回主页", new Vector2(0f, -315f), new Vector2(330f, 90f), new Color(0.25f, 0.30f, 0.42f), 34);

            var panel = root.GetComponent<PanelJumpResult>();
            panel.panelRoot = boardRect;
            panel.titleText = title;
            panel.scoreText = score;
            panel.bestText = best;
            panel.statsText = stats;
            panel.encourageText = encourage;
            panel.retryBtn = retry;
            panel.homeBtn = home;

            SavePrefab(root, resultPath);
        }

        // ---------- Panel_Home 增补开始按钮 ----------
        PatchHomePrefab();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[JumpGameSetup] 跳一跳资产全部就绪（方块模板 / HUD / 结果页 / 首页按钮）");
    }

    /// <summary>Panel_Home 加"跳一跳·开始"按钮（存在则跳过；引用未序列化时补齐）</summary>
    private static void PatchHomePrefab()
    {
        string path = $"{PrefabDir}/Panel_Home.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
        {
            Debug.LogWarning("[JumpGameSetup] 未找到 Panel_Home.prefab（请先执行 UIFramework/2. Create Demo Prefabs）");
            return;
        }

        var contents = PrefabUtility.LoadPrefabContents(path);
        try
        {
            var rootRect = contents.GetComponent<RectTransform>();
            if (rootRect.Find("StartJumpBtn") == null)
            {
                AddButton(rootRect, "StartJumpBtn", "跳一跳 · 开始",
                    new Vector2(0f, 20f), new Vector2(460f, 120f), new Color(0.98f, 0.45f, 0.55f), 42);
                Debug.Log("[JumpGameSetup] Panel_Home 已添加开始按钮");
            }

            var panel = contents.GetComponent<PanelHome>();
            if (panel != null)
            {
                var btnComp = rootRect.Find("StartJumpBtn")?.GetComponent<Button>();
                var so = new SerializedObject(panel);
                var prop = so.FindProperty("startJumpBtn");
                if (prop != null && prop.objectReferenceValue == null && btnComp != null)
                {
                    prop.objectReferenceValue = btnComp;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
            }

            PrefabUtility.SaveAsPrefabAsset(contents, path);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }
    }

    /// <summary>关卡 HUD 已移交 UIBattleViewSetup（菜单 BattleUI/1）——旧 Panel_JumpHud 补丁通道随 v3 替换一并移除</summary>

    // ---------- 构建辅助 ----------

    private static Text AddText(RectTransform parent, string name, string content, Vector2 pos, Vector2 size, int fontSize, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text));
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.sizeDelta = size;
        rect.anchoredPosition = pos;
        var text = go.GetComponent<Text>();
        text.text = content;
        text.font = GetFont();
        text.fontSize = fontSize;
        text.color = color;
        text.alignment = TextAnchor.MiddleCenter;
        text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        return text;
    }

    private static void AddShadow(Text text)
    {
        var shadow = text.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.65f);
        shadow.effectDistance = new Vector2(2f, -2f);
    }

    private static Button AddButton(RectTransform parent, string name, string label, Vector2 pos, Vector2 size, Color btnColor, int fontSize)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.sizeDelta = size;
        rect.anchoredPosition = pos;
        go.GetComponent<Image>().color = btnColor;

        var labelGo = new GameObject("Label", typeof(RectTransform), typeof(Text));
        var labelRect = (RectTransform)labelGo.transform;
        labelRect.SetParent(rect, false);
        labelRect.sizeDelta = size;
        labelRect.anchoredPosition = Vector2.zero;
        var text = labelGo.GetComponent<Text>();
        text.text = label;
        text.font = GetFont();
        text.fontSize = fontSize;
        text.color = Color.white;
        text.alignment = TextAnchor.MiddleCenter;
        text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        return go.GetComponent<Button>();
    }

    private static void DeleteAssetIfExists(string path)
    {
        if (AssetDatabase.LoadAssetAtPath<Object>(path) != null)
        {
            AssetDatabase.DeleteAsset(path);
            Debug.Log($"[JumpGameSetup] 已清理旧资产: {path}");
        }
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static void SavePrefab(GameObject root, string path)
    {
        PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
        Debug.Log($"[JumpGameSetup] 已生成 {path}");
    }

    private static void EnsureDir(string dir)
    {
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
    }

    private static Font GetFont()
    {
        try { return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); }
        catch { return Resources.GetBuiltinResource<Font>("Arial.ttf"); }
    }
}
