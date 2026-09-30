namespace cAlgo.Robots;

// 用户填的风控设置，从组合根一路传给 planner。
public class TradeSettingsModel {
    public TradeSettingsModel(double riskPct, double takeProfitR, int stopOffsetTicks) {
        RiskPct = riskPct;
        TakeProfitR = takeProfitR;
        StopOffsetTicks = stopOffsetTicks;
    }

    // 单笔可亏的账户权益百分比。
    public double RiskPct { get; }

    // 止盈距离 = TakeProfitR × 止损距离，开仓时就定死、挂在订单上交给券商执行。
    public double TakeProfitR { get; }

    // 止损在形态价位之外再让开这么多个 tick，免得贴着影线被扫。
    public int StopOffsetTicks { get; }
}
