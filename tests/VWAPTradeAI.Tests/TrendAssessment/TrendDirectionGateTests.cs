using cAlgo.Robots;
using Xunit;

namespace VWAPTradeAI.Tests.TrendAssessment {
    // The whole AI trend rule, row by row. A Buy needs an UP trend, a Sell a DOWN trend, and the
    // daily VWAP must have a slope, either way. Everything else is rejected.
    public class TrendDirectionGateTests {
        private static string Reject(SignalSideModel side, TrendModel trend, DailyVwapDirectionModel dailyVwapDirection) =>
            TrendDirectionGate.FindRejectReason(side, TestAssessment.Of(trend, dailyVwapDirection));

        [Theory]
        [InlineData(SignalSideModel.Buy, TrendModel.Up, DailyVwapDirectionModel.Rising)]
        [InlineData(SignalSideModel.Buy, TrendModel.Up, DailyVwapDirectionModel.Falling)]
        [InlineData(SignalSideModel.Sell, TrendModel.Down, DailyVwapDirectionModel.Falling)]
        [InlineData(SignalSideModel.Sell, TrendModel.Down, DailyVwapDirectionModel.Rising)]
        public void a_trend_with_the_signal_and_a_sloping_daily_vwap_passes(SignalSideModel side, TrendModel trend,
            DailyVwapDirectionModel dailyVwapDirection) {
            // The daily VWAP only has to slope: it need not slope the signal's way.
            Assert.Null(Reject(side, trend, dailyVwapDirection));
        }

        [Theory]
        [InlineData(SignalSideModel.Buy, TrendModel.Up)]
        [InlineData(SignalSideModel.Sell, TrendModel.Down)]
        public void a_flat_daily_vwap_is_rejected_even_with_the_trend(SignalSideModel side, TrendModel trend) {
            Assert.Equal("Daily VWAP is FLAT", Reject(side, trend, DailyVwapDirectionModel.Flat));
        }

        [Theory]
        [InlineData(SignalSideModel.Buy, TrendModel.Down, DailyVwapDirectionModel.Rising, "Trend DOWN is against a Buy signal")]
        [InlineData(SignalSideModel.Buy, TrendModel.Down, DailyVwapDirectionModel.Falling, "Trend DOWN is against a Buy signal")]
        [InlineData(SignalSideModel.Buy, TrendModel.Down, DailyVwapDirectionModel.Flat, "Trend DOWN is against a Buy signal")]
        [InlineData(SignalSideModel.Sell, TrendModel.Up, DailyVwapDirectionModel.Rising, "Trend UP is against a Sell signal")]
        [InlineData(SignalSideModel.Sell, TrendModel.Up, DailyVwapDirectionModel.Falling, "Trend UP is against a Sell signal")]
        [InlineData(SignalSideModel.Sell, TrendModel.Up, DailyVwapDirectionModel.Flat, "Trend UP is against a Sell signal")]
        public void a_trend_against_the_signal_is_rejected_whatever_the_daily_vwap(SignalSideModel side, TrendModel trend,
            DailyVwapDirectionModel dailyVwapDirection, string reason) {
            Assert.Equal(reason, Reject(side, trend, dailyVwapDirection));
        }

        [Theory]
        [InlineData(SignalSideModel.Buy, DailyVwapDirectionModel.Rising)]
        [InlineData(SignalSideModel.Buy, DailyVwapDirectionModel.Falling)]
        [InlineData(SignalSideModel.Buy, DailyVwapDirectionModel.Flat)]
        [InlineData(SignalSideModel.Sell, DailyVwapDirectionModel.Rising)]
        [InlineData(SignalSideModel.Sell, DailyVwapDirectionModel.Falling)]
        [InlineData(SignalSideModel.Sell, DailyVwapDirectionModel.Flat)]
        public void a_sideways_trend_is_rejected_for_any_signal(SignalSideModel side, DailyVwapDirectionModel dailyVwapDirection) {
            Assert.Equal("Trend is SIDEWAYS", Reject(side, TrendModel.Sideways, dailyVwapDirection));
        }

        [Theory]
        [InlineData(SignalSideModel.Buy)]
        [InlineData(SignalSideModel.Sell)]
        public void an_unreadable_chart_is_rejected(SignalSideModel side) {
            Assert.Equal("Chart unreadable: No candlesticks are visible.", TrendDirectionGate.FindRejectReason(side, TestAssessment.Unreadable()));
        }

        [Theory]
        [InlineData(SignalSideModel.Buy)]
        [InlineData(SignalSideModel.Sell)]
        public void an_unavailable_assessment_is_rejected(SignalSideModel side) {
            Assert.Equal("Assessment unavailable: Local assessment request timed out after 30 seconds",
                TrendDirectionGate.FindRejectReason(side, TestAssessment.Unavailable()));
        }

        [Fact]
        public void confidence_plays_no_part_in_the_decision() {
            // The model's own estimate is not calibrated, so a trade never hangs on it.
            TrendAssessmentResultModel unsure = TrendAssessmentResultModel.Assessed(TrendModel.Up, trendConfidence: 0.01,
                DailyVwapDirectionModel.Rising, dailyVwapConfidence: 0.0, "structure", "reason", TestAssessment.ModelName);

            Assert.Null(TrendDirectionGate.FindRejectReason(SignalSideModel.Buy, unsure));
        }

        [Fact]
        public void a_signal_without_a_side_is_rejected() {
            Assert.NotNull(Reject(SignalSideModel.None, TrendModel.Up, DailyVwapDirectionModel.Rising));
        }
    }
}
