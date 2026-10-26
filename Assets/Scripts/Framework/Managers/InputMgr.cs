using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>拖拽事件参数</summary>
public class DragArgs
{
    public Vector2 Position;
    public Vector2 Delta;
    public bool Canceled;
}

/// <summary>
/// 统一输入：屏幕 Tap 与拖拽（DragStart/Dragging/DragEnd）经 EventCenter 派发，
/// 兼容触摸与鼠标（微信小游戏触摸由 SDK 转 Unity Input，无需直连 wx 触摸事件）。
/// 首次触摸派发 EventKeys.FirstTouch（AudioMgr 的 WebGL 解锁依赖此事件）。
/// </summary>
public class InputMgr : PureSingleton<InputMgr>
{
    private bool _dragging;
    private Vector2 _lastPos;
    private bool _firstTouchSent;

    protected override void OnInit()
    {
        MonoDriver.Instance.UpdateTick += Poll;
    }

    private void Poll()
    {
        if (Input.touchCount > 0)
        {
            var t = Input.GetTouch(0);
            switch (t.phase)
            {
                case TouchPhase.Began:
                    NotifyTap(t.position);
                    BeginDrag(t.position);
                    break;
                case TouchPhase.Moved:
                case TouchPhase.Stationary:
                    UpdateDrag(t.position);
                    break;
                case TouchPhase.Ended:
                    EndDrag(t.position);
                    break;
                case TouchPhase.Canceled:
                    EndDrag(t.position, true);
                    break;
            }
        }
        else
        {
            if (Input.GetMouseButtonDown(0))
            {
                NotifyTap(Input.mousePosition);
                BeginDrag(Input.mousePosition);
            }
            else if (Input.GetMouseButton(0))
            {
                UpdateDrag(Input.mousePosition);
            }
            else if (Input.GetMouseButtonUp(0))
            {
                EndDrag(Input.mousePosition);
            }
        }
    }

    private void BeginDrag(Vector2 pos)
    {
        _dragging = true;
        _lastPos = pos;
        EventCenter.Dispatch(EventKeys.DragStart, new DragArgs { Position = pos, Delta = Vector2.zero });
    }

    private void UpdateDrag(Vector2 pos)
    {
        if (!_dragging) return;
        Vector2 delta = pos - _lastPos;
        _lastPos = pos;
        EventCenter.Dispatch(EventKeys.Dragging, new DragArgs { Position = pos, Delta = delta });
    }

    private void EndDrag(Vector2 pos, bool canceled = false)
    {
        if (!_dragging) return;
        _dragging = false;
        EventCenter.Dispatch(EventKeys.DragEnd, new DragArgs { Position = pos, Delta = pos - _lastPos, Canceled = canceled });
    }

    private void NotifyTap(Vector2 pos)
    {
        EventCenter.Dispatch(EventKeys.Tap, pos);
        if (_firstTouchSent) return;
        _firstTouchSent = true;
        EventCenter.Dispatch(EventKeys.FirstTouch);
    }

    /// <summary>GM/测试：模拟一次屏幕点击（含首触解锁语义）</summary>
    public void SimulateTap(Vector2 screenPos) => NotifyTap(screenPos);

    /// <summary>UI射线：返回命中的最上层UI物体（无则null）</summary>
    public GameObject RaycastUI(Vector2 screenPos)
    {
        if (EventSystem.current == null) return null;
        var ped = new PointerEventData(EventSystem.current) { position = screenPos };
        var results = new List<RaycastResult>();
        EventSystem.current.RaycastAll(ped, results);
        return results.Count > 0 ? results[0].gameObject : null;
    }

    /// <summary>世界射线：主相机点击命中物（无则null）</summary>
    public GameObject RaycastWorld(Vector2 screenPos)
    {
        var cam = Camera.main;
        if (cam == null) return null;
        var ray = cam.ScreenPointToRay(new Vector3(screenPos.x, screenPos.y, 0f));
        return Physics.Raycast(ray, out RaycastHit hit, 1000f) ? hit.collider.gameObject : null;
    }
}
