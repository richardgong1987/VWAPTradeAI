using System;

namespace cAlgo.Robots;

// A position on this symbol that has just closed, with the account as it stood at that moment
// (see IBroker). Pure data.
public class PositionCloseModel {
    public int PositionId { get; set; }

    // The order label; OrderExecutor recognises its own positions by it.
    public string Label { get; set; } = "";

    public TradeDirectionModel Direction { get; set; }

    public DateTime EntryTime { get; set; }

    public double EntryPrice { get; set; }

    // 0 when neither the trade history nor the deals give a fill price.
    public double ClosePrice { get; set; }

    // Server time (Japan time) of the close.
    public DateTime CloseTime { get; set; }

    public PositionCloseReasonModel CloseReason { get; set; }

    public double VolumeInUnits { get; set; }

    public double NetProfit { get; set; }

    // Account equity right after the close.
    public double AccountEquity { get; set; }

    // The closing deal's ID; empty when the broker reports no deal.
    public string DealId { get; set; } = "";
}
