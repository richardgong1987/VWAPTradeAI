using System;

namespace cAlgo.Robots;

// One candle's OHLC plus the geometry the HanJin patterns read (body edges, range,
// direction). Mirrors the private helpers of the Pine library so the pattern code stays a
// direct, readable translation. Pure value type — no cAlgo dependency.
public readonly struct CandleModel {
    public CandleModel(double open, double high, double low, double close) {
        Open = open;
        High = high;
        Low = low;
        Close = close;
    }

    public double Open { get; }

    public double High { get; }

    public double Low { get; }

    public double Close { get; }

    public double Range => High - Low;

    public bool HasRange => Range > 0;

    // Top and bottom of the real body (candle without wicks).
    public double BodyTop => Math.Max(Open, Close);

    public double BodyBottom => Math.Min(Open, Close);

    // +1 bullish, -1 bearish, 0 doji. Pine's bodyDirAt.
    public int BodyDirection => Close > Open ? 1 : Close < Open ? -1 : 0;

    public bool IsBullish => BodyDirection == 1;

    public bool IsBearish => BodyDirection == -1;
}
