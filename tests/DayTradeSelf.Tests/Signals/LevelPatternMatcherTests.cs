using cAlgo.Robots;
using Xunit;

namespace DayTradeSelf.Tests.Signals {
    // The key level is the daily VWAP — the yellow line on the chart. A candle pattern only becomes
    // a signal when it actually touches that line, and only the candles that form the pattern count:
    // pinbar one, engulfing two, fractal and harami three.
    public class LevelPatternMatcherTests {
        // Bearish pinbar spanning 99.5 to 110: upper wick 9.8 of a 10.5 range, lower wick 0.5.
        private static CandleModel BearishPinbar() => new(open: 100.0, high: 110.0, low: 99.5, close: 100.2);

        // A quiet candle well below the pinbar, used as filler where the pattern ignores it.
        private static CandleModel Filler() => new(open: 60.0, high: 61.0, low: 59.0, close: 60.5);

        [Fact]
        public void a_pattern_sitting_on_the_daily_vwap_becomes_a_signal() {
            PatternMatchModel match = LevelPatternMatcher.Match(BearishPinbar(), Filler(), Filler(), ShortLevel(dailyVwap: 105.0));

            Assert.NotNull(match);
            Assert.Equal("S_Pin_1", match.Label);
            Assert.Equal(110.0, match.StopLoss, precision: 6);
        }

        [Fact]
        public void the_same_pattern_away_from_the_daily_vwap_is_ignored() {
            // Identical candle, but the line is above its high, so price never reached the level.
            PatternMatchModel match = LevelPatternMatcher.Match(BearishPinbar(), Filler(), Filler(), ShortLevel(dailyVwap: 120.0));

            Assert.Null(match);
        }

        [Theory]
        [InlineData(99.5)] // the low exactly touches the line
        [InlineData(110.0)] // the high exactly touches the line
        public void touching_the_line_with_a_wick_is_enough(double dailyVwap) {
            Assert.NotNull(LevelPatternMatcher.Match(BearishPinbar(), Filler(), Filler(), ShortLevel(dailyVwap)));
        }

        [Fact]
        public void a_one_candle_pattern_is_not_let_through_by_a_neighbours_touch() {
            // The pinbar is a single-candle pattern, so only the signal candle may satisfy the touch.
            // Here the previous candle straddles the line and the pinbar does not.
            CandleModel previousOnTheLine = new(open: 69.0, high: 75.0, low: 65.0, close: 70.0);

            PatternMatchModel match = LevelPatternMatcher.Match(BearishPinbar(), previousOnTheLine, Filler(), ShortLevel(dailyVwap: 70.0));

            Assert.Null(match);
        }

        [Fact]
        public void a_two_candle_pattern_accepts_a_touch_on_either_candle() {
            // Bearish engulfing: current engulfs previous and closes down. Only `previous` reaches
            // the line, which is enough because the pattern is built from both candles.
            CandleModel previous = new(open: 101.0, high: 105.0, low: 100.0, close: 100.5);
            CandleModel current = new(open: 106.0, high: 106.0, low: 99.0, close: 99.5);

            PatternMatchModel match = LevelPatternMatcher.Match(current, previous, Filler(), ShortLevel(dailyVwap: 104.0));

            Assert.NotNull(match);
            Assert.Equal("S_Eng_1", match.Label);
            Assert.Equal(106.0, match.StopLoss, precision: 6);
        }

        [Fact]
        public void a_bearish_pattern_is_not_taken_when_the_gate_only_allows_longs() {
            // Side comes from the VWAP stack gate; a long-only bar never matches a bearish pattern.
            TradeLevelModel longOnly = new("VWAP", SignalSideModel.Buy, price: 105.0);

            Assert.Null(LevelPatternMatcher.Match(BearishPinbar(), Filler(), Filler(), longOnly));
        }

        // The two sides are mirrored rule tables; each row is checked below for its name and its
        // stop. The three-candle patterns touch the line only on an older candle.

