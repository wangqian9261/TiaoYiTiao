using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 跳一跳角色：骨骼动画表现；资源缺失时保留程序化棋子兜底。
/// 层级：JumpCharacterRoot(脚底定位) → Visual(形变) → ModelRoot(朝向) → [AI模型 或 程序化兜底棋子]。
/// AI 模型经 ResMgr 异步加载并按包围盒归一化；缺失时用兜底棋子，游戏永不阻塞。
/// </summary>
public class JumpCharacter : MonoBehaviour
{
    private Transform _visual;    // 形变层（缩放/摇晃/翻滚，脚底为原点）
    private Transform _modelRoot; // 朝向层
    private Transform _fallback;  // 兜底棋子（AI 模型加载成功后隐藏）
    private int _animVersion;     // 递增版本号：新动画使旧动画循环失效
    private JumpCharacterAnimation _animation;
    private string _pendingState = "Idle";
    private float _pendingPower, _pendingFlight = .5f, _pendingSince;

    public static JumpCharacter Create(Transform parent)
    {
        var root = new GameObject("JumpCharacter");
        root.transform.SetParent(parent, false);
        var c = root.AddComponent<JumpCharacter>();
        c.BuildLayers();
        c.BuildFallback();
        c.LoadModelAsync().Forget();
        return c;
    }

    private void BuildLayers()
    {
        _visual = new GameObject("Visual").transform;
        _visual.SetParent(transform, false);
        _modelRoot = new GameObject("ModelRoot").transform;
        _modelRoot.SetParent(_visual, false);
    }

    // ---------- 模型 ----------

    private async UniTaskVoid LoadModelAsync()
    {
        GameObject prefab = await ResMgr.Instance.LoadAssetAsync<GameObject>(JumpConfig.CharacterResKey);
        if (this == null)
        {
            if (prefab != null) ResMgr.Instance.ReleaseAsset(JumpConfig.CharacterResKey);
            return;
        }
        if (prefab == null) return; // 无 AI 模型：继续用兜底棋子

        var model = Instantiate(prefab, _modelRoot, false);
        model.name = "AIModel";
        // 清除加载期间兜底棋子的形变，避免影响骨骼模型的尺寸归一化。
        ClearOverlay();
        Normalize(model.transform, JumpConfig.CharacterHeight);
        _animation = model.GetComponentInChildren<JumpCharacterAnimation>(true);
        ApplyPendingAnimation();
        if (_fallback != null) _fallback.gameObject.SetActive(false);
        ResMgr.Instance.ReleaseAsset(JumpConfig.CharacterResKey); // 已实例化克隆，prefab 引用可放
        Debug.Log("[JumpCharacter] AI 角色模型已加载并归一化");
    }

    /// <summary>按包围盒归一化：脚底贴 y=0、高度缩放到 targetHeight、x/z 居中（主页展示台复用）</summary>
    internal static void Normalize(Transform model, float targetHeight)
    {
        var renderers = model.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return;
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        float scale = Mathf.Clamp(targetHeight / Mathf.Max(bounds.size.y, 0.001f), 0.01f, 100f);

        // 包一层容器做缩放与贴地，不改模型自身变换。
        // 偏移必须按"相对父容器"计算（父容器可能位于任意世界位置——砖上物品在方块定位后装载，
        // 若按世界包围盒直接赋局部偏移，物品会被整体甩到 -(方块世界坐标)×scale，离原点越远偏得越远）
        var holder = new GameObject("Normalized").transform;
        holder.SetParent(model.parent, false);
        model.SetParent(holder, false);
        holder.localScale = Vector3.one * scale;
        Transform parent = holder.parent;
        Vector3 relCenter = parent.InverseTransformPoint(bounds.center);
        Vector3 relBottom = parent.InverseTransformPoint(
            new Vector3(bounds.center.x, bounds.min.y, bounds.center.z));
        holder.localPosition = new Vector3(
            -relCenter.x * scale, -relBottom.y * scale, -relCenter.z * scale);
    }

    // ---------- 兜底棋子（棋子剪影 + 眼睛，仅运行时构建） ----------

    private void BuildFallback()
    {
        _fallback = new GameObject("FallbackPiece").transform;
        _fallback.SetParent(_modelRoot, false);
        // 兜底棋子按 0.55 基准手工建模，随 CharacterHeight 整体缩放保持与 AI 模型同尺寸
        _fallback.localScale = Vector3.one * (JumpConfig.CharacterHeight / 0.55f);
        Material bodyMat = JumpMaterials.Diffuse(JumpConfig.CharacterColor);
        Material eyeMat = JumpMaterials.Diffuse(Color.white);
        AddCylinder(_fallback, "Base", 0.165f, 0.24f, 0.12f, bodyMat);
        AddCylinder(_fallback, "Torso", 0.125f, 0.20f, 0.34f, bodyMat);
        AddSphere(_fallback, "Head", 0.135f, new Vector3(0f, 0.465f, 0f), bodyMat);
        AddSphere(_fallback, "EyeL", 0.028f, new Vector3(-0.055f, 0.49f, 0.115f), eyeMat);
        AddSphere(_fallback, "EyeR", 0.028f, new Vector3(0.055f, 0.49f, 0.115f), eyeMat);
    }

    private static void AddCylinder(Transform parent, string name, float radius, float height, float centerY, Material mat)
    {
        // Cylinder 原生网格：直径1、高2
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = name;
        var col = go.GetComponent<Collider>();
        if (col != null) Destroy(col);
        go.transform.SetParent(parent, false);
        go.transform.localScale = new Vector3(radius * 2f, height * 0.5f, radius * 2f);
        go.transform.localPosition = new Vector3(0f, centerY, 0f);
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
    }

