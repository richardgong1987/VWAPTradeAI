using cAlgo.Robots;
using Xunit;

namespace VWAPTradeAI.Tests.TradeLog {
    // ResultR is measured against the initial risk price distance, so a stop-out is exactly -1.00
    // and a 2R target is exactly +2.00 — round numbers that stay comparable when the exit rule
    // changes (1R / 1.5R / 2R / breakeven). Net profit is not used: commission and swap would turn
    // a stop-out into -1.03 and break that comparison.
    public class TradeResultRTests {
        private const double Risk = 2.0; // entry 100, stop 98 for a long

        [Fact]
        public void a_long_stopped_out_is_minus_one_r() {
            Assert.Equal(-1.0, Calculate(TradeDirectionModel.Long, entry: 100.0, close: 98.0), precision: 6);
        }

        [Fact]
        public void a_long_hitting_a_two_r_target_is_plus_two_r() {
            Assert.Equal(2.0, Calculate(TradeDirectionModel.Long, entry: 100.0, close: 104.0), precision: 6);
        }

        [Fact]
        public void a_long_closed_at_breakeven_is_zero() {
            Assert.Equal(0.0, Calculate(TradeDirectionModel.Long, entry: 100.0, close: 100.0), precision: 6);
        }

        [Fact]
        public void a_partial_move_reports_its_fraction_of_r() {
            // 2.74 of price movement over a 2.00 risk distance = 1.37R.
            Assert.Equal(1.37, Calculate(TradeDirectionModel.Long, entry: 100.0, close: 102.74), precision: 6);
        }

        [Fact]
        public void a_short_counts_a_fall_as_profit() {
            Assert.Equal(2.0, Calculate(TradeDirectionModel.Short, entry: 100.0, close: 96.0), precision: 6);
            Assert.Equal(-1.0, Calculate(TradeDirectionModel.Short, entry: 100.0, close: 102.0), precision: 6);
        }

        [Fact]
        public void a_missing_close_price_leaves_the_result_unknown() {
            // GetClosePrice returns 0 when neither history nor deals give a fill price.
            Assert.True(double.IsNaN(Calculate(TradeDirectionModel.Long, entry: 100.0, close: 0.0)));
        }

        [Theory]
        [InlineData(0.0)]
        [InlineData(double.NaN)]
        public void a_missing_risk_distance_leaves_the_result_unknown(double riskPrice) {
            Assert.True(double.IsNaN(TradeResultR.Calculate(TradeDirectionModel.Long, 100.0, 104.0, riskPrice)));
        }

        private static double Calculate(TradeDirectionModel direction, double entry, double close) =>
            TradeResultR.Calculate(direction, entry, close, Risk);
    }
}
