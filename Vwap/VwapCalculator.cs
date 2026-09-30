using System;

namespace cAlgo.Robots;

// docs/vwap-v5-slim.pine 的累积口径：VWAP = Σ(hlc3 × volume) / Σ(volume)，每逢新的一天/一周
// 清零重算。调用方按 K 线顺序逐根 Append，周期边界由 VwapPeriod 判断后传进来。
// 这里只有算术，没有 cAlgo 依赖，所以是单元测试覆盖的部分。
public class VwapCalculator {
    private double _dailyPriceVolume;
    private double _dailyVolume;
    private double _weeklyPriceVolume;
    private double _weeklyVolume;

    // 上一根 K 线的当日 VWAP。新的一天开始时，它就是刚结束那一天的收盘 VWAP。
    private double _lastDailyVwap = double.NaN;

    // 对应 Pine 的 ta.valuewhen(newSessionD, vD[1], 0)：跨日时取一次，之后整天保持不变。
    private double _closedDailyVwap = double.NaN;

    public VwapSampleModel Append(double typicalPrice, double volume, bool isDayPeriodStart, bool isWeekPeriodStart) {
        if (isDayPeriodStart && !double.IsNaN(_lastDailyVwap))
            _closedDailyVwap = _lastDailyVwap;

        Accumulate(isDayPeriodStart, ref _dailyPriceVolume, ref _dailyVolume, typicalPrice, volume);
        Accumulate(isWeekPeriodStart, ref _weeklyPriceVolume, ref _weeklyVolume, typicalPrice, volume);

        _lastDailyVwap = ToVwap(_dailyPriceVolume, _dailyVolume, typicalPrice);

        return new VwapSampleModel {
            Daily = _lastDailyVwap,
            Weekly = ToVwap(_weeklyPriceVolume, _weeklyVolume, typicalPrice),
            PreviousDaily = _closedDailyVwap,
            IsDayPeriodStart = isDayPeriodStart,
            IsWeekPeriodStart = isWeekPeriodStart
        };
    }

    private static void Accumulate(bool isSessionStart, ref double priceVolume, ref double volume, double typicalPrice, double barVolume) {
        if (isSessionStart) {
            priceVolume = typicalPrice * barVolume;
            volume = barVolume;
            return;
        }

        priceVolume += typicalPrice * barVolume;
        volume += barVolume;
    }

    // 整段没有成交量时（休市、行情静止）除法失效，退回用这根 K 线的 hlc3，线不会断。
    private static double ToVwap(double priceVolume, double volume, double typicalPrice) {
        return volume > 0.0 ? priceVolume / volume : typicalPrice;
    }
}