    private static void AddSphere(Transform parent, string name, float radius, Vector3 pos, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = name;
        var col = go.GetComponent<Collider>();
        if (col != null) Destroy(col);
        go.transform.SetParent(parent, false);
        go.transform.localScale = Vector3.one * (radius * 2f);
        go.transform.localPosition = pos;
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
    }

    // ---------- 表现 API ----------

    public void SnapTo(Vector3 pos) => transform.position = pos;

    /// <summary>面向跳跃轴（+X 朝向 yaw=90，+Z yaw=0；模型默认面朝 +Z）</summary>
    public void FaceAxis(Vector3 axis)
    {
        if (_modelRoot == null) return;
        float yaw = Mathf.Abs(axis.x) > Mathf.Abs(axis.z) ? 90f : 0f;
        _modelRoot.localRotation = Quaternion.Euler(0f, yaw, 0f);
    }

    /// <summary>复位全部形变（重开 / 取消蓄力）</summary>
    public void ResetVisual()
    {
        Remember("Idle");
        ClearOverlay();
        if (_animation != null) _animation.ResetIdle();
    }

    private void ClearOverlay()
    {
        _animVersion++;
        if (_visual == null) return;
        _visual.localScale = Vector3.one;
        _visual.localRotation = Quaternion.identity;
    }

    /// <summary>蓄力压缩表现（每帧由主控推送，power 0~1）</summary>
    public void SetCharge(float power)
    {
        Remember("Charge");
        _pendingPower = Mathf.Clamp01(power);
        if (_animation != null)
        {
            ClearOverlay();
            _animation.SetCharge(_pendingPower);
            return;
        }
        if (_visual == null) return;
        float p = power * power * (3f - 2f * power); // smoothstep 让压缩起止更跟手
        float y = Mathf.Lerp(1f, JumpConfig.ChargeSquashMinY, p);
        float xz = Mathf.Lerp(1f, 1f + JumpConfig.ChargeWidenXZ, p);
        _visual.localScale = new Vector3(xz, y, xz);
    }

    /// <summary>起跳：先拉长再回弹</summary>
    public void PlayLaunch(float flightDuration = .5f)
    {
        Remember("Takeoff");
        _pendingFlight = flightDuration;
        if (_animation != null)
        {
            ClearOverlay();
            _animation.PlayTakeoff(flightDuration);
            return;
        }
        LaunchAsync(++_animVersion).Forget();
    }

    /// <summary>落地：挤压后弹回</summary>
    public void PlayLand()
    {
        Remember("Land");
        if (_animation != null)
        {
            ClearOverlay();
            _animation.PlayLand();
            return;
        }
        LandAsync(++_animVersion).Forget();
    }

    /// <summary>惊险边缘摇晃（仍存活）</summary>
    public void PlayWobble()
    {
        WobbleAsync(++_animVersion).Forget();
    }

    /// <summary>坠落翻滚（持续旋转直到 ResetVisual）</summary>
    public void PlayFall()
    {
        Remember("Fall");
        if (_animation != null)
        {
            ClearOverlay();
            _animation.PlayFall();
            return;
        }
        FallSpinAsync(++_animVersion).Forget();
    }

    public void PlayCelebrate()
    {
        Remember("Celebrate");
        ClearOverlay();
        if (_animation != null) _animation.PlayCelebrate();
    }

    private void Remember(string state)
    {
        _pendingState = state;
        _pendingSince = Time.time;
    }

    // 异步资源晚到时恢复当前表现，不能覆盖已经发生的落地/重开。
    private void ApplyPendingAnimation()
    {
        if (_animation == null) return;
        _animation.Restore(_pendingState, Time.time - _pendingSince, _pendingPower, _pendingFlight);
    }

    private async UniTaskVoid LaunchAsync(int v)
    {
        await Tween(v, _visual.localScale, new Vector3(0.88f, 1.28f, 0.88f), 0.10f);
        if (v != _animVersion || this == null) return;
        await Tween(v, new Vector3(0.88f, 1.28f, 0.88f), Vector3.one, 0.45f);
    }

    private async UniTaskVoid LandAsync(int v)
    {
        await Tween(v, _visual.localScale, new Vector3(1.16f, 0.74f, 1.16f), 0.09f);
        if (v != _animVersion || this == null) return;
        await Tween(v, new Vector3(1.16f, 0.74f, 1.16f), Vector3.one, 0.16f);
    }

    private async UniTaskVoid WobbleAsync(int v)
    {
        const float dur = 0.7f;
        float t = 0f;
        while (t < dur)
        {
            if (v != _animVersion || this == null || _visual == null) return;
            t += Time.deltaTime;
            float decay = 1f - t / dur;
            float angle = Mathf.Sin(t * 18f) * 11f * decay;
            _visual.localRotation = Quaternion.Euler(0f, 0f, angle);
            await UniTask.Yield();
        }
        if (v == _animVersion && this != null && _visual != null)
            _visual.localRotation = Quaternion.identity;
    }

    private async UniTaskVoid FallSpinAsync(int v)
    {
        while (v == _animVersion && this != null && _visual != null)
        {
            _visual.Rotate(Vector3.right, 520f * Time.deltaTime, Space.Self);
            await UniTask.Yield();
        }
    }

    private async UniTask Tween(int v, Vector3 from, Vector3 to, float dur)
    {
        float t = 0f;
        while (t < dur)
        {
            if (v != _animVersion || this == null || _visual == null) return;
            t += Time.deltaTime;
            _visual.localScale = Vector3.Lerp(from, to, Mathf.Clamp01(t / dur));
            await UniTask.Yield();
        }
        if (v == _animVersion && this != null && _visual != null)
            _visual.localScale = to;
    }
}
