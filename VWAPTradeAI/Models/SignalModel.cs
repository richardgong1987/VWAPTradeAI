using System;

namespace cAlgo.Robots;

// A signal: on a Strong bar, a candle pattern for the Strong side touched the key level (see
// SignalDetector and LevelPatternMatcher). At most one per bar. It says only what the bar
// showed; whether it trades is OrderExecutor's call.
public class SignalModel {
    // The level that was touched; the trade side comes from it.
    public TradeLevelModel Level { get; set; }

    // 命中的形态名，例如 S_Pin_1。写进 CSV 的「信号」列，也是图上标记的文字。
    public string Label { get; set; } = "";

    // 形态自带的止损价位。
    public double StopLoss { get; set; }

    public double Close { get; set; }

    public double High { get; set; }

    public double Low { get; set; }

    // The VWAPs on the signal bar. They travel to the trade CSV, for the close row too.
    public double DailyVwap { get; set; }

    public double WeeklyVwap { get; set; }

    public int BarIndex { get; set; }

    public DateTime BarTime { get; set; }
}
