using UnityEngine;
using UnityEngine.UI;
using Cysharp.Threading.Tasks;

public class PanelReward : UIPanel
{
    public Image rewardIcon;
    public RectTransform panelRoot;
    public Button closeBtn;

    public override async void OnCreate()
    {
        // 先注册回调再 await：面板加载途中被销毁时 CTS 返回 null，后续赋值全部跳过
        closeBtn.onClick.AddListener(OnCloseClick);

        var sprite = await LoadAsset<Sprite>("Texture/Reward_Gold");
        if (sprite != null)
        {
            rewardIcon.sprite = sprite;
        }
    }

    private void OnCloseClick()
    {
        UIMgr.Instance.CloseTopPanel(true).Forget();
    }

    public override void OnOpen(object param = null)
    {
        panelRoot.localScale = Vector3.zero;
    }

    public override async UniTask OnOpenAnim()
    {
        // 0.3秒从0放大到1，纯UniTask时间插值（unscaled：游戏暂停时动画继续）
        float duration = 0.3f;
        float t = 0;
        while (t < duration)
        {
            if (panelRoot == null) return; // 动画期间面板被销毁（关闭/清场）时安全退出
            t += Time.unscaledDeltaTime;
            float progress = Mathf.Clamp01(t / duration);
            panelRoot.localScale = Vector3.Lerp(Vector3.zero, Vector3.one, progress);
            await UniTask.Yield();
        }
        panelRoot.localScale = Vector3.one;
    }

    public override async UniTask OnCloseAnim()
    {
        float duration = 0.25f;
        float t = 0;
        Vector3 start = panelRoot.localScale;
        while (t < duration)
        {
            if (panelRoot == null) return;
            t += Time.unscaledDeltaTime;
            float progress = Mathf.Clamp01(t / duration);
            panelRoot.localScale = Vector3.Lerp(start, Vector3.zero, progress);
            await UniTask.Yield();
        }
        panelRoot.localScale = Vector3.zero;
    }
}
