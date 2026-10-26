using System.Collections.Generic;
using UnityEngine;

/// <summary>Poolable platform; surface geometry communicates one tile rule without floating props.</summary>
public class JumpBlock : MonoBehaviour, IPoolable
{
    public Transform CloudRoot;
    public Renderer[] CloudRenderers;
    public GameObject platformPrefab;
    public int Index;
    public float Size;
    public TileKind Kind;
    public int EffectLevelId;
    public Vector3 TopCenter => new Vector3(transform.position.x, 0, transform.position.z);
    private Material _surfaceMat, _lineMat;
    private Material[][] _originalMats;
    private Transform _surface;
    private LineRenderer _path;
    private readonly List<LineRenderer> _marks = new List<LineRenderer>();
    private Vector3 _direction;
    private float _phase;
    private Color _baseColor;

    public void OnSpawn() { }
    public void OnDespawn()
    {
        ClearMarks();
        Kind = TileKind.Normal;
        EffectLevelId = 0;
    }

    public void Setup(int index, float size, TileKind kind, int effectLevelId, Vector3 direction)
    {
        Index = index; Size = size; Kind = kind; EffectLevelId = effectLevelId;
        _direction = direction; _phase = 0;
        BuildPlatform();
        ClearMarks();
        CloudRoot.gameObject.SetActive(true);
        CloudRoot.localScale = Vector3.one * size;
        if (_originalMats == null || _originalMats.Length != CloudRenderers.Length)
        {
            _originalMats = new Material[CloudRenderers.Length][];
            for (int i = 0; i < CloudRenderers.Length; i++) _originalMats[i] = CloudRenderers[i].sharedMaterials;
        }
        _baseColor = SpecialTiles.ColorOf(kind);
        if (_surfaceMat == null) _surfaceMat = new Material(Shader.Find("Standard"));
        _surfaceMat.color = _baseColor;
        _surfaceMat.SetFloat("_Glossiness", kind == TileKind.Ice ? .92f : .25f);
        _surfaceMat.SetFloat("_Metallic", kind == TileKind.Ice ? .12f : 0f);
        for (int i = 0; i < CloudRenderers.Length; i++)
        {
            if (kind == TileKind.Normal || kind == TileKind.Start) CloudRenderers[i].sharedMaterials = _originalMats[i];
            else
            {
                var materials = new Material[_originalMats[i].Length];
                for (int m = 0; m < materials.Length; m++) materials[m] = _surfaceMat;
                CloudRenderers[i].sharedMaterials = materials;
            }
        }
        if (!SpecialTiles.IsSpecial(kind)) return;
        _surface = new GameObject("TileSurface").transform;
        _surface.SetParent(CloudRoot, false);
        if (kind == TileKind.Ice)
        {
            // Contrasting white parallel glints move along the outgoing direction.
            for (int i = 0; i < 3; i++)
                Mark(new[] { new Vector3(-.32f + i * .30f, .045f, -.30f), new Vector3(-.22f + i * .30f, .045f, .22f) }, Color.white, .025f);
        }
        else if (kind == TileKind.Collapse)
        {
            Mark(new[] { new Vector3(-.43f,.045f,.12f), new Vector3(-.17f,.045f,.02f), new Vector3(.02f,.045f,.13f), new Vector3(.15f,.045f,-.13f), new Vector3(.43f,.045f,-.24f) }, new Color(.28f,.16f,.20f), .028f);
            Mark(new[] { new Vector3(.02f,.045f,.13f), new Vector3(-.12f,.045f,.31f), new Vector3(-.08f,.045f,.44f) }, new Color(.28f,.16f,.20f), .025f);
        }
        else
        {
            for (int i = 0; i < 3; i++)
            {
                float z = -.27f + i * .27f;
                Mark(new[] {new Vector3(-.22f,.045f,z-.10f), new Vector3(0,.045f,z+.10f), new Vector3(.22f,.045f,z-.10f)}, Color.white, .04f);
            }
            _surface.localRotation = Quaternion.LookRotation(direction);
        }
    }

