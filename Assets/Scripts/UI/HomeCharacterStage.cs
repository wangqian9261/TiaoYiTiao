using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 主页 3D 角色展示台（运行时程序化，零 prefab 资产）：
/// 隔离舞台（y=-300 远离游戏区）+ 专用相机渲染角色到透明 RenderTexture，
/// 经 RawImage 叠在主页背景的蛋糕舞台上（视觉上"站在舞台"）。
/// 世界隔离靠距离：方块区 y≥-10 且主相机永不俯视到此，展示相机 far=60 也不及游戏区。
/// 生命周期由 UIHomeView 驱动：Ensure（面板启用）→ SetVisible(false)（面板关闭）→ Dispose（面板销毁）。
/// </summary>
public class HomeCharacterStage : MonoBehaviour
{
    private const float StageY = -300f;    // 舞台世界高度（远离游戏区）
    private const float ShowHeight = 1.1f; // 角色归一化高度（游戏内 0.55 的两倍特写）
    private const int RtWidth = 512;
    private const int RtHeight = 768;

    private Camera _cam;
    private RenderTexture _rt;
    private Transform _modelRoot;
    private bool _disposed;
    private bool _visibleIntent = true;
    private bool _hasSkeletalAnimation;

    public bool Ready { get; private set; }
    public bool HasCharacter { get; private set; }
    public RenderTexture Texture => _rt;

    /// <summary>创建舞台（构建期间整体隐藏，避免归一化前的原点闪现）</summary>
    public static HomeCharacterStage Create()
    {
        var root = new GameObject("HomeCharacterStage");
        root.SetActive(false);
        var stage = root.AddComponent<HomeCharacterStage>();
        stage.BuildAsync().Forget();
        return stage;
    }

    private async UniTaskVoid BuildAsync()
    {
        GameObject prefab = null;
        try
        {
            _rt = new RenderTexture(RtWidth, RtHeight, 24, RenderTextureFormat.ARGB32)
            {
                filterMode = FilterMode.Bilinear,
            };
            _rt.Create();

            // 展示相机：透明底（与背景图自然合成），只拍舞台近景
            var camGo = new GameObject("StageCamera");
            camGo.transform.SetParent(transform, false);
            camGo.transform.localPosition = new Vector3(0f, ShowHeight * 0.5f, -2.9f);
            _cam = camGo.AddComponent<Camera>();
            _cam.targetTexture = _rt;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            _cam.fieldOfView = 32f;
            _cam.orthographic = true;
            _cam.orthographicSize = ShowHeight * .55f;
            _cam.nearClipPlane = 0.1f;
            _cam.farClipPlane = 60f;

            // 灯光：点光（range 受限，不越界照到游戏区）——主光暖白 + 背光薰衣草
            AddPointLight("KeyLight", new Vector3(1.2f, 2.2f, -1.6f), new Color(1f, 0.94f, 0.88f), .65f, 6f);
            AddPointLight("RimLight", new Vector3(-1.4f, 1.8f, -2.2f), new Color(0.85f, 0.72f, 0.97f), .35f, 6f);

            // 角色：与游戏内同款模型（归一化基于世界包围盒，须在舞台移入隔离区前的原点完成）
            _modelRoot = new GameObject("ModelRoot").transform;
            _modelRoot.SetParent(transform, false);
            _modelRoot.localPosition = Vector3.zero;

            prefab = await ResMgr.Instance.LoadAssetAsync<GameObject>(JumpConfig.CharacterResKey);
            if (_disposed)
            {
                return;
            }
            if (prefab != null)
            {
                var model = Instantiate(prefab, _modelRoot, false);
                model.name = "AIModel";
                JumpCharacter.Normalize(model.transform, ShowHeight);
                _hasSkeletalAnimation = model.GetComponentInChildren<JumpCharacterAnimation>(true) != null;
                // 角色朝 +Z；展示相机在 -Z，首次展示正面，再缓速转台。
                if (_hasSkeletalAnimation) _modelRoot.localRotation = Quaternion.Euler(0f, 180f, 0f);
                HasCharacter = model.GetComponentInChildren<Renderer>(true) != null;
            }
            else
            {
                Debug.LogWarning("[HomeCharacterStage] 角色模型缺失，展示台仅渲染空舞台");
            }

            // 归一化完成后整体移入隔离区（相机/灯光/模型局部偏移随根平移）
            transform.position = new Vector3(0f, StageY, 0f);
            Ready = true;
            gameObject.SetActive(_visibleIntent);
            Debug.Log($"[HomeCharacterStage] 展示台就绪（{(prefab != null ? "角色已归一化" : "角色缺席")}）");
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
        }
        finally
        {
            if (prefab != null) ResMgr.Instance.ReleaseAsset(JumpConfig.CharacterResKey);
        }
    }

    private void AddPointLight(string name, Vector3 localPos, Color color, float intensity, float range)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = localPos;
        var light = go.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = color;
        light.intensity = intensity;
        light.range = range;
    }

    /// <summary>面板启用/关闭切换渲染（构建未完成时只记意图，完工时按意图落位）</summary>
    public void SetVisible(bool visible)
    {
        _visibleIntent = visible;
        if (Ready) gameObject.SetActive(visible);
    }

    /// <summary>释放 RenderTexture 与舞台（面板销毁时调用）</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_rt != null)
        {
            _rt.Release();
            Destroy(_rt);
            _rt = null;
        }
        Destroy(gameObject);
    }

    private void Update()
    {
        if (!Ready || _modelRoot == null) return;
        // 骨骼角色自带 Idle 呼吸，避免再叠加整模缩放。
        if (_hasSkeletalAnimation)
        {
            _modelRoot.localRotation = Quaternion.Euler(0f, 180f + 12f * Mathf.Sin(Time.time * .7f), 0f);
            return;
        }
        _modelRoot.Rotate(Vector3.up, 30f * Time.deltaTime, Space.Self);
        float breath = 1f + 0.008f * Mathf.Sin(Time.time * Mathf.PI);
        _modelRoot.localScale = new Vector3(1f, breath, 1f);
    }
}