        [Fact]
        public void a_top_fractal_stops_above_the_middle_candle() {
            CandleModel earlier = new(open: 100.0, high: 102.0, low: 98.0, close: 101.0);
            CandleModel previous = new(open: 103.0, high: 106.0, low: 100.0, close: 104.0);
            CandleModel current = new(open: 101.0, high: 103.0, low: 97.0, close: 98.0);

            PatternMatchModel match = LevelPatternMatcher.Match(current, previous, earlier, ShortLevel(dailyVwap: 105.0));

            Assert.Equal("S_Top_1", match.Label);
            Assert.Equal(106.0, match.StopLoss, precision: 6);
        }

        [Fact]
        public void a_bearish_harami_stops_above_the_higher_of_the_last_two_candles() {
            CandleModel earlier = new(open: 100.0, high: 110.0, low: 90.0, close: 95.0);
            CandleModel previous = new(open: 100.0, high: 105.0, low: 95.0, close: 102.0);
            CandleModel current = new(open: 101.0, high: 104.0, low: 92.0, close: 93.0);

            PatternMatchModel match = LevelPatternMatcher.Match(current, previous, earlier, ShortLevel(dailyVwap: 108.0));

            Assert.Equal("S_Harami_1", match.Label);
            Assert.Equal(105.0, match.StopLoss, precision: 6);
        }

        [Fact]
        public void a_bullish_pinbar_stops_below_its_own_low() {
            CandleModel bullishPinbar = new(open: 100.0, high: 100.5, low: 90.0, close: 100.2);
            CandleModel above = new(open: 140.0, high: 141.0, low: 139.0, close: 140.5);

            PatternMatchModel match = LevelPatternMatcher.Match(bullishPinbar, above, above, LongLevel(dailyVwap: 95.0));

            Assert.Equal("L_Pin_1", match.Label);
            Assert.Equal(90.0, match.StopLoss, precision: 6);
        }

        [Fact]
        public void a_bullish_engulfing_stops_below_the_engulfing_candle() {
            CandleModel previous = new(open: 99.0, high: 100.0, low: 95.0, close: 99.5);
            CandleModel current = new(open: 94.0, high: 101.0, low: 94.0, close: 100.5);

            PatternMatchModel match = LevelPatternMatcher.Match(current, previous, Filler(), LongLevel(dailyVwap: 99.8));

            Assert.Equal("L_Eng_1", match.Label);
            Assert.Equal(94.0, match.StopLoss, precision: 6);
        }

        [Fact]
        public void a_bottom_fractal_stops_below_the_middle_candle() {
            CandleModel earlier = new(open: 100.0, high: 102.0, low: 98.0, close: 99.0);
            CandleModel previous = new(open: 97.0, high: 100.0, low: 94.0, close: 96.0);
            CandleModel current = new(open: 99.0, high: 103.0, low: 97.0, close: 102.0);

            PatternMatchModel match = LevelPatternMatcher.Match(current, previous, earlier, LongLevel(dailyVwap: 95.0));

            Assert.Equal("L_Bot_1", match.Label);
            Assert.Equal(94.0, match.StopLoss, precision: 6);
        }

        [Fact]
        public void a_bullish_harami_stops_below_the_lower_of_the_last_two_candles() {
            CandleModel earlier = new(open: 100.0, high: 110.0, low: 90.0, close: 105.0);
            CandleModel previous = new(open: 100.0, high: 105.0, low: 95.0, close: 98.0);
            CandleModel current = new(open: 99.0, high: 108.0, low: 96.0, close: 107.0);

            PatternMatchModel match = LevelPatternMatcher.Match(current, previous, earlier, LongLevel(dailyVwap: 92.0));

            Assert.Equal("L_Harami_1", match.Label);
            Assert.Equal(95.0, match.StopLoss, precision: 6);
        }

        private static TradeLevelModel ShortLevel(double dailyVwap) => new("VWAP", SignalSideModel.Sell, dailyVwap);

        private static TradeLevelModel LongLevel(double dailyVwap) => new("VWAP", SignalSideModel.Buy, dailyVwap);
    }
}
