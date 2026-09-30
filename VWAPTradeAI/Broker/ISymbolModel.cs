namespace cAlgo.Robots;

// The subset of cTrader symbol facts the planner needs, expressed without any
// cAlgo.API type.
//
// Pattern: Adapter — this is the target interface; CAlgoSymbolModel adapts the real Symbol to it
// and the tests supply a fake.
public interface ISymbolModel {
    double PipSize { get; }

    // 最小报价变动。止损偏移与保本偏移都按 tick 计。
    double TickSize { get; }

    double LotSize { get; }
    double VolumeInUnitsMin { get; }
    double VolumeInUnitsMax { get; }

    // Monetary value of one pip for one unit, in the account's deposit currency. This is
    // what makes sizing currency-correct: for a EUR account trading USD-quoted XAUUSD it
    // already folds in the USD->EUR conversion, which a raw price distance does not.
    double PipValue { get; }

    // Snap a raw volume to the nearest tradable step (so sizing lands as close to the
    // risk budget as the step allows, rather than always rounding down).
    double NormalizeVolumeInUnits(double volumeInUnits);
    double AmountRisked(double volumeInUnits, double stopLossPips);
}
