using cAlgo.API;
using cAlgo.API.Internals;

namespace cAlgo.Robots;

// Pattern: Adapter. Adapts the real cTrader Symbol to ISymbolModel. Volume is rounded to the
// nearest tradable step so a stop-out risks as close to the budget as the step allows.
public class CAlgoSymbolModel : ISymbolModel {
    private readonly Symbol _symbol;

    public CAlgoSymbolModel(Symbol symbol) {
        _symbol = symbol;
    }

    public double PipSize => _symbol.PipSize;
    public double TickSize => _symbol.TickSize;
    public double LotSize => _symbol.LotSize;
    public double VolumeInUnitsMin => _symbol.VolumeInUnitsMin;
    public double VolumeInUnitsMax => _symbol.VolumeInUnitsMax;
    public double PipValue => _symbol.PipValue;

    public double NormalizeVolumeInUnits(double volumeInUnits) {
        return _symbol.NormalizeVolumeInUnits(volumeInUnits, RoundingMode.ToNearest);
    }

    public double AmountRisked(double volumeInUnits, double stopLossPips) {
        return _symbol.AmountRisked(volumeInUnits, stopLossPips);
    }
}
