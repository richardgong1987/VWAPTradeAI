using cAlgo.Robots;
using Xunit;

namespace VWAPTradeAI.Tests.Signals {
    // The direction gate: only close > daily > weekly may go long, only close < daily < weekly may
    // go short. Anything else — the VWAPs crossed, or the close sits between them — trades nothing.
    public class VwapStackTests {
        [Fact]
        public void close_above_daily_above_weekly_allows_only_longs() {
            SignalSideModel side = VwapStack.ResolveSide(close: 105.0, dailyVwap: 102.0, weeklyVwap: 100.0);

            Assert.Equal(SignalSideModel.Buy, side);
        }

        [Fact]
        public void close_below_daily_below_weekly_allows_only_shorts() {
            SignalSideModel side = VwapStack.ResolveSide(close: 95.0, dailyVwap: 98.0, weeklyVwap: 100.0);

            Assert.Equal(SignalSideModel.Sell, side);
        }

        [Fact]
        public void a_close_above_both_vwaps_is_refused_when_the_vwaps_are_stacked_the_other_way() {
            // Price is above everything, but daily < weekly, so the two timeframes disagree.
            SignalSideModel side = VwapStack.ResolveSide(close: 105.0, dailyVwap: 98.0, weeklyVwap: 100.0);

            Assert.Equal(SignalSideModel.None, side);
        }

        [Fact]
        public void a_close_between_the_two_vwaps_is_refused() {
            SignalSideModel side = VwapStack.ResolveSide(close: 101.0, dailyVwap: 102.0, weeklyVwap: 100.0);

            Assert.Equal(SignalSideModel.None, side);
        }

        [Theory]
        [InlineData(102.0, 102.0, 100.0)] // close sits exactly on the daily VWAP
        [InlineData(105.0, 102.0, 102.0)] // the two VWAPs are equal
        public void equal_values_do_not_form_a_stack(double close, double daily, double weekly) {
            Assert.Equal(SignalSideModel.None, VwapStack.ResolveSide(close, daily, weekly));
        }

        [Theory]
        [InlineData(double.NaN, 102.0, 100.0)]
        [InlineData(105.0, double.NaN, 100.0)]
        [InlineData(105.0, 102.0, double.NaN)]
        [InlineData(105.0, double.PositiveInfinity, 100.0)]
        public void missing_vwap_values_trade_nothing(double close, double daily, double weekly) {
            Assert.Equal(SignalSideModel.None, VwapStack.ResolveSide(close, daily, weekly));
        }
    }
}
