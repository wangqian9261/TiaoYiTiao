using System;
using UnityEngine;
using Cysharp.Threading.Tasks;

/// <summary>
/// 全局管理器启动入口，游戏最开始执行，统一调用所有Mgr的OnInit。
/// 挂载在初始场景的空物体 GameLauncher 上（仅启动场景存在，管理器自带 DontDestroyOnLoad）。
/// </summary>
public class GameLauncher : MonoBehaviour
{
    private async void Start()
    {
        try
        {
            // 顺序初始化各个管理器，可按需增减
            await InitAllManagers();

            Debug.Log("===== 所有管理器初始化完成 =====");

            // 初始化完成后打开首页（正式主页 UIHomeView，替换演示 Panel_Home）
            await UIMgr.Instance.OpenPanel("Prefabs/UIHomeView", UILayer.Page);

            // 演示BGM：编辑器立即可播；微信构建在首触解锁后自动补播
            AudioMgr.Instance.PlayBgm("Audio/bgm_loop").Forget();
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
    }

    private async UniTask InitAllManagers()
    {
        // 资源管理器优先初始化
        ResMgr.Instance.OnInit();
        await UniTask.Yield();

        // UI管理器
        UIMgr.Instance.OnInit();
        await UniTask.Yield();

        // ===== Framework 管理器（按依赖顺序：平台门面先行，DataMgr存档依赖它） =====
        WeChatService.Init();
        PoolMgr.Instance.Init();
        DataMgr.Instance.Init();
        PlayerMgr.Instance.Init();   // 玩家基础数据（存档依赖 DataMgr，体力恢复依赖 MonoDriver）
        AudioMgr.Instance.Init();
        FloatTextMgr.Instance.Init();   // 全局飘字UI（事件驱动，常驻顶层Canvas）
        InputMgr.Instance.Init();   // 依赖 MonoDriver（首访问自动创建）
        DebugConsoleMgr.Init();     // 调试构建生效；Release 自动关闭
        await UniTask.Yield();

        // 流程状态机（放最后：立即进入 ProcedureLaunch，流程每帧推进一步）
        ProcedureMgr.Instance.Init();
        await UniTask.Yield();

        // 后续新增Mgr在这里继续添加
        // NetMgr.Instance.OnInit();
    }
}
