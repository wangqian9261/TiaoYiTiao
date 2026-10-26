using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 背包弹窗（Popup 层，点遮罩关闭）：碎片 / 金币 / 积分 / 体力总览。
/// 碎片由砖上物品与转盘累积（PlayerMgr.Shards 持久化入 player_save）；
/// 皮肤合成功能预留位（收集向长线目标）。
/// </summary>
public class PanelBag : UIPanel
{
    public Text shardsText;
    public Text coinsText;
    public Text scoreText;
    public Text energyText;

    public override void OnOpen(object param = null)
    {
        EventCenter.AddListener(PlayerEvents.Changed, Refresh);
        Refresh();
    }

    public override void OnClose()
    {
        EventCenter.RemoveListener(PlayerEvents.Changed, Refresh);
    }

    private void Refresh()
    {
        var p = PlayerMgr.Instance;
        if (shardsText != null) shardsText.text = p.Shards.ToString();
        if (coinsText != null) coinsText.text = p.Coins.ToString();
        if (scoreText != null) scoreText.text = p.Score.ToString();
        if (energyText != null) energyText.text = $"{p.Energy}/{p.EnergyMax}";
    }
}
