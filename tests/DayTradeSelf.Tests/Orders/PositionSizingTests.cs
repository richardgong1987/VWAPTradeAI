using cAlgo.Robots;
using Xunit;

namespace VWAPTradeAI.Tests.Orders {
    // Checks the lot size OrderPlanner computes for realistic broker symbol settings.
    // Sizing rule: riskMoney = equity × the level's RiskPct%, units = riskMoney / (stop pips ×
    // pip value per unit), snapped to the nearest volume step, lots = units / lot size.
    public class PositionSizingTests {
        [Fact]
        public void xauusd_one_percent_with_a_10_dollar_stop_is_a_tenth_of_a_lot() {
            OrderPlanner planner = CreatePlanner(Xauusd(pipValuePerUnit: 0.01));

            // riskMoney = 10000 × 1% = 100 USD. Stop 2410 − 2400 = 10 USD = 1000 pips.
            // One unit (1 oz) loses 1000 × 0.01 = 10 USD, so 100 / 10 = 10 units = 0.10 lot.
            OrderPlanModel plan = planner.CreatePlan(TestSignal.Short(close: 2400.0, stopLoss: 2410.0), entryPrice: 2400.0, accountEquity: 10000.0);

            Assert.True(plan.IsValid, plan.RejectReason);
            Assert.Equal(1000.0, plan.StopLossPips, precision: 6);
            Assert.Equal(10.0, plan.VolumeInUnits, precision: 6);
            Assert.Equal(0.10, plan.Lots, precision: 6);
            Assert.Equal(100.0, plan.EstimatedRiskMoney, precision: 6);
        }

        [Fact]
        public void xauusd_snaps_to_the_nearest_unit_step_even_when_that_overshoots_the_budget() {
            OrderPlanner planner = CreatePlanner(Xauusd(pipValuePerUnit: 0.01), riskPct: 2.0);

            // riskMoney = 25000 × 2% = 500 USD. Stop 2358.00 − 2350.50 = 7.5 USD = 750 pips.
            // One unit loses 7.5 USD, so 500 / 7.5 = 66.67 units -> nearest step is 67 = 0.67 lot.
            // 67 units lose 502.50 at the stop; 66 would lose 495 and leave more budget unused.
            OrderPlanModel plan = planner.CreatePlan(TestSignal.Short(close: 2350.50, stopLoss: 2358.00), entryPrice: 2350.50, accountEquity: 25000.0);

            Assert.True(plan.IsValid, plan.RejectReason);
            Assert.Equal(67.0, plan.VolumeInUnits, precision: 6);
            Assert.Equal(0.67, plan.Lots, precision: 6);
            Assert.Equal(502.5, plan.EstimatedRiskMoney, precision: 6);
            // The overshoot never exceeds what one volume step is worth at the stop.
            Assert.True(plan.EstimatedRiskMoney - plan.RiskMoney < 750.0 * 0.01);
        }

        [Fact]
        public void eurusd_one_percent_with_a_50_pip_stop_is_two_tenths_of_a_lot() {
            OrderPlanner planner = CreatePlanner(Eurusd());

            // riskMoney = 10000 × 1% = 100 USD. Stop 1.0900 − 1.0850 = 50 pips.
            // One unit loses 50 × 0.0001 = 0.005 USD, so 100 / 0.005 = 20000 units = 0.20 lot.
            OrderPlanModel plan = planner.CreatePlan(TestSignal.Short(close: 1.0850, stopLoss: 1.0900), entryPrice: 1.0850, accountEquity: 10000.0);

            Assert.True(plan.IsValid, plan.RejectReason);
            Assert.Equal(50.0, plan.StopLossPips, precision: 6);
            Assert.Equal(20000.0, plan.VolumeInUnits, precision: 6);
            Assert.Equal(0.20, plan.Lots, precision: 6);
            Assert.Equal(100.0, plan.EstimatedRiskMoney, precision: 6);
        }

        [Fact]
        public void eurusd_snaps_to_the_nearest_one_thousand_unit_step() {
            OrderPlanner planner = CreatePlanner(Eurusd());

            // riskMoney = 100 USD. Stop 1.0880 − 1.0850 = 30 pips; one unit loses 0.003 USD.
            // 100 / 0.003 = 33333 units -> nearest 1000-unit step = 33000 = 0.33 lot.
            // Loss at the stop = 33000 × 0.003 = 99 USD.
            OrderPlanModel plan = planner.CreatePlan(TestSignal.Short(close: 1.0850, stopLoss: 1.0880), entryPrice: 1.0850, accountEquity: 10000.0);

            Assert.True(plan.IsValid, plan.RejectReason);
            Assert.Equal(33000.0, plan.VolumeInUnits, precision: 6);
            Assert.Equal(0.33, plan.Lots, precision: 6);
            Assert.Equal(99.0, plan.EstimatedRiskMoney, precision: 6);
        }

        [Fact]
        public void eur_account_on_usd_quoted_gold_sizes_in_account_currency() {
            // 1 USD = 0.92 EUR, so one pip on one ounce is worth 0.01 × 0.92 = 0.0092 EUR.
            OrderPlanner planner = CreatePlanner(Xauusd(pipValuePerUnit: 0.0092));

            // riskMoney = 10000 EUR × 1% = 100 EUR. Stop 2405 − 2400 = 5 USD = 500 pips.
            // One unit loses 500 × 0.0092 = 4.6 EUR, so 100 / 4.6 = 21.74 -> 22 units = 0.22 lot.
            // Ignoring the conversion (100 / 5 USD) would give 20 units and under-risk the account.
            OrderPlanModel plan = planner.CreatePlan(TestSignal.Short(close: 2400.0, stopLoss: 2405.0), entryPrice: 2400.0, accountEquity: 10000.0);

            Assert.True(plan.IsValid, plan.RejectReason);
            Assert.Equal(22.0, plan.VolumeInUnits, precision: 6);
            Assert.Equal(0.22, plan.Lots, precision: 6);
            Assert.Equal(101.2, plan.EstimatedRiskMoney, precision: 6);
        }

        private static OrderPlanner CreatePlanner(FakeSymbolModel symbol, double riskPct = 1.0) =>
            new(symbol, TestSettings.Create(riskPct));

        private static FakeSymbolModel Xauusd(double pipValuePerUnit) {
            return new FakeSymbolModel {
                PipSize = 0.01, PipValue = pipValuePerUnit, LotSize = 100.0, VolumeStep = 1.0,
                VolumeInUnitsMin = 1.0, VolumeInUnitsMax = 1_000_000.0
            };
        }

        private static FakeSymbolModel Eurusd() {
            return new FakeSymbolModel {
                PipSize = 0.0001, PipValue = 0.0001, LotSize = 100_000.0, VolumeStep = 1000.0,
                VolumeInUnitsMin = 1000.0, VolumeInUnitsMax = 100_000_000.0
            };
        }
    }
}
