using System;

/// <summary>DemoConfig 配置表 DTO（JsonUtility 要求字段公开）</summary>
[Serializable]
public class DemoConfig
{
    public int coinPerLevel;
    public int maxHp;
    public string tips;
}

/// <summary>DemoPlayerSave 玩家存档 DTO</summary>
[Serializable]
public class DemoPlayerSave
{
    public int level;
    public int coin;
    public string playerName;
}
