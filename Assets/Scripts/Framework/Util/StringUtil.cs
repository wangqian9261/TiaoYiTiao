using System;
using System.Collections.Generic;

/// <summary>字符串工具</summary>
public static class StringUtil
{
    public static bool IsNullOrEmpty(this string s) => string.IsNullOrEmpty(s);

    public static string Join<T>(string separator, IEnumerable<T> parts) => string.Join(separator, parts);

    /// <summary>安全解析，失败返回默认值</summary>
    public static int ToInt(string s, int def = 0) => int.TryParse(s, out var v) ? v : def;

    public static float ToFloat(string s, float def = 0f) => float.TryParse(s, out var v) ? v : def;

    public static bool ToBool(string s, bool def = false)
    {
        if (bool.TryParse(s, out var v)) return v;
        return ToInt(s) != 0;
    }

    /// <summary>超长截断加省略号</summary>
    public static string Ellipsis(string s, int maxLen)
        => string.IsNullOrEmpty(s) || s.Length <= maxLen ? s : s.Substring(0, Math.Max(0, maxLen - 1)) + "…";

    /// <summary>首字母大写</summary>
    public static string Capitalize(string s)
        => string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s.Substring(1);
}
