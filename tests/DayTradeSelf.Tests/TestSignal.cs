using System;
using cAlgo.Robots;

namespace VWAPTradeAI.Tests {
    // Builds the signal the planner consumes: the level it touched plus the closed bar that hit it.
    // Tests only ever state the bar's close and the pattern's stop; the target comes from those and
    // the planner's settings (see TestSettings), the entry from the price when it is approved.
    internal static class TestSignal {
        public static SignalModel Short(double close, double stopLoss) => ForSide(SignalSideModel.Sell, close, stopLoss);

        public static SignalModel Long(double close, double stopLoss) => ForSide(SignalSideModel.Buy, close, stopLoss);

        private static SignalModel ForSide(SignalSideModel side, double close, double stopLoss) {
            return new SignalModel {
                Level = new TradeLevelModel("VWAP", side, price: close),
                Label = side == SignalSideModel.Sell ? "S_Pin_1" : "L_Pin_1",
                Close = close,
                StopLoss = stopLoss,
                High = Math.Max(close, stopLoss),
                Low = Math.Min(close, stopLoss),
                BarIndex = 42,
                BarTime = new DateTime(2026, 9, 18, 13, 0, 0, DateTimeKind.Utc)
            };
        }
    }
}
