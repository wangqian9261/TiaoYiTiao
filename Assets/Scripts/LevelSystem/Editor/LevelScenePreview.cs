using UnityEditor;
using UnityEngine;

/// <summary>
/// Scene 视图 3D 路线预览：编辑器窗口打开且开关生效时，把当前生成关卡以线框 + 标签
/// 叠加绘制到 Scene 视图（不生成、不保存任何场景对象）。颜色口径与 2D 预览一致。
/// </summary>
[InitializeOnLoad]
public static class LevelScenePreview
{
    private static GeneratedLevel _level;
    private static bool _enabled = true;

    static LevelScenePreview()
    {
        SceneView.duringSceneGui += OnSceneGui;
    }

    public static void SetLevel(GeneratedLevel level)
    {
        _level = level;
        SceneView.RepaintAll();
    }

    public static bool Enabled
    {
        get => _enabled;
        set
        {
            _enabled = value;
            SceneView.RepaintAll();
        }
    }

    private static void OnSceneGui(SceneView view)
    {
        if (!_enabled || _level == null || _level.Tiles == null || _level.Tiles.Count == 0) return;

        var tiles = _level.Tiles;
        var restoreColor = Handles.color;

        // 路线连线 + 蓄力标注
        for (int j = 0; j < tiles.Count - 1; j++)
        {
            var from = new Vector3(tiles[j].X, 0f, tiles[j].Z);
            var to = new Vector3(tiles[j + 1].X, 0f, tiles[j + 1].Z);
            Handles.color = new Color(0.5f, 0.5f, 0.55f, 0.8f);
            Handles.DrawLine(from + Vector3.up * 0.02f, to + Vector3.up * 0.02f);
            float charge = LevelMath.ChargeMsAtDist(Vector2.Distance(
                new Vector2(from.x, from.z), new Vector2(to.x, to.z)));
            Handles.Label(Vector3.Lerp(from, to, 0.5f) + Vector3.up * 0.4f,
                $"{charge:F0}ms", LevelEditorStyles.ChargeLabel);
        }

        // 瓦片线框 + 标签
        for (int i = 0; i < tiles.Count; i++)
        {
            var t = tiles[i];
            var center = new Vector3(t.X, 0f, t.Z);
            float half = t.Size * 0.5f;
            Handles.color = KindColor(t);
            var corners = new Vector3[]
            {
                center + new Vector3(-half, 0.02f, -half),
                center + new Vector3(half, 0.02f, -half),
                center + new Vector3(half, 0.02f, half),
                center + new Vector3(-half, 0.02f, half),
                center + new Vector3(-half, 0.02f, -half),
            };
            Handles.DrawPolyLine(corners);

            string tag = TileTag(t);
            if (!string.IsNullOrEmpty(tag))
                Handles.Label(center + Vector3.up * 0.25f, $"{i}\n{tag}", LevelEditorStyles.TileLabel(i));
            else
                Handles.Label(center + Vector3.up * 0.25f, i.ToString(), LevelEditorStyles.TileLabel(i));
        }

        Handles.color = restoreColor;
    }

    public static Color KindColor(GeneratedTile t)
    {
        switch (t.Kind)
        {
            case TileKind.Start: return new Color(0.35f, 0.9f, 0.45f);
            case TileKind.End: return new Color(1f, 0.84f, 0.25f);
            case TileKind.Reward: return new Color(0.55f, 0.85f, 1f);
            case TileKind.Trap: return new Color(1f, 0.42f, 0.42f);
            default: return new Color(0.85f, 0.85f, 0.9f);
        }
    }

    public static string TileTag(GeneratedTile t)
    {
        switch (t.Kind)
        {
            case TileKind.Reward: return "奖:" + RewardShort(t.Reward);
            case TileKind.Trap: return "陷:" + TrapShort(t.Trap);
            case TileKind.Start: return "起点";
            case TileKind.End: return "终点";
            default: return "";
        }
    }

    private static string RewardShort(RewardKind kind)
    {
        switch (kind)
        {
            case RewardKind.LuckyWheel: return "转盘";
            case RewardKind.ScoreBoost: return "增益";
            case RewardKind.Coin: return "金币";
            case RewardKind.SkinShard: return "碎片";
            case RewardKind.Shield: return "护盾";
            case RewardKind.Sprint: return "冲刺";
            default: return "?";
        }
    }

    private static string TrapShort(TrapKind kind)
    {
        switch (kind)
        {
            case TrapKind.Slippery: return "打滑";
            case TrapKind.Phantom: return "幻影";
            case TrapKind.SlowCharge: return "减速";
            case TrapKind.Shake: return "震荡";
            case TrapKind.Burn: return "燃烧";
            case TrapKind.Reverse: return "反向";
            case TrapKind.Seal: return "封印";
            default: return "?";
        }
    }
}

/// <summary>Scene 预览标签样式缓存（每格编号错开色调，便于与连线对照）</summary>
public static class LevelEditorStyles
{
    private static GUIStyle _tileLabel;
    private static GUIStyle _chargeLabel;

    public static GUIStyle TileLabel(int index)
    {
        if (_tileLabel == null)
        {
            _tileLabel = new GUIStyle(EditorStyles.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                fontSize = 12,
            };
            _tileLabel.normal.textColor = Color.white;
        }
        // 编号奇偶微调亮度，避免重叠瓦片标签混淆
        _tileLabel.normal.textColor = index % 2 == 0 ? Color.white : new Color(0.85f, 0.9f, 1f);
        return _tileLabel;
    }

    public static GUIStyle ChargeLabel
    {
        get
        {
            if (_chargeLabel == null)
            {
                _chargeLabel = new GUIStyle(EditorStyles.miniLabel)
                {
                    alignment = TextAnchor.MiddleCenter,
                };
                _chargeLabel.normal.textColor = new Color(0.75f, 0.75f, 0.85f);
            }
            return _chargeLabel;
        }
    }
}
