using System;
using System.Collections.Generic;
using cAlgo.API;

namespace cAlgo.Robots;

// 全图的 VWAP 序列：读 K 线、按 VwapPeriod 切分日/周，把每根已收线 K 线的三个 VWAP 取值缓存下来。
// 画线（VwapLines）和找信号（SignalDetector）都从这里取值，累积口径只有这一份。
//
// 只算已经收线的 K 线：OnBar 触发时最后一根刚开盘、值还会变。调用方每根 K 线调一次 Update()，
// 它只补算新收线的那几根，重复调用无副作用。
public class VwapSeries {
    private readonly Bars _bars;
    private readonly VwapCalculator _calculator = new();
    private readonly List<VwapSampleModel> _samples = new();

    public VwapSeries(Bars bars) {
        _bars = bars;
    }

    // 已算出取值的 K 线根数；下标 0..Count-1 可以取值。
    public int Count => _samples.Count;

    public VwapSampleModel this[int barIndex] => _samples[barIndex];

    public void Update() {
        int closedBarCount = _bars.Count - 1;

        for (int barIndex = _samples.Count; barIndex < closedBarCount; barIndex++) {
            _samples.Add(CreateSample(barIndex));
        }
    }

    // Every bar accumulates; no stretch of the day is skipped.
    private VwapSampleModel CreateSample(int barIndex) {
        DateTime openTime = _bars.OpenTimes[barIndex];
        DateTime? previousOpenTime = barIndex > 0 ? _bars.OpenTimes[barIndex - 1] : (DateTime?)null;

        VwapSampleModel sample = _calculator.Append(_bars.TypicalPrices[barIndex], _bars.TickVolumes[barIndex],
            VwapPeriod.IsNewDay(openTime, previousOpenTime), VwapPeriod.IsNewWeek(openTime, previousOpenTime));

        sample.OpenTime = openTime;
        return sample;
    }
}
