using UnityEngine;

/// <summary>
/// 跳一跳方块：方块浮台（block_giftbox 礼盒模型，顶面 = y=0）+ 可选砖上物品/陷阱标记（静置 3D 模型贴块顶正中心摆放）。
/// 物品=停留 1.5s 自动触发；陷阱=落地即触发（预警外观：纯色染色/半透明覆盖贴图）。池化复用（IPoolable）。
/// 染色规则：普通块显示模型自带贴图（波点/装饰原样）；终点金/陷阱警戒色/幻影半透明用纯色材质覆盖（玩法信号）。
/// 结构：Root(本组件) / CloudRoot(浮台容器，沿用历史名——外部逻辑引用) / ItemRoot(标记容器，物品与陷阱互斥共用)。
/// 落点判定仍是 Size×Size 的 y=0 顶面；Setup 按 Size 整体缩放 CloudRoot。
/// </summary>
public class JumpBlock : MonoBehaviour, IPoolable
{
    public Transform CloudRoot;
    public Renderer[] CloudRenderers; // 浮台渲染器：贴图态还原模型材质 / 纯色态共享一块实例材质
    public Transform ItemRoot;        // 物品/陷阱标记容器（静置摆放，无浮动/自转）
    public GameObject platformPrefab; // 浮台平台模型（编辑器工具接线；引用缺失兜底重建用）

    public int Index;
    public float Size;                  // 顶面边长（x/z 同尺寸）
    public JumpItemKind ItemKind;       // 本砖携带的物品（None=无）
    public JumpTrapKind TrapKind;       // 本砖携带的陷阱（None=无；与物品互斥）
    public bool ItemConsumed;           // 物品是否已被触发收取
    public bool TrapConsumed;           // 陷阱是否已触发

    /// <summary>顶面中心（y=0 平面）</summary>
    public Vector3 TopCenter => new Vector3(transform.position.x, 0f, transform.position.z);

    private Material _cloudMat;      // 特殊块覆盖材质（终点金/陷阱色/幻影半透明；池化复用）
    private bool _cloudMatTransparent;
    private Material[] _originalMats; // 模型自带材质缓存（普通块贴图态；池化在纯色↔贴图间往返还原）

    public void OnSpawn()
    {
    }

    public void OnDespawn()
    {
        ClearItemModel();
        ItemConsumed = false;
        ItemKind = JumpItemKind.None;
        TrapConsumed = false;
        TrapKind = JumpTrapKind.None;
    }

    /// <summary>布置方块浮台：整体缩放 / 材质（textured=普通块用模型自带贴图；否则纯色覆盖，phantom=半透明幻影预警）/ 标记容器归位</summary>
    public void Setup(int index, float size, Color color, bool phantom = false, bool textured = false)
    {
        Index = index;
        Size = size;

        if (CloudRoot == null || CloudRenderers == null || CloudRenderers.Length == 0 || ItemRoot == null)
        {
            // 池模板缺序列化引用（异常兜底/模板升级）时就地重建结构
            BuildPlatform();
        }

        if (CloudRoot != null)
        {
            // 塌陷过的块回收复用：云簇曾被隐藏/缩小，重用时恢复显示
            if (!CloudRoot.gameObject.activeSelf) CloudRoot.gameObject.SetActive(true);
            CloudRoot.localScale = Vector3.one * size;
        }
        if (ItemRoot != null)
        {
            // 标记静置：贴块顶摆放，不浮动不自转（每帧无动画开销）
            ItemRoot.localPosition = new Vector3(0f, JumpConfig.ItemFloatHeight, 0f);
            ItemRoot.localRotation = Quaternion.identity;
        }

        if (CloudRenderers != null && CloudRenderers.Length > 0)
        {
            if (textured && _originalMats != null && _originalMats.Length == CloudRenderers.Length)
            {
                // 普通块：还原模型自带贴图材质（波点/装饰原样展示；纯色块回收复用时由此恢复）
                for (int i = 0; i < CloudRenderers.Length; i++) CloudRenderers[i].sharedMaterial = _originalMats[i];
            }
            else
            {
                // 特殊块（终点金/陷阱警戒色/幻影半透明）纯色覆盖（池化复用避免每块新建材质）
                if (_cloudMat == null || _cloudMatTransparent != phantom)
                {
                    _cloudMat = phantom
                        ? JumpMaterials.TransparentDiffuse(color, JumpConfig.PhantomAlpha)
                        : JumpMaterials.Diffuse(color);
                    _cloudMatTransparent = phantom;
                }
                else
                {
                    // 复用同形态材质：幻影须还原基线透明度（闪烁只改 alpha，Setup 重置）
                    var c = color;
                    if (phantom) c.a = JumpConfig.PhantomAlpha;
                    _cloudMat.color = c;
                }
                for (int i = 0; i < CloudRenderers.Length; i++) CloudRenderers[i].sharedMaterial = _cloudMat;
            }
        }
    }

    /// <summary>幻影闪烁预警（progress01 0→1）：透明度在基线与最低值间脉冲（塌陷倒计时驱动）</summary>
    public void SetPhantomBlink(float progress01)
    {
        if (_cloudMat == null) return;
        float pulse = 0.5f + 0.5f * Mathf.Sin(progress01 * Mathf.PI * 10f);
        var c = _cloudMat.color;
        c.a = Mathf.Lerp(JumpConfig.PhantomAlpha, JumpConfig.PhantomBlinkAlphaMin, pulse);
        _cloudMat.color = c;
    }

