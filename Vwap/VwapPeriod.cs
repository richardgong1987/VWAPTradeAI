using System;

namespace cAlgo.Robots;

// The VWAP's own periods:
//
//   daily VWAP   resets every morning at 06:00 and accumulates until 06:00 the next day. 06:00 is
//                the FX/gold day roll (17:00 New York).
//   weekly VWAP  resets Monday 06:00 and accumulates until Saturday 06:00.
//
// Every bar falls in some period, so the VWAP is always accumulating and never blank.
//
// 纯时间比较，没有 cAlgo 依赖，有单元测试。
public static class VwapPeriod {
    // 日切：早上 06:00。
    private static readonly TimeSpan DayStartTime = new(6, 0, 0);

    // 这个时间属于哪一天，返回那一天开始累积的 06:00。
    public static DateTime GetDayStart(DateTime time) {
        DateTime dayStart = time.Date + DayStartTime;

        // 06:00 之前还算前一天，跟日切之前的行情算在一起。
        return time.TimeOfDay >= DayStartTime ? dayStart : dayStart.AddDays(-1);
    }

    // 这个时间属于哪一周，返回那一周周一的 06:00。先落到「哪一天」再回退到周一，
    // 这样周一 06:00 之前（还属于上周五那一天）不会被算成新的一周。
    public static DateTime GetWeekStart(DateTime time) {
        DateTime dayStart = GetDayStart(time);
        int daysSinceMonday = ((int)dayStart.DayOfWeek + 6) % 7;

        return dayStart.AddDays(-daysSinceMonday);
    }

    // 序列的第一根（previous 为 null）一律算开新周期：累积量本来就是空的。
    public static bool IsNewDay(DateTime current, DateTime? previous) {
        return !previous.HasValue || GetDayStart(current) != GetDayStart(previous.Value);
    }

    public static bool IsNewWeek(DateTime current, DateTime? previous) {
        return !previous.HasValue || GetWeekStart(current) != GetWeekStart(previous.Value);
    }
}
