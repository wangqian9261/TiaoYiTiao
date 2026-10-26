using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 流程加载页驱动：把 Procedure 流程链映射到 UILoadingView 的进度与开关（纯事件驱动）。
/// 订阅 ProcedureEvents.Changed：进入 ProcedureLaunch 时打开加载页（Top 层），
/// 每个状态推进目标进度（Launch=0 → CheckVersion=0.2 → UpdateManifest=0.4 → Preload=0.6
/// → Login=0.8 → Game=1.0），进入 ProcedureGame 后等显示值追平且满足最短展示时长即关闭销毁。
/// 展示层平滑（MoveTowards）+ 最短展示时长，避免空流程一闪而过；将来状态接入真实耗时后同样适用。
/// 状态类与 GameLauncher 不感知 UI（Procedure.md 接入规范：状态机接口无需改动）。
/// </summary>
public static class ProcedureLoadingDriver
{
    private const string PanelKey = "Prefabs/UILoadingView";
    private const float FillSeconds = 0.45f;    // 显示值从 0 追到 1 的最短时长
    private const float MinShowSeconds = 0.8f; // 加载页最短展示时长

    // 固定流程链的目标进度（未登记的状态名不参与加载页，扩展新流程不受影响）
    private static readonly Dictionary<string, float> Targets = new Dictionary<string, float>
    {
        { "ProcedureLaunch", 0f },
        { "ProcedureCheckVersion", 0.2f },
        { "ProcedureUpdateManifest", 0.4f },
        { "ProcedurePreload", 0.6f },
        { "ProcedureLogin", 0.8f },
        { "ProcedureGame", 1f },
    };

    private static bool _subscribed;      // 事件只挂一次（跨播放防重复）
    private static bool _started;         // 本次播放是否已启动加载序列
    private static bool _closeRequested;  // 已进入最终状态，等待收尾关闭
    private static float _target;          // 目标进度
    private static UILoadingView _view;    // 当前加载页实例（打开完成前为 null）

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        // 会话状态复位（编辑器关闭域重载播放时静态会残留）
        _started = false;
        _closeRequested = false;
        _target = 0f;
        _view = null;

        if (_subscribed) return;
        _subscribed = true;
        EventCenter.AddListener<string>(ProcedureEvents.Changed, OnProcedureChanged);
    }

    private static void OnProcedureChanged(string procedureName)
    {
        if (!Targets.TryGetValue(procedureName, out float target)) return; // 未知状态：不参与
        _target = target;

        if (procedureName == "ProcedureGame")
            _closeRequested = true;

        if (!_started)
        {
            _started = true; // 首个已知流程事件即启动（正常时序为 Launch）
            RunAsync().Forget();
        }
    }

    private static async UniTaskVoid RunAsync()
    {
        var opened = await UIMgr.Instance.OpenPanel(PanelKey, UILayer.Top);
        _view = opened as UILoadingView;
        if (_view == null)
        {
            Debug.LogError("[ProcedureLoading] 加载页打开失败，流程加载展示停用");
            return;
        }
        Debug.Log("[ProcedureLoading] 打开加载页");

        float shown = 0f;  // 平滑后的显示进度
        float elapsed = 0f; // 打开面板至今的时长
        float rate = 1f / FillSeconds;

        while (true)
        {
            if (_view == null) return; // 面板被外部关闭/销毁，驱动就此退出

            float dt = Time.unscaledDeltaTime;
            elapsed += dt;
            shown = Mathf.MoveTowards(shown, _target, rate * dt);
            _view.SetProgress(shown);

            // 进入最终状态且填充追平、满足最短展示时长 → 关闭销毁
            if (_closeRequested && shown >= _target - 0.001f && elapsed >= MinShowSeconds)
                break;
            await UniTask.Yield();
        }

        await UIMgr.Instance.ClosePanel(PanelKey, true);
        _view = null;
        Debug.Log($"[ProcedureLoading] 加载页完成并关闭（展示 {elapsed:F2}s）");
    }
}
