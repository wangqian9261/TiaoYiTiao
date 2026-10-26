using UnityEngine;
using Cysharp.Threading.Tasks;

public class UIPanel : AssetLoader
{
    [Header("面板资源Key")]
    public string PanelKey;
    [Header("UI层级")]
    public UILayer Layer;

    public virtual void OnCreate() { }

    public virtual void OnOpen(object param = null) { }

    /// <summary>打开动画，异步，动画结束后返回；默认无动画</summary>
    public virtual UniTask OnOpenAnim()
    {
        return UniTask.CompletedTask;
    }

    public virtual void OnClose() { }

    /// <summary>关闭动画，异步，动画结束后返回；默认无动画</summary>
    public virtual UniTask OnCloseAnim()
    {
        return UniTask.CompletedTask;
    }

    /// <summary>
    /// 销毁面板。OnClose 生命周期由 UIMgr 统一调用恰好一次，此处不再重复调用（修复原设计双调）。
    /// </summary>
    public virtual void DestroyPanel()
    {
        Destroy(gameObject);
    }
}
