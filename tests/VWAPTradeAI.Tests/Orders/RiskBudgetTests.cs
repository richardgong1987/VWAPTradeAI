using cAlgo.Robots;
using Xunit;

namespace VWAPTradeAI.Tests.Orders {
    // How much account currency one trade may lose. Everything about position size follows from it.
    public class RiskBudgetTests {
        [Fact]
        public void the_budget_is_a_percentage_of_equity() {
            Assert.Equal(100.0, RiskBudget.Calculate(equity: 10000.0, riskPct: 1.0), precision: 6);
        }

        [Theory]
        [InlineData(0.0, 1.0)]
        [InlineData(10000.0, 0.0)]
        [InlineData(-1.0, 1.0)]
        public void a_missing_equity_or_risk_percentage_risks_nothing(double equity, double riskPct) {
            // Risk % left at 0 is how the user switches trading off, so it must budget nothing
            // rather than fall back to a default.
            Assert.Equal(0.0, RiskBudget.Calculate(equity, riskPct), precision: 6);
        }
    }
}
