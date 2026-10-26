using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>局外背包资产生成；与特殊格局内玩法无关。</summary>
public static class JumpItemUiSetup
{
    private const string PrefabDir = "Assets/GameRes/Prefabs";

    // 深紫棕/奶白/金色文案（对齐主页与 HUD 既有配色）
    private static readonly Color CreamWhite = new Color(1f, 253f / 255f, 245f / 255f);
    private static readonly Color SoftBlue = new Color(0.72f, 0.78f, 0.92f);
    private static readonly Color Gold = new Color(1f, 0.85f, 0.35f);
    private static readonly Color DeepPlum = new Color(92f / 255f, 42f / 255f, 78f / 255f);

    [MenuItem("BagUI/1. Create Assets")]
    public static void CreateBag()
    {
        EnsureDir(PrefabDir);
        AssetDatabase.Refresh();

        string path = $"{PrefabDir}/PanelBag.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
        {
            var root = new GameObject("PanelBag", typeof(RectTransform), typeof(Image), typeof(PanelBag));
            Stretch((RectTransform)root.transform);
            var dim = root.GetComponent<Image>();
            dim.color = new Color(0.04f, 0.05f, 0.10f, 0.75f); // 点遮罩关闭（Popup 层默认语义）
            dim.raycastTarget = true;

            var boardGo = new GameObject("PanelBoard", typeof(RectTransform), typeof(Image));
            var board = (RectTransform)boardGo.transform;
            board.SetParent(root.transform, false);
            board.sizeDelta = new Vector2(720f, 900f);
            board.anchoredPosition = Vector2.zero;
            boardGo.GetComponent<Image>().color = new Color(0.10f, 0.13f, 0.22f, 0.96f);

            var title = AddText(board, "TitleText", "背包", new Vector2(0f, 390f), new Vector2(400f, 70f), 52, CreamWhite);
            AddShadow(title);

            var shardLabel = AddText(board, "ShardLabel", "皮肤碎片", new Vector2(0f, 255f), new Vector2(400f, 50f), 40, SoftBlue);
            var shardCount = AddText(board, "ShardsText", "0", new Vector2(0f, 150f), new Vector2(400f, 110f), 88, Gold);
            AddShadow(shardCount);
            AddShadow(shardLabel);

            AddText(board, "CoinRow", "金币", new Vector2(-150f, 40f), new Vector2(240f, 50f), 40, SoftBlue, TextAnchor.MiddleRight);
            var coins = AddText(board, "CoinsText", "0", new Vector2(150f, 40f), new Vector2(300f, 50f), 40, CreamWhite, TextAnchor.MiddleLeft);
            AddText(board, "ScoreRow", "积分", new Vector2(-150f, -60f), new Vector2(240f, 50f), 40, SoftBlue, TextAnchor.MiddleRight);
            var score = AddText(board, "ScoreText", "0", new Vector2(150f, -60f), new Vector2(300f, 50f), 40, CreamWhite, TextAnchor.MiddleLeft);
            AddText(board, "EnergyRow", "体力", new Vector2(-150f, -160f), new Vector2(240f, 50f), 40, SoftBlue, TextAnchor.MiddleRight);
            var energy = AddText(board, "EnergyText", "5/5", new Vector2(150f, -160f), new Vector2(300f, 50f), 40, CreamWhite, TextAnchor.MiddleLeft);
            AddShadow(coins); AddShadow(score); AddShadow(energy);

            var hint = AddText(board, "HintText", "集齐碎片可合成心仪皮肤（敬请期待）",
                new Vector2(0f, -330f), new Vector2(640f, 60f), 30, DeepPlum, TextAnchor.MiddleCenter);
            AddShadow(hint);

            var panel = root.GetComponent<PanelBag>();
            panel.shardsText = shardCount;
            panel.coinsText = coins;
            panel.scoreText = score;
            panel.energyText = energy;

            SavePrefab(root, path);
        }
        else Debug.Log("[JumpItemUiSetup] PanelBag 已存在，跳过（幂等）");

        AssetDatabase.SaveAssets();
        Debug.Log("[JumpItemUiSetup] 背包资产就绪");
    }

    // ---------- 构建辅助 ----------

    private static Text AddText(RectTransform parent, string name, string content, Vector2 pos, Vector2 size,
        int fontSize, Color color, TextAnchor alignment = TextAnchor.MiddleCenter)
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
        text.alignment = alignment;
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
        Debug.Log($"[JumpItemUiSetup] 已生成 {path}");
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
