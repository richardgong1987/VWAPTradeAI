using System;

namespace cAlgo.Robots;

// One row of the trade CSV (see TradeCsvLogger). A trade writes two: its entry and its close.
//
// Only the row's own facts live here. The VWAP readings are not copied field by field: the row holds
// the entry's OrderPlanModel, and TradeCsvColumns reads them through it, so the entry row and the
// close row report the same values.
public class TradeRecordModel {
    public string Id { get; set; } = "";

    public string KeyLevel { get; set; } = "";

    public string Signal { get; set; } = "";

    public string Comment { get; set; } = "";

    public string Symbol { get; set; } = "";

    public string TimeFrame { get; set; } = "";

    public string Side { get; set; } = "";

    public DateTime EntryTime { get; set; }

    public double EntryPrice { get; set; }

    public double ClosePrice { get; set; }

    public double StopPrice { get; set; }

    public double TakeProfitPrice { get; set; }

    public double RiskPrice { get; set; }

    public double VolumeInUnits { get; set; }

    public string CloseReason { get; set; } = "";

    public double EntryAccountEquity { get; set; }

    public double CloseAccountEquity { get; set; }

    public double ProfitLoss { get; set; }

    // Null on an entry row.
    public DateTime? CloseTime { get; set; }

    public string PositionId { get; set; } = "";

    public string DealId { get; set; } = "";

    // Close rows only.
    public string FinalResult { get; set; } = "";

    public double ResultR { get; set; } = double.NaN;

    // The entry's order plan; null for a position opened before a restart.
    public OrderPlanModel EntryPlan { get; set; }
}
