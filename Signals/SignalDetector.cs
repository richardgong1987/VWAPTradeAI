using cAlgo.API;

namespace cAlgo.Robots;

// Asked once per closed bar. The bar is Strong when the stack alone points one way
// (VwapStack.ResolveSide on close / daily / weekly returns Buy or Sell). A Strong bar with a
// candle pattern for that side touching the daily VWAP (LevelPatternMatcher) is a signal.
//
// The reader half of a reader/rule pair: it reads Bars and the VWAP series, the rules live in
// VwapStack and LevelPatternMatcher. Whether a signal trades is decided much later, by the user and
// then OrderExecutor.
public class SignalDetector {
    // A pattern reads up to three candles (current / previous / earlier), and in OnBar() the last
    // closed bar is Count-2, so at least four bars are needed to reach `earlier`.
    private const int MinimumBarCount = 4;

    // Goes into the order label and the trade CSV's 关键位 column. There is one key level: the
    // yellow line, the daily VWAP.
    private const string LevelName = "VWAP";

    private readonly Bars _chartBars;
    private readonly VwapSeries _vwapSeries;

    public SignalDetector(Bars chartBars, VwapSeries vwapSeries) {
        _chartBars = chartBars;
        _vwapSeries = vwapSeries;
    }

    // Null when the bar is not Strong, or no pattern for the Strong side touches the daily VWAP.
    public SignalModel DetectOnClosedBar() {
        int closedBarIndex = _chartBars.Count - 2; // last fully closed bar in OnBar()

        if (_chartBars.Count < MinimumBarCount || closedBarIndex >= _vwapSeries.Count)
            return null;

        CandleModel current = ReadCandle(closedBarIndex);
        VwapSampleModel vwap = _vwapSeries[closedBarIndex];
        SignalSideModel side = VwapStack.ResolveSide(current.Close, vwap.Daily, vwap.Weekly);

        if (side == SignalSideModel.None)
            return null;

        // The key level is the daily VWAP; the weekly VWAP only gates direction.
        var level = new TradeLevelModel(LevelName, side, vwap.Daily);
        PatternMatchModel match = LevelPatternMatcher.Match(current, ReadCandle(closedBarIndex - 1), ReadCandle(closedBarIndex - 2), level);

        if (match == null)
            return null;

        return new SignalModel {
            Level = level,
            Label = match.Label,
            StopLoss = match.StopLoss,
            Close = current.Close,
            High = current.High,
            Low = current.Low,
            DailyVwap = vwap.Daily,
            WeeklyVwap = vwap.Weekly,
            BarIndex = closedBarIndex,
            BarTime = _chartBars.OpenTimes[closedBarIndex]
        };
    }

    private CandleModel ReadCandle(int index) {
        return new CandleModel(open: _chartBars.OpenPrices[index], high: _chartBars.HighPrices[index], low: _chartBars.LowPrices[index],
            close: _chartBars.ClosePrices[index]);
    }
}
