using cAlgo.Robots;
using Xunit;

namespace DayTradeSelf.Tests.Orders {
    public class OrderPlannerTests {
        // Default symbol is quoted in the account currency: pipSize 0.1 and pipValue 0.1, so one unit
        // loses 0.1 per pip and volume = riskMoney / price distance. Equity 10000 at 1% risks 100.

        [Fact]
        public void sizes_short_to_lose_the_risk_budget_at_the_stop() {
            OrderPlanner planner = CreatePlanner();

            OrderPlanModel plan = planner.CreatePlan(TestSignal.Short(close: 100.0, stopLoss: 102.0), entryPrice: 100.0, accountEquity: 10000.0);

            Assert.True(plan.IsValid, plan.RejectReason);
            Assert.Equal(TradeDirectionModel.Short, plan.Direction);
            Assert.Equal(100.0, plan.EntryPrice, precision: 6);
            Assert.Equal(102.0, plan.StopPrice, precision: 6);
            Assert.Equal(20.0, plan.StopLossPips, precision: 6);
            Assert.Equal(100.0, plan.RiskMoney, precision: 6);
            // idealVolume = 100 / (20 pips * 0.1) = 50 units = 0.5 lot.
            Assert.Equal(50.0, plan.VolumeInUnits, precision: 6);
            Assert.Equal(0.5, plan.Lots, precision: 6);
            Assert.Equal(100.0, plan.EstimatedRiskMoney, precision: 6);
        }

        [Fact]
        public void sizes_long_with_stop_below_entry() {
            OrderPlanner planner = CreatePlanner();

            OrderPlanModel plan = planner.CreatePlan(TestSignal.Long(close: 100.0, stopLoss: 98.0), entryPrice: 100.0, accountEquity: 10000.0);

            Assert.True(plan.IsValid, plan.RejectReason);
            Assert.Equal(TradeDirectionModel.Long, plan.Direction);
            Assert.Equal(20.0, plan.StopLossPips, precision: 6);
            Assert.Equal(50.0, plan.VolumeInUnits, precision: 6);
        }

        [Fact]
        public void places_the_short_take_profit_at_the_r_multiple_below_the_signal_close() {
            OrderPlanner planner = CreatePlanner(takeProfitR: 3.0);

            OrderPlanModel plan = planner.CreatePlan(TestSignal.Short(close: 100.0, stopLoss: 102.0), entryPrice: 100.0, accountEquity: 10000.0);

            Assert.True(plan.IsValid, plan.RejectReason);
            // Risk distance is 2.0, so 3R of take profit sits 6.0 below the close.
            Assert.Equal(2.0, plan.RiskPrice, precision: 6);
            Assert.Equal(94.0, plan.TakeProfitPrice, precision: 6);
            Assert.Equal(60.0, plan.TakeProfitPips, precision: 6);
        }

        [Fact]
        public void places_the_long_take_profit_at_the_r_multiple_above_the_signal_close() {
            OrderPlanner planner = CreatePlanner(takeProfitR: 3.0);

            OrderPlanModel plan = planner.CreatePlan(TestSignal.Long(close: 100.0, stopLoss: 98.0), entryPrice: 100.0, accountEquity: 10000.0);

            Assert.True(plan.IsValid, plan.RejectReason);
            Assert.Equal(106.0, plan.TakeProfitPrice, precision: 6);
            Assert.Equal(60.0, plan.TakeProfitPips, precision: 6);
        }

        [Fact]
        public void budgets_risk_from_the_risk_percentage_setting() {
            OrderPlanner planner = CreatePlanner(riskPct: 2.0);

            OrderPlanModel plan = planner.CreatePlan(TestSignal.Short(close: 100.0, stopLoss: 102.0), entryPrice: 100.0, accountEquity: 10000.0);

            Assert.True(plan.IsValid, plan.RejectReason);
            Assert.Equal(200.0, plan.RiskMoney, precision: 6);
            Assert.Equal(100.0, plan.VolumeInUnits, precision: 6);
        }

        [Fact]
        public void the_plan_keeps_the_signal_it_was_made_from() {
            // The trade CSV reads the entry's VWAP readings through it, for the close row too.
            OrderPlanner planner = new(new FakeSymbolModel { PipSize = 0.1, LotSize = 100.0, PipValue = 0.1 }, TestSettings.Create());
            SignalModel signal = TestSignal.Short(close: 100.0, stopLoss: 102.0);

            OrderPlanModel plan = planner.CreatePlan(signal, entryPrice: 100.0, accountEquity: 10000.0);

            Assert.Same(signal, plan.Signal);
        }

