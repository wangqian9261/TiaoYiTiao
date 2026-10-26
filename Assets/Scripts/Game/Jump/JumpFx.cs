using UnityEngine;

/// <summary>
/// 跳一跳程序化粒子反馈：落地尘环 / 完美星火 / 彩蛋撒花。
/// 三套一次性 Emit 的 ParticleSystem（循环复用，零资产依赖，微信包体友好）。
/// </summary>
public class JumpFx : MonoBehaviour
{
    private ParticleSystem _dust;
    private ParticleSystem _stars;
    private ParticleSystem _confetti;
    private TrailRenderer _slide;
    private Material _slideMaterial;

    public void SlideTrail(Vector3 position, bool emitting)
    {
        if (_slide == null)
        {
            if (!emitting) return;
            var go = new GameObject("IceSlideTrail");
            go.transform.SetParent(transform, false);
            go.transform.position = position + Vector3.up * .03f;
            _slide = go.AddComponent<TrailRenderer>();
            _slideMaterial = new Material(Shader.Find("Sprites/Default"));
            _slide.sharedMaterial = _slideMaterial;
            _slide.time = .5f;
            _slide.minVertexDistance = .015f;
            _slide.startWidth = .10f; _slide.endWidth = .01f;
            _slide.startColor = Color.white; _slide.endColor = new Color(.6f,.9f,1f,0);
        }
        _slide.transform.position = position + Vector3.up * .03f;
        if (emitting && !_slide.emitting) _slide.Clear();
        _slide.emitting = emitting;
    }

    private void OnDestroy()
    { if (_slideMaterial != null) Destroy(_slideMaterial); }

    public static JumpFx Create(Transform parent)
    {
        var go = new GameObject("JumpFx");
        go.transform.SetParent(parent, false);
        var fx = go.AddComponent<JumpFx>();
        Material mat = JumpMaterials.Particle();

        // 落地尘环：平铺地面的环形扩散
        fx._dust = Build("Dust", go.transform, mat);
        var sh = fx._dust.shape;
        sh.shapeType = ParticleSystemShapeType.Circle;
        sh.radius = 0.22f;
        sh.rotation = new Vector3(90f, 0f, 0f);
        var m = fx._dust.main;
        m.startLifetime = 0.45f;
        m.startSpeed = 1.6f;
        m.startSize = 0.12f;
        m.gravityModifier = 0.25f;
        m.startColor = new Color(1f, 0.98f, 0.95f, 0.9f);

        // 完美星火：向上锥形爆发（品牌艳粉）
        fx._stars = Build("Stars", go.transform, mat);
        sh = fx._stars.shape;
        sh.shapeType = ParticleSystemShapeType.Cone;
        sh.angle = 28f;
        sh.radius = 0.05f;
        sh.rotation = new Vector3(-90f, 0f, 0f);
        m = fx._stars.main;
        m.startLifetime = 0.6f;
        m.startSpeed = 2.8f;
        m.startSize = 0.07f;
        m.gravityModifier = 1.1f;
        m.startColor = new Color(1.00f, 0.35f, 0.60f, 1f);

        // 彩蛋撒花：球形四溅，多段发射多色
        fx._confetti = Build("Confetti", go.transform, mat);
        sh = fx._confetti.shape;
        sh.shapeType = ParticleSystemShapeType.Sphere;
        sh.radius = 0.06f;
        m = fx._confetti.main;
        m.startLifetime = 0.9f;
        m.startSpeed = 3.2f;
        m.startSize = 0.09f;
        m.gravityModifier = 1.4f;

        return fx;
    }

    public void Dust(Vector3 pos)
    {
        if (_dust == null) return;
        _dust.transform.position = pos;
        _dust.Emit(10);
    }

    public void Stars(Vector3 pos)
    {
        if (_stars == null) return;
        _stars.transform.position = pos;
        _stars.Emit(14);
    }

    public void Confetti(Vector3 pos)
    {
        if (_confetti == null) return;
        _confetti.transform.position = pos;
        // 品牌撞色撒花：艳粉 / 薰衣草 / 奶油白 / 珍珠灰
        Color[] brand =
        {
            new Color(1.00f, 0.09f, 0.53f),
            new Color(0.85f, 0.72f, 0.97f),
            new Color(1.00f, 0.95f, 0.87f),
            new Color(0.90f, 0.88f, 0.85f),
        };
        var m = _confetti.main;
        for (int i = 0; i < brand.Length; i++)
        {
            m.startColor = brand[i];
            _confetti.Emit(7);
        }
    }

    /// <summary>非循环、不自动播、零持续发射的基础粒子（按需 Emit 一次性爆发）</summary>
    private static ParticleSystem Build(string name, Transform parent, Material mat)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); // 组件创建即播放，先停再配置
        var main = ps.main;
        main.playOnAwake = false;
        main.loop = false;
        main.duration = 0.5f;
        var em = ps.emission;
        em.rateOverTime = 0f;
        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = mat;
        return ps;
    }
}
