namespace cAlgo.Robots;

// 单笔可亏多少钱：账户权益的 RiskPct%，例如 10000 的 1% = 100。仓位大小由它和止损距离倒推
// （见 OrderPlanner）。权益或百分比缺一不可，任一非正就是「这一笔不冒险」，也就下不了单。
//
// 纯算术，没有 cAlgo 依赖，有单元测试。
public static class RiskBudget {
    public static double Calculate(double equity, double riskPct) {
        if (equity <= 0.0 || riskPct <= 0.0)
            return 0.0;

        return equity * riskPct / 100.0;
    }
}
