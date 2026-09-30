using cAlgo.Robots;
using Xunit;

namespace DayTradeSelf.Tests.Orders {
    // The stop does not sit exactly on the pattern's level — it is pushed that many ticks further
    // out, so a wick that just grazes the level does not take the trade out.
    public class StopOffsetTests {
        [Fact]
        public void a_short_stop_is_pushed_above_the_pattern_level() {
            OrderPlanner planner = CreatePlanner(stopOffsetTicks: 20); // 20 × 0.01 = 0.20

            OrderPlanModel plan = planner.CreatePlan(TestSignal.Short(close: 100.0, stopLoss: 102.0), entryPrice: 100.0, accountEquity: 10000.0);

            Assert.True(plan.IsValid, plan.RejectReason);
            Assert.Equal(102.2, plan.StopPrice, precision: 6);
            // Risk widens with the offset, so the take profit moves out by the same R multiple.
            Assert.Equal(2.2, plan.RiskPrice, precision: 6);
            Assert.Equal(95.6, plan.TakeProfitPrice, precision: 6);
        }

        [Fact]
        public void a_long_stop_is_pushed_below_the_pattern_level() {
            OrderPlanner planner = CreatePlanner(stopOffsetTicks: 20);

            OrderPlanModel plan = planner.CreatePlan(TestSignal.Long(close: 100.0, stopLoss: 98.0), entryPrice: 100.0, accountEquity: 10000.0);

            Assert.True(plan.IsValid, plan.RejectReason);
            Assert.Equal(97.8, plan.StopPrice, precision: 6);
            Assert.Equal(2.2, plan.RiskPrice, precision: 6);
            Assert.Equal(104.4, plan.TakeProfitPrice, precision: 6);
        }

        [Fact]
        public void the_wider_stop_still_risks_the_same_budget() {
            OrderPlanner planner = CreatePlanner(stopOffsetTicks: 20);

            OrderPlanModel plan = planner.CreatePlan(TestSignal.Short(close: 100.0, stopLoss: 102.0), entryPrice: 100.0, accountEquity: 10000.0);

            // A wider stop buys less volume, so the loss at the stop stays on the 1% budget.
            Assert.Equal(100.0, plan.RiskMoney, precision: 6);
            Assert.Equal(22.0, plan.StopLossPips, precision: 6);
            Assert.Equal(45.0, plan.VolumeInUnits, precision: 6); // 100 / (22 × 0.1) = 45.45 -> 45
        }

        [Fact]
        public void a_zero_offset_leaves_the_stop_on_the_pattern_level() {
            OrderPlanner planner = CreatePlanner(stopOffsetTicks: 0);

            OrderPlanModel plan = planner.CreatePlan(TestSignal.Short(close: 100.0, stopLoss: 102.0), entryPrice: 100.0, accountEquity: 10000.0);

            Assert.Equal(102.0, plan.StopPrice, precision: 6);
        }

        private static OrderPlanner CreatePlanner(int stopOffsetTicks) {
            FakeSymbolModel symbol = new() {
                PipSize = 0.1,
                TickSize = 0.01,
                LotSize = 100.0,
                PipValue = 0.1
            };

            return new OrderPlanner(symbol, TestSettings.WithStopOffsetTicks(stopOffsetTicks));
        }
    }
}
