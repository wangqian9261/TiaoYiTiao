using UnityEngine;

/// <summary>
/// 跳一跳运行时材质工具：Built-in 管线下的 Diffuse / 粒子材质兜底链。
/// 编辑器侧的持久化材质资产由 JumpGameSetup 生成，此处只服务运行时路径。
/// </summary>
public static class JumpMaterials
{
    private static Shader _diffuse;
    private static Shader _particle;

    /// <summary>Diffuse 材质（每次调用生成新实例，由调用方持有）</summary>
    public static Material Diffuse(Color color)
    {
        if (_diffuse == null)
        {
            _diffuse = Shader.Find("Legacy Shaders/Diffuse");
            if (_diffuse == null) _diffuse = Shader.Find("Diffuse");
            if (_diffuse == null) _diffuse = Shader.Find("Standard");
        }
        var mat = new Material(_diffuse);
        if (mat.HasProperty("_Color")) mat.color = color;
        return mat;
    }

    private static Shader _transparentDiffuse;

    /// <summary>半透明 Diffuse 材质（幻影砖：看似正常方块但可辨识的半透明外观）</summary>
    public static Material TransparentDiffuse(Color color, float alpha)
    {
        if (_transparentDiffuse == null)
        {
            _transparentDiffuse = Shader.Find("Legacy Shaders/Transparent/Diffuse");
            if (_transparentDiffuse == null) _transparentDiffuse = Shader.Find("Transparent/Diffuse");
            if (_transparentDiffuse == null) _transparentDiffuse = _diffuse; // 兜底：退化为不透明
        }
        var mat = new Material(_transparentDiffuse);
        if (mat.HasProperty("_Color"))
        {
            color.a = alpha;
            mat.color = color;
        }
        return mat;
    }

    /// <summary>粒子材质（Standard Unlit → 旧版 Alpha Blended 回退）</summary>
    public static Material Particle()
    {
        if (_particle == null)
        {
            _particle = Shader.Find("Particles/Standard Unlit");
            if (_particle == null) _particle = Shader.Find("Legacy Shaders/Particles/Alpha Blended");
            if (_particle == null) _particle = Shader.Find("Sprites/Default");
        }
        return new Material(_particle);
    }
}
