using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using Cysharp.Threading.Tasks;

public class UIMgr : Singleton<UIMgr>, IInit
{
    private Dictionary<UILayer, Transform> _layerCanvasDic = new();
    private Transform _uiRoot;
    private Image _globalMask;
    private Button _maskBtn;

    private Dictionary<string, UIPanel> _panelCache = new();
    private Stack<UIPanel> _uiStack = new();
    // 防重复打开锁（加载期间拦截同 key 的重复请求）
    private HashSet<string> _openingLockSet = new HashSet<string>();
    // 栈顶关闭互斥：关闭动画未结束时拦截新的栈顶关闭，防止连点遮罩/按钮时连环 Pop
    private bool _closingTop;
    // PopAllToPage 重入保护
    private bool _poppingAll;

    public void OnInit()
    {
        // 幂等：场景重载导致 GameLauncher 再次调用时直接跳过
        if (_layerCanvasDic.Count > 0)
            return;

        var uiRootGo = GameObject.Find("UIRoot");
        if (uiRootGo == null)
        {
            Debug.LogError("[UIMgr] 场景中未找到 UIRoot，请先执行菜单 UIFramework/1. Build Scene Hierarchy");
            return;
        }
        _uiRoot = uiRootGo.transform;

        var maskTrans = _uiRoot.Find("CanvasMask/UIMask");
        if (maskTrans == null)
        {
            Debug.LogError("[UIMgr] 未找到 UIRoot/CanvasMask/UIMask，请先执行菜单 UIFramework/1. Build Scene Hierarchy");
            return;
        }
        _globalMask = maskTrans.GetComponent<Image>();
        _maskBtn = maskTrans.GetComponent<Button>();
        _globalMask.gameObject.SetActive(false);
        _maskBtn.onClick.AddListener(OnMaskClick);

        _layerCanvasDic.Add(UILayer.Page, _uiRoot.Find("CanvasPage"));
        _layerCanvasDic.Add(UILayer.Popup, _uiRoot.Find("CanvasPopup"));
        _layerCanvasDic.Add(UILayer.Tip, _uiRoot.Find("CanvasTip"));
        _layerCanvasDic.Add(UILayer.Guide, _uiRoot.Find("CanvasGuide"));
        _layerCanvasDic.Add(UILayer.Top, _uiRoot.Find("CanvasTop"));
    }

    /// <summary>
    /// 打开面板。入栈与遮罩刷新在打开动画之前完成，确保动画期间
    /// 关闭栈顶/点遮罩等操作作用于正确的面板（修复原设计动画期间栈顶错位竞态）。
    /// </summary>
    public async UniTask<UIPanel> OpenPanel(string panelKey, UILayer layer, object param = null)
    {
        if (_openingLockSet.Contains(panelKey))
        {
            Debug.LogWarning($"UI {panelKey} 正在打开，禁止重复调用");
            return null;
        }

        if (_panelCache.TryGetValue(panelKey, out UIPanel cached))
        {
            // 单实例语义：已在栈中说明面板处于打开状态，直接复用（修复重复 Push）
            if (_uiStack.Contains(cached))
            {
                Debug.LogWarning($"UI {panelKey} 已处于打开状态，忽略重复打开");
                return cached;
            }

            if (!TryGetLayerParent(layer, out var cachedParent))
                return null;

            cached.Layer = layer;
            cached.transform.SetParent(cachedParent, false);
            cached.gameObject.SetActive(true);
            cached.OnOpen(param);
            _uiStack.Push(cached);
            RefreshMaskState();
            await cached.OnOpenAnim();
            return cached;
        }

        // try/finally 保证防重复锁在任何异常路径下都会释放（修复原设计异常即永久锁死）
        _openingLockSet.Add(panelKey);
        try
        {
            if (!TryGetLayerParent(layer, out var parent))
                return null;

            GameObject prefab = await ResMgr.Instance.LoadAssetAsync<GameObject>(panelKey);
            if (prefab == null)
            {
                Debug.LogError($"[UIMgr] 面板prefab加载失败: {panelKey}");
                return null;
            }

            GameObject go = GameObject.Instantiate(prefab, parent, false);
            UIPanel panel = go.GetComponent<UIPanel>();
            if (panel == null)
            {
                Debug.LogError($"[UIMgr] 面板prefab缺少UIPanel组件: {panelKey}");
                Destroy(go);
                ResMgr.Instance.ReleaseAsset(panelKey);
                return null;
            }

            panel.PanelKey = panelKey;
            panel.Layer = layer;
            // prefab 由 UIMgr 加载，但记录在面板名下统一释放（谁加载谁记录）
            panel.TrackAsset(panelKey);

            panel.OnCreate();
            _panelCache.Add(panelKey, panel);

            panel.OnOpen(param);
            _uiStack.Push(panel);
            RefreshMaskState();
            await panel.OnOpenAnim();
            return panel;
        }
        finally
        {
            _openingLockSet.Remove(panelKey);
        }
    }

