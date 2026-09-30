using cAlgo.Robots;

namespace VWAPTradeAI.Tests {
    // Settings for tests: 1% risk, 2R take profit and no stop offset, so what is under test is only
    // what the test states.
    internal static class TestSettings {
        public static TradeSettingsModel NoStopOffset() => Create();

        public static TradeSettingsModel WithStopOffsetTicks(int stopOffsetTicks) => Create(stopOffsetTicks: stopOffsetTicks);

        public static TradeSettingsModel Create(double riskPct = 1.0, double takeProfitR = 2.0, int stopOffsetTicks = 0) =>
            new(riskPct, takeProfitR, stopOffsetTicks);
    }
}
