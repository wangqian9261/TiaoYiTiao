using System.Collections.Generic;
using UnityEngine;

/// <summary>Transform 常用扩展（避免写三遍 localPosition）</summary>
public static class TransformExtensions
{
    public static void SetLocalPosX(this Transform t, float x) { var p = t.localPosition; p.x = x; t.localPosition = p; }
    public static void SetLocalPosY(this Transform t, float y) { var p = t.localPosition; p.y = y; t.localPosition = p; }
    public static void SetLocalPosZ(this Transform t, float z) { var p = t.localPosition; p.z = z; t.localPosition = p; }

    public static void SetWorldPosX(this Transform t, float x) { var p = t.position; p.x = x; t.position = p; }
    public static void SetWorldPosY(this Transform t, float y) { var p = t.position; p.y = y; t.position = p; }
    public static void SetWorldPosZ(this Transform t, float z) { var p = t.position; p.z = z; t.position = p; }

    /// <summary>局部位置/旋转/缩放归零归一</summary>
    public static void ResetLocal(this Transform t)
    {
        t.localPosition = Vector3.zero;
        t.localRotation = Quaternion.identity;
        t.localScale = Vector3.one;
    }
}

/// <summary>集合扩展</summary>
public static class CollectionExtensions
{
    public static bool IsNullOrEmpty<T>(this List<T> list) => list == null || list.Count == 0;
}
