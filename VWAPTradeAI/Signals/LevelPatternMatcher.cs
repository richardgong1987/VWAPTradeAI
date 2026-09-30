using System;

namespace cAlgo.Robots;

// Does a candle pattern for the allowed side touch the key level — the daily VWAP, the yellow line?
// The side is already fixed by VwapStack (see SignalDetector); this only checks the pattern and the
// touch.
//
// A touch counts only on the candles that form the pattern: pinbar 1, engulfing 2, fractal and
// harami 3. Checking all three for every pattern would let a one-candle pattern through on its
// neighbour's touch.
//
// Pattern: Strategy, as a rule table. Each row carries its own "did it fire" and "where is its
// stop"; rows are tried in priority order and the first that fires and touches the level wins. The
// short and long tables mirror each other line by line.
//
// Pure, no cAlgo dependency, unit tested.
public static class LevelPatternMatcher {
    // w: the three-candle window (see CandleWindow).
    private static readonly PatternRule[] ShortRules = {
        new("S_Pin_1", candleCount: 1, w => w.Scan.Pinbar == SignalSideModel.Sell, w => w.Current.High),
        new("S_Eng_1", candleCount: 2, w => w.Scan.Engulf == SignalSideModel.Sell, w => w.Current.High),
        new("S_Top_1", candleCount: 3, w => w.Scan.FractalTop == SignalSideModel.Sell && w.Current.IsBearish, w => w.Previous.High),
        new("S_Harami_1", candleCount: 3, w => w.Scan.HaramiSingle == SignalSideModel.Sell, w => Math.Max(w.Previous.High, w.Current.High))
    };

    private static readonly PatternRule[] LongRules = {
        new("L_Pin_1", candleCount: 1, w => w.Scan.Pinbar == SignalSideModel.Buy, w => w.Current.Low),
        new("L_Eng_1", candleCount: 2, w => w.Scan.Engulf == SignalSideModel.Buy, w => w.Current.Low),
        new("L_Bot_1", candleCount: 3, w => w.Scan.FractalBottom == SignalSideModel.Buy && w.Current.IsBullish, w => w.Previous.Low),
        new("L_Harami_1", candleCount: 3, w => w.Scan.HaramiSingle == SignalSideModel.Buy, w => Math.Min(w.Previous.Low, w.Current.Low))
    };

    // Null when no pattern for the level's side touches it.
    public static PatternMatchModel Match(CandleModel current, CandleModel previous, CandleModel earlier, TradeLevelModel level) {
        var window = new CandleWindow(current, previous, earlier);
        PatternRule[] rules = level.Side == SignalSideModel.Sell ? ShortRules : LongRules;

        foreach (PatternRule rule in rules) {
            if (rule.Fires(window) && window.Touches(level.Price, rule.CandleCount))
                return new PatternMatchModel(rule.Label, rule.StopLoss(window));
        }

        return null;
    }

    private sealed class PatternRule {
        public PatternRule(string label, int candleCount, Func<CandleWindow, bool> fires, Func<CandleWindow, double> stopLoss) {
            Label = label;
            CandleCount = candleCount;
            Fires = fires;
            StopLoss = stopLoss;
        }

        public string Label { get; }

        // How many of the newest candles form the pattern; only those may touch the level.
        public int CandleCount { get; }

        public Func<CandleWindow, bool> Fires { get; }

        public Func<CandleWindow, double> StopLoss { get; }
    }

    // The closed bar and the two before it (Pine offsets [0], [1], [2]), and what HanJinSignals26
    // found in them.
    private readonly struct CandleWindow {
        public CandleWindow(CandleModel current, CandleModel previous, CandleModel earlier) {
            Current = current;
            Previous = previous;
            Earlier = earlier;
            Scan = HanJinSignals26.Scan(current, previous, earlier);
        }

        public CandleModel Current { get; }

        public CandleModel Previous { get; }

        public CandleModel Earlier { get; }

        public HanJinSignalScanModel Scan { get; }

        // The price lies within the high–low range, ends included, of one of the newest candleCount candles.
        public bool Touches(double price, int candleCount) {
            return Spans(Current, price) || (candleCount >= 2 && Spans(Previous, price)) || (candleCount >= 3 && Spans(Earlier, price));
        }

        private static bool Spans(CandleModel candle, double price) {
            return candle.Low <= price && candle.High >= price;
        }
    }
}
