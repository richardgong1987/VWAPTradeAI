namespace cAlgo.Robots;

// C# port of the Pine library HanJinSignals26 (© richardgong1988). Pure classifier: given a
// candle (or a 3-bar window ordered current -> previous -> earlier, i.e. Pine offsets
// [0],[1],[2]), it returns which pattern fired and on which side. No cAlgo dependency, so it
// is unit tested. Design: docs/design/hanjin-signals-26.md.
public static class HanJinSignals26 {
    // Shared read-only defaults so the parameterless overloads reproduce the Pine defaults
    // without allocating. Never mutated.
    private static readonly HanJinSignalOptionsModel DefaultOptions = new();

    // ── Aggregate ─────────────────────────────────────────────────────────────
    // Runs every pattern over the window. earlier/previous are only read by the multi-bar
    // patterns; single-bar patterns look at current alone.
    public static HanJinSignalScanModel Scan(CandleModel current, CandleModel previous, CandleModel earlier) =>
        Scan(current, previous, earlier, DefaultOptions);

    public static HanJinSignalScanModel Scan(CandleModel current, CandleModel previous, CandleModel earlier,
        HanJinSignalOptionsModel options) {
        (SignalSideModel top, SignalSideModel bottom) = Fractal(current, previous, earlier);
        SignalSideModel harami = Harami(current, previous, earlier);

        return new HanJinSignalScanModel {
            Pinbar = Pinbar(current, options),
            Engulf = Engulf(current, previous),
            FractalTop = top,
            FractalBottom = bottom,
            HaramiSingle = harami
        };
    }

    // ── ① Pinbar ──────────────────────────────────────────────────────────────
    public static SignalSideModel Pinbar(CandleModel bar) => Pinbar(bar, DefaultOptions);

    public static SignalSideModel Pinbar(CandleModel bar, HanJinSignalOptionsModel options) {
        if (!bar.HasRange)
            return SignalSideModel.None;

        double upperWick = (bar.High - bar.BodyTop) / bar.Range;
        double lowerWick = (bar.BodyBottom - bar.Low) / bar.Range;

        // 下引线长。上引线短
        bool isBull = lowerWick >= options.PinbarLongFraction && (!options.PinbarStrict || upperWick <= options.PinbarShortFraction);

        // 上引线长，下引线短。
        bool isBear = upperWick >= options.PinbarLongFraction && (!options.PinbarStrict || lowerWick <= options.PinbarShortFraction);

        return isBull ? SignalSideModel.Buy : isBear ? SignalSideModel.Sell : SignalSideModel.None;
    }

    // ── ② Engulfing 吞没───────────────────────────────────────────────────────────
    public static SignalSideModel Engulf(CandleModel current, CandleModel previous) {
        bool isEngulfing = current.High > previous.High && current.Low < previous.Low;
        if (isEngulfing) {
            if (current.IsBullish) {
                return SignalSideModel.Buy;
            }

            if (current.IsBearish) {
                return SignalSideModel.Sell;
            }
        }

        return SignalSideModel.None;
    }

    // ── ③ Fractal — returns (Top, Bottom) ─────────────────────────────────────
    // Strict structural fractal: the middle bar (previous, [1]) dominates BOTH neighbours on
    // the high line AND the low line.
    public static (SignalSideModel Top, SignalSideModel Bottom) Fractal(CandleModel current, CandleModel previous, CandleModel earlier) {
        bool isTop = previous.High > earlier.High && previous.High > current.High && previous.Low > earlier.Low &&
                     previous.Low > current.Low && previous.BodyBottom > current.Close && current.IsBearish;

        bool isBottom = earlier.Low > previous.Low && previous.Low < current.Low && earlier.High > previous.High &&
                        previous.High < current.High && previous.BodyTop < current.Close && current.IsBullish;

        return (isTop ? SignalSideModel.Sell : SignalSideModel.None, isBottom ? SignalSideModel.Buy : SignalSideModel.None);
    }

    // ── ④ Harami ─────────────────────────────────────────────────────────────
    public static SignalSideModel Harami(CandleModel current, CandleModel previous, CandleModel earlier) {
        bool earlierContainsPrevious = Contains(outer: earlier, inner: previous);

        return earlierContainsPrevious ? HaramiDirection(previous, current) : SignalSideModel.None;
    }

    private static SignalSideModel HaramiDirection(CandleModel previous, CandleModel current) {
        if (previous.High < current.Close) {
            return SignalSideModel.Buy;
        }

        if (previous.Low > current.Close) {
            return SignalSideModel.Sell;
        }

        return SignalSideModel.None;
    }

    // ── Internal geometry helpers (mirror the Pine private functions) ──────────
    // outer fully brackets inner on both the high and the low line.
    private static bool Contains(CandleModel outer, CandleModel inner) =>
        outer.High > inner.High && outer.Low < inner.Low;
}
