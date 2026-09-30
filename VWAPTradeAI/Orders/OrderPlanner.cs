namespace cAlgo.Robots;

// Turns an approved signal into a sized order plan, or a plan that says why it cannot be ordered:
//
//   stop         the pattern's own stop, pushed StopOffsetTicks further out so a wick that only
//                grazes the level does not take the trade out
//   take profit  TakeProfitR × the signal's risk (its close to the stop), measured from its close
//   entry        the market price when the user approves, which may be well after the signal
//   volume       what loses RiskPct% of equity between the entry and the stop, snapped to the
//                nearest volume step
//
// The stop and the target belong to the signal, so they stay where it put them however long the
// user takes to decide; only the entry, and with it the size, follows the market. Once price has
// reached either one, the trade is gone.
//
// Pure: it depends only on the ISymbolModel port, never on cAlgo, so it is unit tested.
public class OrderPlanner {
    private readonly ISymbolModel _symbol;
    private readonly TradeSettingsModel _settings;

    public OrderPlanner(ISymbolModel symbol, TradeSettingsModel settings) {
        _symbol = symbol;
        _settings = settings;
    }

    // entryPrice: what the market order fills at now, the ask for a long and the bid for a short.
    public OrderPlanModel CreatePlan(SignalModel signal, double entryPrice, double accountEquity) {
        bool isLong = signal.Level.Side == SignalSideModel.Buy;
        double stop = PlaceStop(signal, isLong);
        double signalRisk = DistanceInFavour(isLong, stop, signal.Close);

        // A stop on the close, or on its wrong side, leaves no risk to size or to set a target from.
        if (signalRisk <= 0.0)
            return Rejected(signal, $"The stop {stop} is not beyond the signal close {signal.Close}.");

        double target = _settings.TakeProfitR * signalRisk;
        double takeProfit = isLong ? signal.Close + target : signal.Close - target;
        double riskPrice = DistanceInFavour(isLong, stop, entryPrice);
        double rewardPrice = DistanceInFavour(isLong, entryPrice, takeProfit);

        if (riskPrice <= 0.0)
            return Rejected(signal, $"Price {entryPrice} has already reached the stop {stop}.");

        if (rewardPrice <= 0.0)
            return Rejected(signal, $"Price {entryPrice} has already reached the take profit {takeProfit}.");

        double stopLossPips = riskPrice / _symbol.PipSize;
        double riskMoney = RiskBudget.Calculate(accountEquity, _settings.RiskPct);
        double volume = SizeVolume(riskMoney, stopLossPips);
        string volumeProblem = FindVolumeProblem(volume);

        if (volumeProblem != null)
            return Rejected(signal, volumeProblem);

        return new OrderPlanModel {
            IsValid = true,
            Signal = signal,
            Direction = isLong ? TradeDirectionModel.Long : TradeDirectionModel.Short,
            EntryPrice = entryPrice,
            StopPrice = stop,
            TakeProfitPrice = takeProfit,
            RiskPrice = riskPrice,
            StopLossPips = stopLossPips,
            TakeProfitPips = rewardPrice / _symbol.PipSize,
            Lots = volume / _symbol.LotSize,
            VolumeInUnits = volume,
            AccountEquity = accountEquity,
            RiskMoney = riskMoney,
            EstimatedRiskMoney = _symbol.AmountRisked(volume, stopLossPips)
        };
    }

    private double PlaceStop(SignalModel signal, bool isLong) {
        double stopOffset = _symbol.TickSize * _settings.StopOffsetTicks;
        return isLong ? signal.StopLoss - stopOffset : signal.StopLoss + stopOffset;
    }

    // How far `to` lies beyond `from` in the trade's favour: up for a long, down for a short.
    // Negative when it lies against it.
    private static double DistanceInFavour(bool isLong, double from, double to) {
        return isLong ? to - from : from - to;
    }

    // The volume whose loss at the stop equals the risk budget. PipValue is a pip's worth in the
    // account currency, so the budget stays in that currency: dividing by the raw price distance
    // instead once sized a EUR account on USD-quoted XAUUSD ~15% too small.
    private double SizeVolume(double riskMoney, double stopLossPips) {
        double lossPerUnit = stopLossPips * _symbol.PipValue;
        double idealVolume = lossPerUnit > 0.0 ? riskMoney / lossPerUnit : 0.0;
        return _symbol.NormalizeVolumeInUnits(idealVolume);
    }

    // Null when the broker accepts this volume. No risk budget sizes to 0, which lands here too.
    private string FindVolumeProblem(double volume) {
        if (volume < _symbol.VolumeInUnitsMin)
            return $"Calculated volume is below broker minimum. Volume={volume}, Min={_symbol.VolumeInUnitsMin}";

        if (volume > _symbol.VolumeInUnitsMax)
            return $"Calculated volume is above broker maximum. Volume={volume}, Max={_symbol.VolumeInUnitsMax}";

        return null;
    }

    private static OrderPlanModel Rejected(SignalModel signal, string reason) {
        return new OrderPlanModel { Signal = signal, RejectReason = reason };
    }
}