        [Fact]
        public void rounds_volume_up_to_the_step_when_the_ideal_size_is_past_the_halfway_point() {
            OrderPlanner planner = CreatePlanner();

            OrderPlanModel plan = planner.CreatePlan(TestSignal.Short(close: 100.0, stopLoss: 102.15), entryPrice: 100.0, accountEquity: 10000.0);

            // idealVolume = 100 / (21.5 pips * 0.1) = 46.51, so the nearest step is 47.
            Assert.True(plan.IsValid, plan.RejectReason);
            Assert.Equal(47.0, plan.VolumeInUnits, precision: 6);
        }

        [Fact]
        public void rounds_volume_down_to_the_step_when_the_ideal_size_is_short_of_the_halfway_point() {
            OrderPlanner planner = CreatePlanner();

            OrderPlanModel plan = planner.CreatePlan(TestSignal.Short(close: 100.0, stopLoss: 102.2), entryPrice: 100.0, accountEquity: 10000.0);

            // idealVolume = 100 / (22 pips * 0.1) = 45.45, so the nearest step is 45.
            Assert.True(plan.IsValid, plan.RejectReason);
            Assert.Equal(45.0, plan.VolumeInUnits, precision: 6);
            Assert.True(plan.EstimatedRiskMoney <= plan.RiskMoney);
        }

        [Fact]
        public void sizes_in_account_currency_when_pip_value_differs_from_pip_size() {
            // pipValue 0.0855 < pipSize 0.1 models a EUR account trading a USD-quoted instrument: each
            // pip costs less in the account currency, so more volume is needed to risk the same 100.
            OrderPlanner planner = CreatePlanner(pipValue: 0.0855);

            OrderPlanModel plan = planner.CreatePlan(TestSignal.Short(close: 100.0, stopLoss: 102.15), entryPrice: 100.0, accountEquity: 10000.0);

            Assert.True(plan.IsValid, plan.RejectReason);
            // lossPerUnit = 21.5 pips * 0.0855 = 1.83825; idealVolume = 54.40 -> 54.
            Assert.Equal(54.0, plan.VolumeInUnits, precision: 6);
            Assert.True(plan.EstimatedRiskMoney <= plan.RiskMoney);
        }

        [Fact]
        public void rejects_a_short_whose_stop_is_not_above_the_signal_close() {
            OrderPlanner planner = CreatePlanner();

            // A stop on the wrong side yields a negative risk distance, which cannot be sized.
            OrderPlanModel plan = planner.CreatePlan(TestSignal.Short(close: 100.0, stopLoss: 98.0), entryPrice: 100.0, accountEquity: 10000.0);

            Assert.False(plan.IsValid);
            Assert.Contains("is not beyond the signal close", plan.RejectReason);
        }

        [Fact]
        public void rejects_when_the_stop_sits_on_the_signal_close() {
            OrderPlanner planner = CreatePlanner();

            OrderPlanModel plan = planner.CreatePlan(TestSignal.Short(close: 100.0, stopLoss: 100.0), entryPrice: 100.0, accountEquity: 10000.0);

            Assert.False(plan.IsValid);
            Assert.Contains("is not beyond the signal close", plan.RejectReason);
        }

        [Fact]
        public void a_late_long_enters_at_the_current_price_with_the_signals_stop_and_target() {
            OrderPlanner planner = CreatePlanner();

            // Close 100, stop 98: the signal's 2R target is 104. Approved once price is at 101.
            OrderPlanModel plan = planner.CreatePlan(TestSignal.Long(close: 100.0, stopLoss: 98.0), entryPrice: 101.0, accountEquity: 10000.0);

            Assert.True(plan.IsValid, plan.RejectReason);
            Assert.Equal(101.0, plan.EntryPrice, precision: 6);
            Assert.Equal(98.0, plan.StopPrice, precision: 6);
            Assert.Equal(104.0, plan.TakeProfitPrice, precision: 6);
            Assert.Equal(3.0, plan.RiskPrice, precision: 6);
            Assert.Equal(30.0, plan.TakeProfitPips, precision: 6);
            // Sized on the real 3.0 to the stop, so the stop still loses the 100 budget: 100 / (30 × 0.1) = 33.3 -> 33.
            Assert.Equal(33.0, plan.VolumeInUnits, precision: 6);
        }

        [Fact]
        public void a_late_short_enters_at_the_current_price_with_the_signals_stop_and_target() {
            OrderPlanner planner = CreatePlanner();

            // Close 100, stop 102: the signal's 2R target is 96. Approved once price has fallen to 99.
            OrderPlanModel plan = planner.CreatePlan(TestSignal.Short(close: 100.0, stopLoss: 102.0), entryPrice: 99.0, accountEquity: 10000.0);

            Assert.True(plan.IsValid, plan.RejectReason);
            Assert.Equal(102.0, plan.StopPrice, precision: 6);
            Assert.Equal(96.0, plan.TakeProfitPrice, precision: 6);
            Assert.Equal(3.0, plan.RiskPrice, precision: 6);
            Assert.Equal(30.0, plan.TakeProfitPips, precision: 6);
        }

