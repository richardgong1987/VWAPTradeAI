using System;

namespace cAlgo.Robots;

// 一根 K 线上的三个 VWAP 取值，由 VwapCalculator 算出、交给 VwapLines 画。纯数据，不依赖 cAlgo。
public class VwapSampleModel {
    // Preserve the source bar timestamp for chart coordinates.
    public DateTime OpenTime { get; set; }

    public double Daily { get; set; }

    public double Weekly { get; set; }

    // 前一交易日收盘时的当日 VWAP。第一次跨日之前没有「前一日」，此时是 NaN，调用方不画。
    public double PreviousDaily { get; set; }

    // 这根 K 线是否是新一周的第一根（周一 06:00）。周 VWAP 在这里清零，画线时要跳过这一段。
    public bool IsWeekPeriodStart { get; set; }

    // 这根 K 线是否是新一天的第一根（06:00）。日 VWAP 与前一日 VWAP 都在这里跳变，画线时要跳过。
    public bool IsDayPeriodStart { get; set; }
}
