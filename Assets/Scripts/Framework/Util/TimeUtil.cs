using System;

/// <summary>时间工具</summary>
public static class TimeUtil
{
    private static readonly DateTime Epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>当前 Unix 秒</summary>
    public static long UnixSeconds() => (long)(DateTime.UtcNow - Epoch).TotalSeconds;

    /// <summary>当前 Unix 毫秒</summary>
    public static long UnixMilliSeconds() => (long)(DateTime.UtcNow - Epoch).TotalMilliseconds;

    /// <summary>今日本地零点的 Unix 秒（每日重置用）</summary>
    public static long TodayZeroUnixSeconds() => (long)(DateTime.Now.Date - Epoch).TotalSeconds;

    /// <summary>格式化为 mm:ss（超过1小时为 hh:mm:ss）</summary>
    public static string FormatDuration(float seconds)
    {
        int total = (int)Math.Max(0, seconds);
        int h = total / 3600;
        int m = (total % 3600) / 60;
        int s = total % 60;
        return h > 0 ? $"{h:D2}:{m:D2}:{s:D2}" : $"{m:D2}:{s:D2}";
    }
}