    private static Material _sealGhostMat;

    /// <summary>封印轮廓化：剩余物品模型换幽灵材质——保留轮廓、隐藏颜色与发光（实际奖励不变）</summary>
    public void SealItemVisuals()
    {
        if (ItemRoot == null) return;
        if (_sealGhostMat == null)
            _sealGhostMat = JumpMaterials.TransparentDiffuse(
                new Color(0.12f, 0.11f, 0.14f), JumpConfig.SealGhostAlpha);
        var renderers = ItemRoot.GetComponentsInChildren<MeshRenderer>();
        for (int i = 0; i < renderers.Length; i++)
            renderers[i].sharedMaterial = _sealGhostMat;
    }

    /// <summary>
    /// 装配方块浮台：实例化 block_giftbox 模型并归一（footprint 1×1、顶面贴合 y=0、身体在下方）；
    /// ItemRoot 为空容器（无几何）。platformPrefab 缺失的极端兜底：单位薄盒。
    /// </summary>
    public void BuildPlatform()
    {
        if (CloudRoot == null)
        {
            var cloud = new GameObject("CloudRoot"); // 容器名沿用历史（外部逻辑引用）
            cloud.transform.SetParent(transform, false);
            CloudRoot = cloud.transform;

            if (platformPrefab != null)
            {
                var platform = Instantiate(platformPrefab, CloudRoot, false);
                platform.name = "Platform";
                RemoveColliders(platform);
                NormalizePlatform(platform.transform);
                CloudRenderers = platform.GetComponentsInChildren<MeshRenderer>();
                _originalMats = new Material[CloudRenderers.Length];
                for (int i = 0; i < CloudRenderers.Length; i++) _originalMats[i] = CloudRenderers[i].sharedMaterial;
            }
            else
            {
                var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
                box.name = "FallbackBox";
                RemoveColliders(box);
                box.transform.SetParent(CloudRoot, false);
                box.transform.localScale = new Vector3(1f, 0.4f, 1f);
                box.transform.localPosition = new Vector3(0f, -0.2f, 0f);
                box.GetComponent<MeshRenderer>().sharedMaterial = JumpMaterials.Diffuse(Color.white);
                CloudRenderers = new[] { box.GetComponent<MeshRenderer>() };
                _originalMats = new[] { box.GetComponent<MeshRenderer>().sharedMaterial };
            }
        }

        if (ItemRoot == null)
        {
            var item = new GameObject("ItemRoot");
            item.transform.SetParent(transform, false);
            item.transform.localPosition = new Vector3(0f, JumpConfig.ItemFloatHeight, 0f);
            ItemRoot = item.transform;
        }
    }

    /// <summary>
    /// 浮台归一：footprint 缩放到 1×1（x/z 取大边）、几何居中、顶面贴合 y=0（落点面，身体在下方）。
    /// 包围盒取世界坐标并以浮台根为原点换算（构建时容器位于方块原点，世界≈局部）。
    /// </summary>
    private static void NormalizePlatform(Transform platform)
    {
        var renderers = platform.GetComponentsInChildren<MeshRenderer>();
        if (renderers.Length == 0) return;
        var b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);

        Vector3 relCenter = b.center - platform.position; // 包围盒中心相对浮台根
        float footprint = Mathf.Max(b.size.x, b.size.z);
        float s = footprint > 0.001f ? 1f / footprint : 1f;
        platform.localScale = Vector3.one * s;
        // 缩放后几何中心落在 (0, -厚/2, 0)：x/z 居中，顶面恰好 y=0
        platform.localPosition = new Vector3(
            -relCenter.x * s,
            -b.size.y * s * 0.5f - relCenter.y * s,
            -relCenter.z * s);
    }

    /// <summary>装载物品模型克隆（归一化到 ItemVisualSize 高、贴 ItemRoot 原点；prefab 缺席则静默无演出）</summary>
    public void ShowItemModel(GameObject prefab)
    {
        ClearItemModel();
        if (ItemRoot == null || prefab == null) return;
        var model = Instantiate(prefab, ItemRoot, false);
        model.name = "ItemModel";
        JumpCharacter.Normalize(model.transform, JumpConfig.ItemVisualSize);
    }

    /// <summary>清空物品模型克隆（池化回收/收取时调用）</summary>
    public void ClearItemModel()
    {
        if (ItemRoot == null) return;
        for (int i = ItemRoot.childCount - 1; i >= 0; i--)
        {
            var child = ItemRoot.GetChild(i).gameObject;
            if (Application.isPlaying) Destroy(child);
            else DestroyImmediate(child);
        }
    }

    /// <summary>物品触发：收走模型（撒花/音效/效果由调用方负责）</summary>
    public void PlayItemCollect()
    {
        ItemConsumed = true;
        ClearItemModel();
    }

    /// <summary>移除层级内全部碰撞体（模型自带碰撞体会干扰落点判定；编辑器构建与运行时双路径）</summary>
    private static void RemoveColliders(GameObject go)
    {
        var cols = go.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < cols.Length; i++)
        {
            if (Application.isPlaying) Destroy(cols[i]);
            else DestroyImmediate(cols[i]);
        }
    }
}
