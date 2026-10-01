namespace cAlgo.Robots;

// What AiChartshots needs from the chart on screen: whether it is visible, which bars it shows,
// a way to scroll it, and a picture of it.
//
// Pattern: Adapter. CAlgoChartCamera adapts cTrader's Chart to it. An interface with one real
// implementation is justified only because the tests cannot load cAlgo.API, and the waiting logic
// in AiChartshots is worth testing.
public interface IChartCamera {
    bool IsVisible { get; }

    int FirstVisibleBarIndex { get; }

    // The newest bar the chart shows. Lower than the cBot's newest bar while the chart lags behind
    // (a fast visual backtest), or when someone has scrolled the chart back.
    int LastVisibleBarIndex { get; }

    void ScrollXTo(int barIndex);

    // PNG bytes; null when the chart is not visible.
    byte[] TakeChartshot();
}