    private void Mark(Vector3[] points, Color color, float width)
    {
        var line = new GameObject("SurfaceMark").AddComponent<LineRenderer>();
        line.transform.SetParent(_surface, false);
        if (_lineMat == null) _lineMat = new Material(Shader.Find("Sprites/Default"));
        line.sharedMaterial = _lineMat;
        line.useWorldSpace = false; line.positionCount = points.Length; line.SetPositions(points);
        line.startColor = line.endColor = color;
        line.startWidth = line.endWidth = width;
        line.numCornerVertices = 2; line.numCapVertices = 2;
        _marks.Add(line);
    }

    public void ShowSprintPath(Vector3[] points)
    {
        if (_path == null)
        {
            _path = new GameObject("SprintRoute").AddComponent<LineRenderer>();
            _path.transform.SetParent(transform, false);
        }
        _path.gameObject.SetActive(true);
        if (_lineMat == null) _lineMat = new Material(Shader.Find("Sprites/Default"));
        _path.sharedMaterial = _lineMat; _path.useWorldSpace = true;
        _path.positionCount = points.Length + 5;
        for (int i = 0; i < points.Length; i++) _path.SetPosition(i, points[i] + Vector3.up * .07f);
        var end = points[points.Length - 1] + Vector3.up * .07f;
        Vector3[] box = {new Vector3(-.25f,0,-.25f),new Vector3(.25f,0,-.25f),new Vector3(.25f,0,.25f),new Vector3(-.25f,0,.25f),new Vector3(-.25f,0,-.25f)};
        for (int i = 0; i < 5; i++) _path.SetPosition(points.Length + i, end + box[i]);
        _path.startWidth = _path.endWidth = .035f;
        _path.startColor = _path.endColor = new Color(.8f,.7f,1f,.65f);
    }
    public void SetPathVisible(bool visible) { if (_path != null) _path.gameObject.SetActive(visible); }
    public void SetCollapseProgress(float progress)
    {
        float p = Mathf.Clamp01(progress);
        float pulse = .5f + .5f * Mathf.Sin(p * Mathf.PI * (8 + EffectLevelId));
        if (_surfaceMat != null) _surfaceMat.color = Color.Lerp(_baseColor, Color.white, pulse * .45f);
        foreach (var mark in _marks) mark.widthMultiplier = 1 + p * 2;
    }
    public void Collapse() { CloudRoot.gameObject.SetActive(false); SetPathVisible(false); }
    private void Update()
    {
        if (_surface == null || (Kind != TileKind.Ice && Kind != TileKind.Sprint)) return;
        _phase += Time.deltaTime;
        if (Kind == TileKind.Ice) _surface.localPosition = _direction * (Mathf.Repeat(_phase * .22f, .12f) - .06f);
        else
        {
            float glow = .55f + .45f * Mathf.Sin(_phase * 5f);
            foreach (var mark in _marks) mark.startColor = mark.endColor = new Color(1f, 1f, 1f, glow);
            if (_path != null) _path.startColor = _path.endColor = new Color(.8f,.7f,1f, .4f + .4f * glow);
        }
    }
    private void ClearMarks()
    {
        if (_surface != null) { _surface.gameObject.SetActive(false); Delete(_surface.gameObject); }
        _surface = null; _marks.Clear(); SetPathVisible(false);
    }
    private static void Delete(Object value) { if (Application.isPlaying) Destroy(value); else DestroyImmediate(value); }
    private void OnDestroy()
    {
        if (_surfaceMat != null) Delete(_surfaceMat);
        if (_lineMat != null) Delete(_lineMat);
    }
    public void BuildPlatform()
    {
        if (CloudRoot != null) return;
        CloudRoot = new GameObject("CloudRoot").transform;
        CloudRoot.SetParent(transform, false);
        GameObject model;
        if (platformPrefab != null)
        {
            model = Instantiate(platformPrefab, CloudRoot, false);
            NormalizePlatform(model.transform);
        }
        else
        {
            model = GameObject.CreatePrimitive(PrimitiveType.Cube);
            model.transform.SetParent(CloudRoot, false);
            model.transform.localScale = new Vector3(1, .4f, 1);
            model.transform.localPosition = new Vector3(0, -.2f, 0);
        }
        RemoveColliders(model);
        CloudRenderers = model.GetComponentsInChildren<MeshRenderer>();
    }

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
