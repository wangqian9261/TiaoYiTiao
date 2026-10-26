using UnityEngine;
using UnityEngine.UI;
using Cysharp.Threading.Tasks;

public class PanelHome : UIPanel
{
    public Button openRewardBtn;
    public Button startJumpBtn;

    public override void OnCreate()
    {
        openRewardBtn.onClick.AddListener(OnOpenRewardClick);
        if (startJumpBtn != null)
            startJumpBtn.onClick.AddListener(OnStartJumpClick);
    }

    private void OnStartJumpClick()
    {
        JumpGame.StartGame();
    }

    private void OnOpenRewardClick()
    {
        // OpenPanel 自带防重复锁与栈内单实例守卫，直接发起即可
        UIMgr.Instance.OpenPanel("Prefabs/Panel_Reward", UILayer.Popup).Forget();
    }
}