    /// <summary>关闭栈顶面板；带互斥锁，防止关闭动画期间连点导致连环 Pop 连关多层</summary>
    public async UniTask CloseTopPanel(bool destroy = false)
    {
        if (_uiStack.Count == 0) return;
        if (_closingTop) return;
        _closingTop = true;
        try
        {
            UIPanel panel = _uiStack.Pop();
            await ClosePanelInternal(panel, destroy);
        }
        finally
        {
            _closingTop = false;
        }
    }

    /// <summary>
    /// 按 key 关闭面板（允许关闭非栈顶面板）；面板会从栈中剔除，不留脏引用。
    /// 不在栈中（已关闭）的面板调用会被忽略，保证生命周期恰好执行一次。
    /// </summary>
    public async UniTask ClosePanel(string panelKey, bool destroy = false)
    {
        if (!_panelCache.TryGetValue(panelKey, out var panel)) return;
        if (!_uiStack.Contains(panel)) return;
        await ClosePanelInternal(panel, destroy);
    }

    private async UniTask ClosePanelInternal(UIPanel panel, bool destroy)
    {
        // 从栈中剔除该面板并保持其余面板相对顺序（底 -> 顶）
        var snapshot = _uiStack.ToArray(); // Stack.ToArray 为 顶 -> 底
        var newStack = new Stack<UIPanel>();
        for (int i = snapshot.Length - 1; i >= 0; i--)
        {
            if (snapshot[i] != panel)
                newStack.Push(snapshot[i]);
        }
        _uiStack = newStack;

        await panel.OnCloseAnim();

        // 关闭动画期间面板可能已被销毁（场景卸载/ClearAllPanel），不再继续生命周期
        if (panel == null)
        {
            RefreshMaskState();
            return;
        }

        panel.OnClose();

        if (destroy)
        {
            panel.DestroyPanel(); // DestroyPanel 内部不调用 OnClose，避免双调
            _panelCache.Remove(panel.PanelKey);
        }
        else
        {
            panel.gameObject.SetActive(false);
        }
        RefreshMaskState();
    }

    /// <summary>关闭所有弹窗，回退到底层Page页面；带重入保护</summary>
    public async UniTask PopAllToPage()
    {
        if (_poppingAll) return;
        _poppingAll = true;
        try
        {
            while (_uiStack.Count > 0)
            {
                var top = _uiStack.Peek();
                if (top.Layer == UILayer.Page)
                    break;
                await CloseTopPanel(true);
            }
        }
        finally
        {
            _poppingAll = false;
        }
    }

    private void OnMaskClick()
    {
        if (_uiStack.Count == 0) return;
        var topPanel = _uiStack.Peek();
        if (topPanel.Layer == UILayer.Top)
            return;
        // 既定语义：点击遮罩销毁栈顶普通弹窗（destroy=true 时缓存随之移除；
        // 缓存复用仅对 ClosePanel(key, destroy:false) 调用方生效）
        CloseTopPanel(true).Forget();
    }

    /// <summary>清空全部面板（场景切换等），生命周期各执行一次后销毁</summary>
    public void ClearAllPanel()
    {
        _uiStack.Clear();
        foreach (var kv in _panelCache)
        {
            kv.Value.OnClose();
            kv.Value.DestroyPanel();
        }
        _panelCache.Clear();
        _openingLockSet.Clear();
        _closingTop = false;
        _poppingAll = false;
        RefreshMaskState();
    }

    private void RefreshMaskState()
    {
        if (_globalMask == null) return;
        bool needMask = false;
        foreach (var panel in _uiStack)
        {
            // 仅 Popup/Guide 触发全局遮罩；Tip 飘字不应压暗全屏拦截点击
            if (panel.Layer == UILayer.Popup || panel.Layer == UILayer.Guide)
            {
                needMask = true;
                break;
            }
        }
        _globalMask.gameObject.SetActive(needMask);
    }

    private bool TryGetLayerParent(UILayer layer, out Transform parent)
    {
        if (_layerCanvasDic.TryGetValue(layer, out parent))
            return true;
        Debug.LogError($"[UIMgr] 层级未初始化: {layer}，请检查 UIRoot 搭建与 UIMgr.OnInit 调用顺序");
        return false;
    }

    public Transform GetLayerRoot(UILayer layer)
    {
        _layerCanvasDic.TryGetValue(layer, out var trans);
        return trans;
    }

    public void PrintUIMemoryLog()
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("===== UI资源内存日志 =====");
        foreach (var kv in _panelCache)
        {
            var panel = kv.Value;
            sb.AppendLine($"面板：{panel.PanelKey} | 层级：{panel.Layer} | 实例激活：{panel.gameObject.activeSelf}");
            foreach (var assetKey in panel.GetLoadedAssetKeys())
            {
                sb.AppendLine($"    - {assetKey}");
            }
        }
        sb.AppendLine("==========================");
        Debug.Log(sb.ToString());
    }
}
