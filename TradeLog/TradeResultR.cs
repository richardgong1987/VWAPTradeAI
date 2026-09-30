namespace cAlgo.Robots;

// 一笔交易实际打出来的 R，按开仓时的初始风险价格距离算：
//
//   多头 = (平仓价 − 入场价) / RiskPrice
//   空头 = (入场价 − 平仓价) / RiskPrice
//
// 止损被打到就是 −1.00，吃到 2R 止盈就是 +2.00，保本走人是 0.00。
//
// 用价格距离而不是净盈亏：净盈亏含手续费和隔夜利息，1R 止损会写成 −1.03 这样的数，
// 以后比较 1R / 1.5R / 2R / 保本 几种出场方式时对不齐。净盈亏在「平仓盈亏」列里另有一份。
// 纯算术，没有 cAlgo 依赖，有单元测试。
public static class TradeResultR {
    public static double Calculate(TradeDirectionModel directionModel, double entryPrice, double closePrice, double riskPrice) {
        if (!IsUsable(entryPrice) || !IsUsable(closePrice) || !IsUsable(riskPrice) || riskPrice <= 0.0)
            return double.NaN;

        double move = directionModel == TradeDirectionModel.Long ? closePrice - entryPrice : entryPrice - closePrice;
        return move / riskPrice;
    }

    private static bool IsUsable(double value) {
        return !double.IsNaN(value) && !double.IsInfinity(value) && value > 0.0;
    }
}
