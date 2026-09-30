namespace cAlgo.Robots;

// The sized order the planner produces from a signal, or the reason it cannot be ordered. Sizing
// only, plus a reference to the signal; don't copy signal fields into it. Pure data.
public class OrderPlanModel {
    public bool IsValid { get; set; }
    public string RejectReason { get; set; } = "";

    public TradeDirectionModel Direction { get; set; }

    public double EntryPrice { get; set; }
    public double StopPrice { get; set; }
    public double TakeProfitPrice { get; set; }
    public double RiskPrice { get; set; }
    public double StopLossPips { get; set; }
    public double TakeProfitPips { get; set; }

    public double Lots { get; set; }
    public double VolumeInUnits { get; set; }

    public double AccountEquity { get; set; }
    public double RiskMoney { get; set; }
    public double EstimatedRiskMoney { get; set; }

    // The signal this plan was made from. The trade CSV reads the entry's VWAP readings through
    // it, for the entry row and again for the close row (by then the live VWAP has moved on).
    public SignalModel Signal { get; set; }
}