        [Theory]
        [InlineData(98.0)]
        [InlineData(97.0)]
        public void rejects_a_long_once_price_has_reached_the_stop(double entryPrice) {
            OrderPlanner planner = CreatePlanner();

            OrderPlanModel plan = planner.CreatePlan(TestSignal.Long(close: 100.0, stopLoss: 98.0), entryPrice, accountEquity: 10000.0);

            Assert.False(plan.IsValid);
            Assert.Contains("has already reached the stop", plan.RejectReason);
        }

        [Theory]
        [InlineData(104.0)]
        [InlineData(105.0)]
        public void rejects_a_long_once_price_has_reached_the_take_profit(double entryPrice) {
            OrderPlanner planner = CreatePlanner();

            OrderPlanModel plan = planner.CreatePlan(TestSignal.Long(close: 100.0, stopLoss: 98.0), entryPrice, accountEquity: 10000.0);

            Assert.False(plan.IsValid);
            Assert.Contains("has already reached the take profit", plan.RejectReason);
        }

        [Fact]
        public void rejects_a_short_once_price_has_reached_the_stop() {
            OrderPlanner planner = CreatePlanner();

            OrderPlanModel plan = planner.CreatePlan(TestSignal.Short(close: 100.0, stopLoss: 102.0), entryPrice: 102.5, accountEquity: 10000.0);

            Assert.False(plan.IsValid);
            Assert.Contains("has already reached the stop", plan.RejectReason);
        }

        [Fact]
        public void rejects_a_short_once_price_has_reached_the_take_profit() {
            OrderPlanner planner = CreatePlanner();

            OrderPlanModel plan = planner.CreatePlan(TestSignal.Short(close: 100.0, stopLoss: 102.0), entryPrice: 96.0, accountEquity: 10000.0);

            Assert.False(plan.IsValid);
            Assert.Contains("has already reached the take profit", plan.RejectReason);
        }

        [Theory]
        [InlineData(0.0)]
        [InlineData(-1.0)]
        public void rejects_when_there_is_no_risk_budget(double riskPct) {
            OrderPlanner planner = CreatePlanner(riskPct: riskPct);

            // No risk budget sizes to zero volume, which the broker minimum then rejects.
            OrderPlanModel plan = planner.CreatePlan(TestSignal.Short(close: 100.0, stopLoss: 102.0), entryPrice: 100.0, accountEquity: 10000.0);

            Assert.False(plan.IsValid);
            Assert.Contains("below broker minimum", plan.RejectReason);
        }

        [Fact]
        public void rejects_when_the_account_has_no_equity() {
            OrderPlanner planner = CreatePlanner();

            OrderPlanModel plan = planner.CreatePlan(TestSignal.Short(close: 100.0, stopLoss: 102.0), entryPrice: 100.0, accountEquity: 0.0);

            Assert.False(plan.IsValid);
            Assert.Contains("below broker minimum", plan.RejectReason);
        }

        [Fact]
        public void rejects_when_sized_volume_is_below_broker_minimum() {
            OrderPlanner planner = CreatePlanner(volumeInUnitsMin: 100.0);

            OrderPlanModel plan = planner.CreatePlan(TestSignal.Short(close: 100.0, stopLoss: 102.0), entryPrice: 100.0, accountEquity: 10000.0);

            Assert.False(plan.IsValid);
            Assert.Contains("below broker minimum", plan.RejectReason);
        }

        [Fact]
        public void rejects_when_sized_volume_is_above_broker_maximum() {
            OrderPlanner planner = CreatePlanner(volumeInUnitsMax: 10.0);

            OrderPlanModel plan = planner.CreatePlan(TestSignal.Short(close: 100.0, stopLoss: 102.0), entryPrice: 100.0, accountEquity: 10000.0);

            Assert.False(plan.IsValid);
            Assert.Contains("above broker maximum", plan.RejectReason);
        }

        private static OrderPlanner CreatePlanner(double pipValue = 0.1, double volumeInUnitsMin = 1.0,
            double volumeInUnitsMax = 1_000_000.0, double riskPct = 1.0, double takeProfitR = 2.0) {
            FakeSymbolModel symbol = new() {
                PipSize = 0.1,
                LotSize = 100.0,
                VolumeInUnitsMin = volumeInUnitsMin,
                VolumeInUnitsMax = volumeInUnitsMax,
                PipValue = pipValue
            };

            return new OrderPlanner(symbol, TestSettings.Create(riskPct, takeProfitR));
        }
    }
}
