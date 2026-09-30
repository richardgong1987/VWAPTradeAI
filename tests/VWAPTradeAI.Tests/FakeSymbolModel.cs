using System;
using cAlgo.Robots;

namespace VWAPTradeAI.Tests {
    // Deterministic stand-in for a cTrader Symbol. Volume snaps to the nearest whole step,
    // matching RoundingMode.ToNearest in CAlgoSymbolModel. The epsilon absorbs floating-point
    // noise such as 19999.9999999 so an exact budget is not pushed a step off.
    internal sealed class FakeSymbolModel : ISymbolModel {
        public double PipSize { get; init; }
        public double TickSize { get; init; } = 0.01;
        public double PipValue { get; init; }
        public double LotSize { get; init; }
        public double VolumeStep { get; init; } = 1.0;
        public double VolumeInUnitsMin { get; init; } = 1.0;
        public double VolumeInUnitsMax { get; init; } = 1_000_000.0;

        public double NormalizeVolumeInUnits(double volumeInUnits) =>
            Math.Round(volumeInUnits / VolumeStep + 1e-9, MidpointRounding.AwayFromZero) * VolumeStep;

        public double AmountRisked(double volumeInUnits, double stopLossPips) => volumeInUnits * stopLossPips * PipValue;
    }
}
