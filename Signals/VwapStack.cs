namespace cAlgo.Robots;

// 排列（Strong）：收盘价 > 日 VWAP > 周 VWAP 只开多；收盘价 < 日 VWAP < 周 VWAP 只开空。
// 不是 Strong 就没有信号。纯比较，没有 cAlgo 依赖，有单元测试。
public static class VwapStack {
    // Strong: the stack alone. Buy or Sell is the only side a signal may take.
    public static SignalSideModel ResolveSide(double close, double dailyVwap, double weeklyVwap) {
        if (!IsUsable(close) || !IsUsable(dailyVwap) || !IsUsable(weeklyVwap))
            return SignalSideModel.None;

        if (close > dailyVwap && dailyVwap > weeklyVwap)
            return SignalSideModel.Buy;

        if (close < dailyVwap && dailyVwap < weeklyVwap)
            return SignalSideModel.Sell;

        return SignalSideModel.None;
    }

    private static bool IsUsable(double value) {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
